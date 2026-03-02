using System.Net.NetworkInformation;
using Microsoft.Extensions.Logging;
using ByeTcp.Core.Interfaces;
using ByeTcp.Core.Models;

namespace ByeTcp.Core.Diagnostics;

/// <summary>
/// Диагностический движок для измерения сетевых метрик
/// </summary>
public sealed class DiagnosticsEngine : IDiagnosticsEngine
{
    private readonly ILogger<DiagnosticsEngine> _logger;
    private readonly DiagnosticsConfig _config;
    private readonly List<Ping> _pingPool = new();
    private readonly object _lock = new();
    
    private Timer? _diagnosticsTimer;
    private bool _disposed;
    private CancellationTokenSource? _shutdownCts;
    private DiagnosticResult? _lastResult;

    public event EventHandler<DiagnosticResult>? DiagnosticsCompleted;

    public DiagnosticsEngine(ILogger<DiagnosticsEngine> logger, DiagnosticsConfig? config = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _config = config ?? DiagnosticsConfig.Default;
        
        // Создаем пул Ping объектов для повторного использования
        for (int i = 0; i < 3; i++)
        {
            var ping = new Ping();
            ping.PingCompleted += (s, e) => ReturnPingToPool(ping);
            _pingPool.Add(ping);
        }
    }

