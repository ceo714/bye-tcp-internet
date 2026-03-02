using System.Diagnostics;
using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using ByeTcp.Core.Interfaces;
using ByeTcp.Core.Models;

namespace ByeTcp.Core.Monitors;

/// <summary>
/// Монитор сетевых метрик на основе ICMP и Performance Counters
/// </summary>
public sealed class IcmpNetworkMonitor : INetworkMonitor
{
    private readonly ILogger<IcmpNetworkMonitor> _logger;
    private readonly NetworkMonitorConfig _config;
    private readonly List<double> _rttHistory = new();
    private readonly object _lock = new();
    
    private Timer? _metricsTimer;
    private CancellationTokenSource? _shutdownCts;
    private bool _disposed;
    private NetworkMetrics _currentMetrics = new();
    
    // Performance counters для TCP статистики
    private PerformanceCounter? _tcpRetransmissionsCounter;
    private PerformanceCounter? _tcpSegmentsSentCounter;
    private long _lastRetransmissions = 0;
    private long _lastSegmentsSent = 0;
    private DateTime _lastCounterRead = DateTime.UtcNow;

    public event EventHandler<NetworkMetrics>? MetricsUpdated;

    public IcmpNetworkMonitor(ILogger<IcmpNetworkMonitor> logger, NetworkMonitorConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? NetworkMonitorConfig.Default;
        
        InitializePerformanceCounters();
    }

    /// <summary>
    /// Инициализация Performance Counters
    /// </summary>
    private void InitializePerformanceCounters()
    {
        try
        {
            // TCPv4 Performance Counters
            _tcpRetransmissionsCounter = new PerformanceCounter(
                "TCPv4", 
                "Segments Retransmitted/sec", 
                readOnly: true
            );
            
            _tcpSegmentsSentCounter = new PerformanceCounter(
                "TCPv4", 
                "Segments Sent/sec", 
                readOnly: true
            );
            
            _logger.LogDebug("Performance Counters инициализированы");
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось инициализировать Performance Counters. Метрики TCP будут неполными.");
        }
    }

