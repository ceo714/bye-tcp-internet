using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Orchestration;

/// <summary>
/// Watchdog для health monitoring и self-recovery
/// 
/// Ответственность:
/// - Периодическая проверка здоровья компонентов
/// - Автоматическое восстановление при сбоях
/// - Уведомления об изменении статуса
/// </summary>
public sealed class Watchdog : IWatchdog, IDisposable
{
    private readonly ILogger<Watchdog> _logger;
    private readonly IHealthCheck[] _healthChecks;
    private readonly TimeSpan _checkInterval;
    private readonly int _maxConsecutiveFailures;
    
    private Timer? _checkTimer;
    private CancellationTokenSource? _shutdownCts;
    private bool _disposed;
    private HealthStatus _currentStatus = HealthStatus.Unknown;
    private int _consecutiveFailures;

    public event EventHandler<HealthStatus>? HealthStatusChanged;

    public Watchdog(
        ILogger<Watchdog> logger,
        IEnumerable<IHealthCheck> healthChecks,
        TimeSpan? checkInterval = null,
        int maxConsecutiveFailures = 3)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _healthChecks = healthChecks.ToArray();
        _checkInterval = checkInterval ?? TimeSpan.FromSeconds(30);
        _maxConsecutiveFailures = maxConsecutiveFailures;
        
        _logger.LogInformation(
            "🐕 Watchdog инициализирован. Интервал: {Interval}, Max Failures: {Max}",
            _checkInterval, _maxConsecutiveFailures);
    }

    public Task StartAsync(CancellationToken ct)
    {
        _logger.LogInformation("Запуск Watchdog");
        
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Первый check немедленно
        _ = PerformHealthCheckAsync();
        
        // Периодические проверки
        _checkTimer = new Timer(
            async _ => await PerformHealthCheckAsync(),
            null,
            _checkInterval,
            _checkInterval
        );
        
        return Task.CompletedTask;
    }

    private async Task PerformHealthCheckAsync()
    {
        try
        {
            var issues = new List<HealthIssue>();
            var componentHealth = new List<ComponentHealth>();
            
            foreach (var check in _healthChecks)
            {
                try
                {
                    var result = await check.CheckHealthAsync(_shutdownCts?.Token ?? CancellationToken.None);
                    
                    componentHealth.Add(new ComponentHealth
                    {
                        ComponentName = check.ComponentName,
                        IsHealthy = result.IsHealthy,
                        ErrorMessage = result.ErrorMessage,
                        LastCheckTime = DateTime.UtcNow
                    });
                    
                    if (!result.IsHealthy)
                    {
                        issues.Add(new HealthIssue
                        {
                            Type = HealthIssueType.MonitorFailure,
                            Severity = Severity.Warning,
                            Message = $"{check.ComponentName}: {result.ErrorMessage}",
                            FirstOccurrence = DateTime.UtcNow,
                            OccurrenceCount = 1
                        });
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Health check failed for {Component}", check.ComponentName);
                    
                    componentHealth.Add(new ComponentHealth
                    {
                        ComponentName = check.ComponentName,
                        IsHealthy = false,
                        ErrorMessage = ex.Message,
                        LastCheckTime = DateTime.UtcNow
                    });
                }
            }
            
            // Определение общего статуса
            var unhealthyCount = componentHealth.Count(c => !c.IsHealthy);
            var newStatus = unhealthyCount switch
            {
                0 => HealthStatus.Healthy,
                <= 1 => HealthStatus.Degraded,
                _ => HealthStatus.Unhealthy
            };
            
            // Update статуса
            UpdateHealthStatus(newStatus);
            
            // Tracking consecutive failures
            if (newStatus == HealthStatus.Unhealthy)
            {
                _consecutiveFailures++;
                
                if (_consecutiveFailures >= _maxConsecutiveFailures)
                {
                    _logger.LogWarning(
                        "⚠️ {Max} последовательных неудач. Запуск восстановления.",
                        _maxConsecutiveFailures);
                    
                    await AttemptRecoveryAsync(issues);
                }
            }
            else
            {
                _consecutiveFailures = 0;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения health check");
        }
    }

    private void UpdateHealthStatus(HealthStatus newStatus)
    {
        if (_currentStatus != newStatus)
        {
            _logger.LogInformation(
                "📊 Health Status: {OldStatus} → {NewStatus}",
                _currentStatus, newStatus);
            
            _currentStatus = newStatus;
            HealthStatusChanged?.Invoke(this, newStatus);
        }
    }

    private async Task AttemptRecoveryAsync(List<HealthIssue> issues)
    {
        foreach (var issue in issues)
        {
            try
            {
                _logger.LogInformation("🔧 Попытка восстановления: {Issue}", issue.Message);
                
                // Находим соответствующий health check и пытаемся восстановить
                var check = _healthChecks.FirstOrDefault(h => 
                    h.ComponentName.Contains(issue.Type.ToString(), StringComparison.OrdinalIgnoreCase));
                
                if (check != null)
                {
                    await check.RecoverAsync();
                    _logger.LogInformation("✅ Восстановление {Component} успешно", check.ComponentName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Восстановление не удалось: {Issue}", issue.Message);
            }
        }
    }

    public async Task RecoverAsync(Exception ex)
    {
        _logger.LogWarning(ex, "🚨 Recover вызван из-за исключения");
        
        foreach (var check in _healthChecks)
        {
            try
            {
                await check.RecoverAsync();
            }
            catch (Exception recoverEx)
            {
                _logger.LogError(recoverEx, "Recover failed for {Component}", check.ComponentName);
            }
        }
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Остановка Watchdog");
        
        _shutdownCts?.Cancel();
        _checkTimer?.Dispose();
        _checkTimer = null;
        
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        StopAsync().Wait();
        _shutdownCts?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Интерфейс health check для компонента
/// </summary>
public interface IHealthCheck
{
    string ComponentName { get; }
    Task<HealthCheckResult> CheckHealthAsync(CancellationToken ct);
    Task RecoverAsync();
}

/// <summary>
/// Результат health check
/// </summary>
public record HealthCheckResult
{
    public bool IsHealthy { get; init; }
    public string? ErrorMessage { get; init; }
    public Dictionary<string, object>? Details { get; init; }
    
    public static HealthCheckResult Healthy(Dictionary<string, object>? details = null) =>
        new() { IsHealthy = true, Details = details };
    
    public static HealthCheckResult Unhealthy(string error, Dictionary<string, object>? details = null) =>
        new() { IsHealthy = false, ErrorMessage = error, Details = details };
}