    /// <summary>
    /// Запуск периодической диагностики
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(DiagnosticsEngine));
        
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        _logger.LogInformation("🔍 Запуск диагностического движка (интервал: {Interval} сек)", 
            _config.DiagnosticsIntervalSeconds);
        
        // Запускаем первую диагностику немедленно
        _ = RunOnceAsync();
        
        // Устанавливаем периодический запуск
        _diagnosticsTimer = new Timer(
            async _ => await RunOnceAsync(),
            null,
            TimeSpan.FromSeconds(_config.DiagnosticsIntervalSeconds),
            TimeSpan.FromSeconds(_config.DiagnosticsIntervalSeconds)
        );
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Остановка диагностики
    /// </summary>
    public Task StopAsync()
    {
        _logger.LogInformation("Остановка диагностического движка");
        
        _shutdownCts?.Cancel();
        _diagnosticsTimer?.Dispose();
        _diagnosticsTimer = null;
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Выполнение однократной диагностики
    /// </summary>
    public async Task<DiagnosticResult> RunOnceAsync()
    {
        var stopwatch = System.Diagnostics.Stopwatch.StartNew();
        _logger.LogDebug("📊 Запуск диагностического цикла");
        
        try
        {
            // Ping к первичному целевому хосту
            var primaryPing = await PingHostAsync(_config.PrimaryTarget);
            
            // Ping к вторичному целевому хосту
            var secondaryPing = await PingHostAsync(_config.SecondaryTarget);
            
            // Проверка шлюза по умолчанию
            var gatewayReachable = await CheckGatewayAsync();
            
            // Проверка DNS
            var dnsWorking = await CheckDnsAsync();
            
            // Трассировка (только если обнаружены проблемы)
            List<HopInfo>? traceroute = null;
            if (primaryPing.TimedOut || primaryPing.RoundtripTime > TimeSpan.FromMilliseconds(200))
            {
                _logger.LogWarning("⚠️ Обнаружены проблемы с сетью, запускаем трассировку");
                traceroute = await RunTracerouteAsync(_config.PrimaryTarget);
            }
            
            var result = new DiagnosticResult
            {
                Timestamp = DateTime.UtcNow,
                PingPrimary = primaryPing.RoundtripTime,
                PingSecondary = secondaryPing.RoundtripTime,
                GatewayReachable = gatewayReachable,
                DnsWorking = dnsWorking,
                Traceroute = traceroute,
                ErrorMessage = primaryPing.Error
            };
            
            _lastResult = result;
            
            _logger.LogInformation(
                "📈 Диагностика: Primary={PrimaryMs}ms, Secondary={SecondaryMs}ms, Gateway={Gateway}, DNS={Dns}",
                result.PingPrimary.TotalMilliseconds,
                result.PingSecondary.TotalMilliseconds,
                result.GatewayReachable ? "OK" : "FAIL",
                result.DnsWorking ? "OK" : "FAIL"
            );
            
            DiagnosticsCompleted?.Invoke(this, result);
            
            return result;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения диагностики");
            
            var errorResult = new DiagnosticResult
            {
                Timestamp = DateTime.UtcNow,
                PingPrimary = TimeSpan.Zero,
                PingSecondary = TimeSpan.Zero,
                GatewayReachable = false,
                DnsWorking = false,
                ErrorMessage = ex.Message
            };
            
            _lastResult = errorResult;
            DiagnosticsCompleted?.Invoke(this, errorResult);
            
            return errorResult;
        }
        finally
        {
            stopwatch.Stop();
            _logger.LogDebug("Диагностический цикл завершен за {Elapsed}ms", stopwatch.ElapsedMilliseconds);
        }
    }

    /// <summary>
    /// Ping к хосту
    /// </summary>
    private async Task<PingResult> PingHostAsync(string host)
    {
        var ping = GetPingFromPool();
        if (ping == null)
        {
            // Если пул пуст, создаем временный Ping
            ping = new Ping();
        }
        
        try
        {
            var buffer = new byte[32]; // Small payload
            var options = new PingOptions(64, true); // TTL=64, Don't Fragment
            
            var reply = await ping.SendPingAsync(host, _config.PingTimeoutMs, buffer, options);
            
            return new PingResult
            {
                RoundtripTime = TimeSpan.FromMilliseconds(reply.RoundtripTime),
                TimedOut = reply.Status == IPStatus.TimedOut,
                Error = reply.Status != IPStatus.Success ? reply.Status.ToString() : null
            };
        }
        catch (PingException ex)
        {
            _logger.LogDebug(ex, "Ping ошибка для {Host}", host);
            return new PingResult
            {
                RoundtripTime = TimeSpan.Zero,
                TimedOut = true,
                Error = ex.Message
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Неожиданная ошибка Ping для {Host}", host);
            return new PingResult
            {
                RoundtripTime = TimeSpan.Zero,
                TimedOut = true,
                Error = ex.Message
            };
        }
    }

    /// <summary>
    /// Проверка доступности шлюза по умолчанию
    /// </summary>
    private async Task<bool> CheckGatewayAsync()
    {
        try
        {
            var gateway = GetDefaultGateway();
            if (gateway == null)
            {
                _logger.LogWarning("Шлюз по умолчанию не найден");
                return false;
            }
            
            var result = await PingHostAsync(gateway);
            return !result.TimedOut;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка проверки шлюза");
            return false;
        }
    }

    /// <summary>
    /// Проверка работы DNS
    /// </summary>
    private async Task<bool> CheckDnsAsync()
    {
        try
        {
            // Пробуем разрешить известное доменное имя
            var addresses = await System.Net.Dns.GetHostAddressesAsync("dns.google");
            return addresses.Length > 0;
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "DNS проверка не удалась");
            return false;
        }
    }

    /// <summary>
    /// Трассировка маршрута
    /// </summary>
    private async Task<List<HopInfo>> RunTracerouteAsync(string target)
    {
        var hops = new List<HopInfo>();
        var ping = GetPingFromPool() ?? new Ping();
        
        try
        {
            for (int ttl = 1; ttl <= _config.MaxTracerouteHops; ttl++)
            {
                try
                {
                    var options = new PingOptions(ttl, true);
                    var buffer = new byte[32];
                    
                    var reply = await ping.SendPingAsync(target, _config.PingTimeoutMs, buffer, options);
                    
                    hops.Add(new HopInfo
                    {
                        HopNumber = ttl,
                        Address = reply.Address?.ToString(),
                        Rtt = TimeSpan.FromMilliseconds(reply.RoundtripTime),
                        TimedOut = reply.Status == IPStatus.TimedOut
                    });
                    
                    // Если достигли цели, прекращаем
                    if (reply.Status == IPStatus.Success)
                        break;
                }
                catch (Exception ex)
                {
                    _logger.LogDebug(ex, "Ошибка трассировки на TTL={Ttl}", ttl);
                    hops.Add(new HopInfo
                    {
                        HopNumber = ttl,
                        Address = null,
                        Rtt = TimeSpan.Zero,
                        TimedOut = true
                    });
                }
            }
        }
        finally
        {
            ReturnPingToPool(ping);
        }
        
        return hops;
    }

    /// <summary>
    /// Получить Ping из пула
    /// </summary>
    private Ping? GetPingFromPool()
    {
        lock (_lock)
        {
            if (_pingPool.Count > 0)
            {
                var ping = _pingPool[0];
                _pingPool.RemoveAt(0);
                return ping;
            }
        }
        return null;
    }

    /// <summary>
    /// Вернуть Ping в пул
    /// </summary>
    private void ReturnPingToPool(Ping ping)
    {
        lock (_lock)
        {
            if (!_pingPool.Contains(ping))
            {
                _pingPool.Add(ping);
            }
        }
    }

    /// <summary>
    /// Получить шлюз по умолчанию
    /// </summary>
    private static string? GetDefaultGateway()
    {
        try
        {
            foreach (var adapter in NetworkInterface.GetAllNetworkInterfaces())
            {
                if (adapter.OperationalStatus == OperationalStatus.Up &&
                    adapter.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                {
                    var properties = adapter.GetIPProperties();
                    var gateway = properties.GatewayAddresses
                        .FirstOrDefault(g => g.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork);
                    
                    if (gateway != null)
                        return gateway.Address.ToString();
                }
            }
        }
        catch (Exception)
        {
            // Игнорируем ошибки
        }
        return null;
    }

    /// <summary>
    /// Получить последнюю диагностику
    /// </summary>
    public DiagnosticResult? GetLastDiagnostics()
    {
        return _lastResult;
    }

    /// <summary>
    /// Освобождение ресурсов
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        StopAsync().Wait();
        
        foreach (var ping in _pingPool)
        {
            ping.Dispose();
        }
        _pingPool.Clear();
        
        _shutdownCts?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Результат Ping операции
/// </summary>
internal record PingResult
{
    public TimeSpan RoundtripTime { get; init; }
    public bool TimedOut { get; init; }
    public string? Error { get; init; }
}

/// <summary>
/// Конфигурация диагностического движка
/// </summary>
public record DiagnosticsConfig
{
    public string PrimaryTarget { get; init; } = "8.8.8.8";
    public string SecondaryTarget { get; init; } = "1.1.1.1";
    public int PingTimeoutMs { get; init; } = 3000;
    public int DiagnosticsIntervalSeconds { get; init; } = 30;
    public int MaxTracerouteHops { get; init; } = 30;
    
    public static DiagnosticsConfig Default => new();
}