    /// <summary>
    /// Запуск мониторинга сетевых метрик
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(IcmpNetworkMonitor));
        
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        _logger.LogInformation("🌐 Запуск сетевого монитора (интервал: {Interval} сек)", 
            _config.MetricsIntervalSeconds);
        
        // Запускаем первый замер немедленно
        _ = CollectMetricsAsync();
        
        // Устанавливаем периодический сбор метрик
        _metricsTimer = new Timer(
            async _ => await CollectMetricsAsync(),
            null,
            TimeSpan.FromSeconds(_config.MetricsIntervalSeconds),
            TimeSpan.FromSeconds(_config.MetricsIntervalSeconds)
        );
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Остановка мониторинга
    /// </summary>
    public Task StopAsync()
    {
        _logger.LogInformation("Остановка сетевого монитора");
        
        _shutdownCts?.Cancel();
        _metricsTimer?.Dispose();
        _metricsTimer = null;
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Сбор сетевых метрик
    /// </summary>
    private async Task CollectMetricsAsync()
    {
        var stopwatch = Stopwatch.StartNew();
        
        try
        {
            // Измеряем RTT к целевому хосту
            var rtt = await MeasureRttAsync(_config.TargetHost);
            
            // Читаем TCP Performance Counters
            var (retransmissions, segmentsSent) = ReadTcpCounters();
            
            // Вычисляем джиттер (стандартное отклонение RTT)
            var jitter = CalculateJitter(rtt);
            
            // Вычисляем packet loss (на основе последних N измерений)
            var packetLoss = CalculatePacketLoss();
            
            // Вычисляем rate ретрансмиссий
            var retransmissionRate = CalculateRetransmissionRate(retransmissions, segmentsSent);
            
            // Обновляем текущие метрики
            var metrics = new NetworkMetrics
            {
                RttMs = rtt,
                JitterMs = jitter,
                PacketLossPercent = packetLoss,
                TcpRetransmissions = retransmissions,
                BandwidthMbps = EstimateBandwidth(segmentsSent),
                Timestamp = DateTime.UtcNow
            };
            
            lock (_lock)
            {
                _currentMetrics = metrics;
                
                // Добавляем RTT в историю для вычисления джиттера
                _rttHistory.Add(rtt);
                if (_rttHistory.Count > _config.RttHistorySize)
                {
                    _rttHistory.RemoveAt(0);
                }
            }
            
            _lastRetransmissions = retransmissions;
            _lastSegmentsSent = segmentsSent;
            _lastCounterRead = DateTime.UtcNow;
            
            _logger.LogDebug(
                "📊 Метрики: RTT={Rtt:F1}ms, Jitter={Jitter:F2}ms, Loss={Loss:F2}%, Retx={Retx}/sec",
                metrics.RttMs, metrics.JitterMs, metrics.PacketLossPercent, retransmissionRate
            );
            
            MetricsUpdated?.Invoke(this, metrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сбора сетевых метрик");
        }
        finally
        {
            stopwatch.Stop();
            if (stopwatch.ElapsedMilliseconds > _config.MetricsIntervalSeconds * 1000)
            {
                _logger.LogWarning("⚠️ Сбор метрик занял слишком много времени: {Elapsed}ms", 
                    stopwatch.ElapsedMilliseconds);
            }
        }
    }

    /// <summary>
    /// Измерение RTT через ICMP Ping
    /// </summary>
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

    /// <summary>
    /// Чтение TCP Performance Counters
    /// </summary>
    private (long Retransmissions, long SegmentsSent) ReadTcpCounters()
    {
        try
        {
            var retransmissions = _tcpRetransmissionsCounter?.NextValue() ?? 0;
            var segmentsSent = _tcpSegmentsSentCounter?.NextValue() ?? 0;
            
            return ((long)retransmissions, (long)segmentsSent);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Ошибка чтения Performance Counters");
            return (0, 0);
        }
    }

    /// <summary>
    /// Вычисление джиттера (стандартное отклонение RTT)
    /// </summary>
    private double CalculateJitter(double currentRtt)
    {
        lock (_lock)
        {
            if (_rttHistory.Count < 2)
                return 0;
            
            // Простое вычисление джиттера как разницы между последними измерениями
            var lastRtt = _rttHistory[^1];
            var jitter = Math.Abs(currentRtt - lastRtt);
            
            // Более точное вычисление через стандартное отклонение
            if (_rttHistory.Count >= 5)
            {
                var mean = _rttHistory.Average();
                var variance = _rttHistory.Select(x => Math.Pow(x - mean, 2)).Average();
                jitter = Math.Sqrt(variance);
            }
            
            return jitter;
        }
    }

    /// <summary>
    /// Вычисление packet loss на основе неудачных ping
    /// </summary>
    private double CalculatePacketLoss()
    {
        // Упрощенная реализация - в production нужно хранить историю ping результатов
        // Здесь возвращаем 0, если последний ping успешен
        return 0;
    }

    /// <summary>
    /// Вычисление rate ретрансмиссий
    /// </summary>
    private double CalculateRetransmissionRate(long currentRetransmissions, long currentSegmentsSent)
    {
        var now = DateTime.UtcNow;
        var elapsed = (now - _lastCounterRead).TotalSeconds;
        
        if (elapsed <= 0 || _lastRetransmissions == 0)
            return 0;
        
        var retransDelta = currentRetransmissions - _lastRetransmissions;
        return retransDelta / elapsed;
    }

    /// <summary>
    /// Оценка пропускной способности на основе сегментов
    /// </summary>
    private double EstimateBandwidth(long segmentsPerSecond)
    {
        // Грубая оценка: segments * 1460 bytes (MSS) * 8 bits / 1_000_000
        return segmentsPerSecond * 1460 * 8 / 1_000_000;
    }

    /// <summary>
    /// Получить текущие метрики
    /// </summary>
    public NetworkMetrics GetCurrentMetrics()
    {
        lock (_lock)
        {
            return _currentMetrics;
        }
    }

    /// <summary>
    /// Освобождение ресурсов
    /// </summary>
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
    public int MetricsIntervalSeconds { get; init; } = 5;
    public int RttHistorySize { get; init; } = 20;
    
    public static NetworkMonitorConfig Default => new();
}
