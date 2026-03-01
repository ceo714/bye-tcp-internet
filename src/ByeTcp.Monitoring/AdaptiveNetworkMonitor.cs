using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Monitoring;

/// <summary>
/// Адаптивный монитор сети
/// 
/// Особенности:
/// - Адаптивный интервал опроса в зависимости от режима
/// - Event-driven обновления при изменении метрик
/// - Кэширование результатов для избежания redundant вычислений
/// - Определение качества сети (Excellent/Good/Fair/Poor/Critical)
/// </summary>
public sealed class AdaptiveNetworkMonitor : INetworkMonitor, IDisposable
{
    private readonly ILogger<AdaptiveNetworkMonitor> _logger;
    private readonly NetworkMonitorConfig _config;
    private readonly ConcurrentQueue<double> _rttHistory = new();
    private readonly object _lock = new();
    
    private Timer? _metricsTimer;
    private CancellationTokenSource? _shutdownCts;
    private bool _disposed;
    
    private NetworkMetrics _currentMetrics = new();
    private AdaptiveMode _currentMode = AdaptiveMode.Normal;
    private NetworkQuality _currentQuality = NetworkQuality.Unknown;
    
    // Performance counters
    private PerformanceCounter? _tcpRetransmissionsCounter;
    private PerformanceCounter? _tcpSegmentsSentCounter;
    private long _lastRetransmissions;
    private long _lastSegmentsSent;
    private DateTime _lastCounterRead = DateTime.UtcNow;
    
    // Адаптивная логика
    private int _consecutivePoorReadings;
    private DateTime _lastModeChange = DateTime.UtcNow;

    public event EventHandler<NetworkMetrics>? MetricsUpdated;

    public AdaptiveNetworkMonitor(
        ILogger<AdaptiveNetworkMonitor> logger,
        NetworkMonitorConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? NetworkMonitorConfig.Default;
        
        InitializePerformanceCounters();
    }

    private void InitializePerformanceCounters()
    {
        try
        {
            _tcpRetransmissionsCounter = new PerformanceCounter(
                "TCPv4", "Segments Retransmitted/sec", readOnly: true);
            _tcpSegmentsSentCounter = new PerformanceCounter(
                "TCPv4", "Segments Sent/sec", readOnly: true);
            
            _logger.LogDebug("Performance Counters инициализированы");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось инициализировать Performance Counters");
        }
    }

    public Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger.LogInformation("🌐 Запуск Adaptive Network Monitor (режим: {Mode})", _currentMode);
        
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Первый замер немедленно
        _ = CollectMetricsAsync();
        
        // Периодический сбор с адаптивным интервалом
        StartTimer();
        
        return Task.CompletedTask;
    }

    private void StartTimer()
    {
        var interval = GetIntervalForMode(_currentMode);
        
        _metricsTimer?.Dispose();
        _metricsTimer = new Timer(
            async _ => await CollectMetricsAsync(),
            null,
            interval,
            interval
        );
        
        _logger.LogDebug("Таймер установлен на {Interval} сек", interval.TotalSeconds);
    }

    private TimeSpan GetIntervalForMode(AdaptiveMode mode) => mode switch
    {
        AdaptiveMode.Idle => TimeSpan.FromSeconds(60),
        AdaptiveMode.Normal => TimeSpan.FromSeconds(30),
        AdaptiveMode.Active => TimeSpan.FromSeconds(10),
        AdaptiveMode.Critical => TimeSpan.FromSeconds(5),
        _ => TimeSpan.FromSeconds(30)
    };

    private async Task CollectMetricsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Измерение RTT
            var rtt = await MeasureRttAsync(_config.TargetHost);
            
            // Чтение TCP counters
            var (retransmissions, segmentsSent) = ReadTcpCounters();
            
            // Вычисление джиттера
            var jitter = CalculateJitter(rtt);
            
            // Вычисление packet loss (на основе истории)
            var packetLoss = CalculatePacketLoss();
            
            // Определение качества сети
            var quality = DetermineNetworkQuality(rtt, packetLoss);
            
            // Обновление состояния
            var metrics = new NetworkMetrics
            {
                RttMs = rtt,
                JitterMs = jitter,
                PacketLossPercent = packetLoss,
                TcpRetransmissionsPerSec = retransmissions,
                BandwidthMbps = EstimateBandwidth(segmentsSent),
                Timestamp = DateTime.UtcNow,
                Quality = quality
            };
            
            lock (_lock)
            {
                _currentMetrics = metrics;
                _currentQuality = quality;
                
                // История RTT для джиттера
                _rttHistory.Enqueue(rtt);
                if (_rttHistory.Count > _config.RttHistorySize)
                {
                    _rttHistory.TryDequeue(out _);
                }
            }
            
            _lastRetransmissions = retransmissions;
            _lastSegmentsSent = segmentsSent;
            _lastCounterRead = DateTime.UtcNow;
            
            // Адаптивное переключение режима
            AdaptMode(metrics);
            
            _logger.LogDebug(
                "📊 Метрики: RTT={Rtt:F1}ms, Jitter={Jitter:F2}ms, Loss={Loss:F2}%, Quality={Quality}",
                metrics.RttMs, metrics.JitterMs, metrics.PacketLossPercent, metrics.Quality);
            
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сбора сетевых метрик");
            
            // При ошибках переключаемся в Critical режим для более частой диагностики
            SetAdaptiveMode(AdaptiveMode.Critical);
        }
        finally
        {
            stopwatch.Stop();
            
            if (stopwatch.ElapsedMilliseconds > GetIntervalForMode(_currentMode).TotalMilliseconds * 0.8)
            {
                _logger.LogWarning("⚠️ Сбор метрик занял {Elapsed}ms (>{Pct}% интервала)", 
                    stopwatch.ElapsedMilliseconds, 80);
            }
        }
    }

    private async Task<double> MeasureRttAsync(string host)
    {
        using var ping = new Ping();
        var buffer = new byte[32];
        var options = new PingOptions(64, true);
        
        try
        {
            var reply = await ping.SendPingAsync(host, _config.PingTimeoutMs, buffer, options);
            
            if (reply.Status == IPStatus.Success)
            {
                return reply.RoundtripTime;
            }
            else
            {
                _logger.LogDebug("Ping не удался: {Status}", reply.Status);
                return -1; // Индикатор ошибки
            }
        }
        catch (PingException ex)
        {
            _logger.LogDebug(ex, "Ping ошибка для {Host}", host);
            return -1;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка Ping для {Host}", host);
            return -1;
        }
    }

    private (long Retransmissions, long SegmentsSent) ReadTcpCounters()
    {
        try
        {
            var retrans = (long)(_tcpRetransmissionsCounter?.NextValue() ?? 0);
            var segments = (long)(_tcpSegmentsSentCounter?.NextValue() ?? 0);
            return (retrans, segments);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ошибка чтения Performance Counters");
            return (0, 0);
        }
    }

    private double CalculateJitter(double currentRtt)
    {
        lock (_lock)
        {
            if (_rttHistory.Count < 2)
                return 0;
            
            // Простое вычисление как разница между последними измерениями
            var history = _rttHistory.ToArray();
            var lastRtt = history[^1];
            return Math.Abs(currentRtt - lastRtt);
        }
    }

    private double CalculatePacketLoss()
    {
        // Упрощенная реализация на основе неудачных ping
        // В production нужно хранить историю результатов
        lock (_lock)
        {
            var history = _rttHistory.ToArray();
            if (history.Length == 0)
                return 0;
            
            var failed = history.Count(r => r < 0);
            return (double)failed / history.Length * 100;
        }
    }

    private NetworkQuality DetermineNetworkQuality(double rtt, double packetLoss)
    {
        if (rtt < 0) return NetworkQuality.Unknown; // Ошибка измерения
        
        if (rtt < 20 && packetLoss < 0.1)
            return NetworkQuality.Excellent;
        
        if (rtt < 50 && packetLoss < 1)
            return NetworkQuality.Good;
        
        if (rtt < 100 && packetLoss < 3)
            return NetworkQuality.Fair;
        
        if (rtt < 200 && packetLoss < 10)
            return NetworkQuality.Poor;
        
        return NetworkQuality.Critical;
    }

    private void AdaptMode(NetworkMetrics metrics)
    {
        // Минимальный интервал между переключениями режима
        if (DateTime.UtcNow - _lastModeChange < TimeSpan.FromSeconds(30))
            return;
        
        var targetMode = metrics.Quality switch
        {
            NetworkQuality.Excellent or NetworkQuality.Good => AdaptiveMode.Idle,
            NetworkQuality.Fair => AdaptiveMode.Normal,
            NetworkQuality.Poor => AdaptiveMode.Active,
            NetworkQuality.Critical => AdaptiveMode.Critical,
            _ => AdaptiveMode.Normal
        };
        
        // Hysteresis: требуем несколько consecutive poor readings для ухудшения режима
        if (targetMode > _currentMode)
        {
            _consecutivePoorReadings++;
            if (_consecutivePoorReadings < 3)
                return;
        }
        else
        {
            _consecutivePoorReadings = 0;
        }
        
        if (targetMode != _currentMode)
        {
            _logger.LogInformation("🔄 Смена режима мониторинга: {From} → {To}", _currentMode, targetMode);
            SetAdaptiveMode(targetMode);
            _lastModeChange = DateTime.UtcNow;
        }
    }

    public void SetAdaptiveMode(AdaptiveMode mode)
    {
        if (_currentMode == mode)
            return;
        
        _logger.LogDebug("Установка режима мониторинга: {Mode}", mode);
        _currentMode = mode;
        StartTimer();
    }

    public NetworkMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return _currentMetrics;
        }
    }

    public NetworkQuality GetCurrentQuality()
    {
        return _currentQuality;
    }

    private double EstimateBandwidth(long segmentsPerSecond)
    {
        // Грубая оценка: segments * 1460 bytes (MSS) * 8 bits / 1_000_000
        return segmentsPerSecond * 1460 * 8 / 1_000_000;
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Остановка Adaptive Network Monitor");
        
        _shutdownCts?.Cancel();
        _metricsTimer?.Dispose();
        _metricsTimer = null;
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        StopAsync().Wait();
        
        _tcpRetransmissionsCounter?.Dispose();
        _tcpSegmentsSentCounter?.Dispose();
        _shutdownCts?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Конфигурация сетевого монитора
/// </summary>
public record NetworkMonitorConfig
{
    public string TargetHost { get; init; } = "8.8.8.8";
    public int PingTimeoutMs { get; init; } = 3000;
    public int RttHistorySize { get; init; } = 20;
    
    public static NetworkMonitorConfig Default => new();
}
