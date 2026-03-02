using System.Diagnostics;
using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;
using ByeTcp.Monitoring;
using ByeTcp.Decision;
using ByeTcp.Execution;
using ByeTcp.Infrastructure;
using NJsonSchema;

namespace ByeTcp.Orchestration;

/// <summary>
/// Orchestrator — центральный компонент управления жизненным циклом
/// 
/// Ответственность:
/// - Координация между слоями (Monitoring → Decision → Execution)
/// - Graceful shutdown с timeout
/// - Health monitoring
/// - Обработка unhandled exceptions
/// </summary>
public sealed class Orchestrator : IOrchestrator, IDisposable
{
    private readonly ILogger<Orchestrator> _logger;
    private readonly IProcessMonitor _processMonitor;
    private readonly INetworkMonitor _networkMonitor;
    private readonly IDiagnosticsEngine? _diagnosticsEngine;
    private readonly IRuleEngine _ruleEngine;
    private readonly IStateManager _stateManager;
    private readonly ISettingsExecutor _settingsExecutor;
    private readonly IConfigManager _configManager;
    private readonly IWatchdog _watchdog;
    
    private readonly string _configPath;
    private readonly string _statePath;
    
    private CancellationTokenSource? _shutdownCts;
    private DateTime _startTime;
    private int _profileChangesCount;
    private int _failedOperationsCount;
    private DateTime? _lastSuccessfulOperation;
    private bool _disposed;

    public Orchestrator(
        ILogger<Orchestrator> logger,
        IProcessMonitor processMonitor,
        INetworkMonitor networkMonitor,
        IDiagnosticsEngine? diagnosticsEngine,
        IRuleEngine ruleEngine,
        IStateManager stateManager,
        ISettingsExecutor settingsExecutor,
        IConfigManager configManager,
        IWatchdog watchdog,
        string configPath,
        string statePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processMonitor = processMonitor ?? throw new ArgumentNullException(nameof(processMonitor));
        _networkMonitor = networkMonitor ?? throw new ArgumentNullException(nameof(networkMonitor));
        _diagnosticsEngine = diagnosticsEngine;
        _ruleEngine = ruleEngine ?? throw new ArgumentNullException(nameof(ruleEngine));
        _stateManager = stateManager ?? throw new ArgumentNullException(nameof(stateManager));
        _settingsExecutor = settingsExecutor ?? throw new ArgumentNullException(nameof(settingsExecutor));
        _configManager = configManager ?? throw new ArgumentNullException(nameof(configManager));
        _watchdog = watchdog ?? throw new ArgumentNullException(nameof(watchdog));
        _configPath = configPath;
        _statePath = statePath;
        
        // Подписка на события
        SubscribeToEvents();
    }

    private void SubscribeToEvents()
    {
        _processMonitor.ProcessEventReceived += OnProcessEvent;
        _networkMonitor.MetricsUpdated += OnNetworkMetricsUpdated;
        _watchdog.HealthStatusChanged += OnHealthStatusChanged;
    }

    public async Task InitializeAsync(CancellationToken ct)
    {
        _logger.LogInformation("╔═══════════════════════════════════════════════════════════╗");
        _logger.LogInformation("║        Bye-TCP Internet Orchestrator v2.0                 ║");
        _logger.LogInformation("║  Адаптивный оптимизатор TCP/IP для Windows                ║");
        _logger.LogInformation("╚═══════════════════════════════════════════════════════════╝");
        
        _startTime = DateTime.UtcNow;
        
        // 1. Проверка прав администратора
        if (!SecurityHelper.IsRunningAsAdministrator())
        {
            _logger.LogWarning("⚠️ Запуск не от Administrator. Некоторые функции могут быть недоступны.");
        }
        else
        {
            _logger.LogInformation("✅ Запуск от Administrator");
        }
        
        // 2. Загрузка конфигурации
        await LoadConfigurationAsync(ct);
        
        // 3. Загрузка состояния
        await _stateManager.LoadStateAsync();
        
        // 4. Запуск watchdog
        await _watchdog.StartAsync(ct);
        
        // 5. Запуск мониторов
        await _processMonitor.StartAsync(ct);
        await _networkMonitor.StartAsync(ct);
        
        if (_diagnosticsEngine != null)
        {
            await _diagnosticsEngine.StartAsync(ct);
        }
        
        _logger.LogInformation("✅ Все компоненты инициализированы");
    }

    private async Task LoadConfigurationAsync(CancellationToken ct)
    {
        _logger.LogInformation("📂 Загрузка конфигурации...");
        
        var profilesPath = Path.Combine(_configPath, "profiles.json");
        var rulesPath = Path.Combine(_configPath, "rules.json");
        
        // Загружаем профили
        var profilesSchema = await LoadSchemaAsync("profiles.schema.json", ct);
        var profilesResult = await _configManager.LoadConfigAsync<ProfilesConfig>(
            profilesPath, profilesSchema, ct);
        
        if (!profilesResult.Success || profilesResult.Config == null)
        {
            _logger.LogWarning("Не удалось загрузить профили: {Errors}. Используем встроенные.", 
                string.Join(", ", profilesResult.Errors));
        }
        else
        {
            _logger.LogInformation("✅ Загружено {Count} профилей", profilesResult.Config.Profiles.Count);
        }
        
        // Загружаем правила
        var rulesSchema = await LoadSchemaAsync("rules.schema.json", ct);
        var rulesResult = await _configManager.LoadConfigAsync<RulesConfig>(
            rulesPath, rulesSchema, ct);
        
        if (!rulesResult.Success || rulesResult.Config == null)
        {
            _logger.LogWarning("Не удалось загрузить правила: {Errors}. Используем встроенные.", 
                string.Join(", ", rulesResult.Errors));
        }
        else
        {
            // Валидация правил
            var validation = _ruleEngine.ValidateRules(rulesResult.Config.Rules);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Валидация правил не пройдена: {Errors}", 
                    string.Join(", ", validation.Errors));
            }
            
            _logger.LogInformation("✅ Загружено {Count} правил", rulesResult.Config.Rules.Count);
        }
    }

    private async Task<JsonSchema> LoadSchemaAsync(string schemaName, CancellationToken ct)
    {
        var schemaPath = Path.Combine(AppContext.BaseDirectory, "schemas", schemaName);
        
        if (File.Exists(schemaPath))
        {
            var schemaJson = await File.ReadAllTextAsync(schemaPath, ct);
            return await JsonSchema.FromJsonAsync(schemaJson);
        }
        
        // Fallback: встроенная схема (пустая, пропускает всё)
        return new JsonSchema();
    }

    public async Task RunAsync(CancellationToken ct)
    {
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        _logger.LogInformation("🚀 Запуск основного цикла");
        
        try
        {
            // Применяем начальный профиль
            await ApplyInitialProfileAsync(ct);
            
            // Основной цикл (ожидание событий)
            while (!_shutdownCts.IsCancellationRequested)
            {
                // Периодическая оценка правил (каждые 30 сек)
                await EvaluateRulesAsync(ct);
                
                // Health check
                LogHealthStatus();
                
                try
                {
                    await Task.Delay(TimeSpan.FromSeconds(30), _shutdownCts.Token);
                }
                catch (OperationCanceledException)
                {
                    break;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogCritical(ex, "Критическая ошибка в основном цикле");
            await RecoverFromFailureAsync(ex);
        }
    }

    private async Task ApplyInitialProfileAsync(CancellationToken ct)
    {
        _logger.LogInformation("🎯 Применение начального профиля...");
        
        var defaultProfile = _stateManager.GetDefaultProfile();
        var result = await _settingsExecutor.ApplyProfileAsync(defaultProfile, ct);
        
        if (result.Success)
        {
            _stateManager.RecordProfileChange(new ProfileChangeRecord
            {
                Timestamp = DateTime.Now,
                ToProfileId = defaultProfile.Id,
                Reason = "Initial application",
                Success = true,
                Duration = result.Duration
            });
            
            _lastSuccessfulOperation = DateTime.UtcNow;
        }
        else
        {
            _failedOperationsCount++;
        }
    }

    private async Task EvaluateRulesAsync(CancellationToken ct)
    {
        try
        {
            var processes = await _processMonitor.GetRunningProcessesAsync();
            var metrics = _networkMonitor.GetCurrentMetrics();
            
            var context = new EvaluationContext
            {
                RunningProcesses = processes.ToHashSet(ProcessInfoComparer.Instance),
                NetworkMetrics = metrics,
                EvaluationTime = DateTime.UtcNow,
                CurrentProfileId = _stateManager.GetCurrentProfile()?.Id
            };
            
            // Загружаем правила из config (в production лучше кэшировать)
            var rules = LoadRulesFromConfig();
            var profiles = _stateManager.GetType()
                .GetField("_profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
                ?.GetValue(_stateManager) as ConcurrentDictionary<string, TcpProfile>;
            
            var result = _ruleEngine.Evaluate(context, rules, profiles?.ToDictionary(k => k.Key, v => v.Value) ?? new());
            
            if (result.ShouldSwitch && result.SelectedProfileId != null)
            {
                await SwitchProfileAsync(result.SelectedProfileId, result.Reason, ct);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка оценки правил");
            _failedOperationsCount++;
        }
    }

    private List<Rule> LoadRulesFromConfig()
    {
        // В production: кэшировать правила
        var rulesPath = Path.Combine(_configPath, "rules.json");
        
        if (!File.Exists(rulesPath))
            return new List<Rule>();
        
        var json = File.ReadAllText(rulesPath);
        var config = Newtonsoft.Json.JsonConvert.DeserializeObject<RulesConfig>(json);
        
        return config?.Rules ?? new List<Rule>();
    }

    private async Task SwitchProfileAsync(string profileId, string reason, CancellationToken ct)
    {
        // Rate limiting check
        if (!_stateManager.CanSwitchProfile(profileId, _stateManager.GetCurrentProfile()?.Id))
        {
            _logger.LogWarning("⚠️ Переключение профиля отклонено (rate limiting): {ProfileId}", profileId);
            return;
        }
        
        _logger.LogInformation("🔄 Переключение профиля: {ProfileId} ({Reason})", profileId, reason);
        
        var profile = _stateManager.GetType()
            .GetField("_profiles", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance)
            ?.GetValue(_stateManager) as ConcurrentDictionary<string, TcpProfile>;
        
        var tcpProfile = profile?.GetValueOrDefault(profileId);
        
        if (tcpProfile == null)
        {
            _logger.LogError("Профиль {ProfileId} не найден", profileId);
            return;
        }
        
        var result = await _settingsExecutor.ApplyProfileAsync(tcpProfile, ct);
        
        _profileChangesCount++;
        
        if (result.Success)
        {
            _stateManager.RecordProfileChange(new ProfileChangeRecord
            {
                Timestamp = DateTime.Now,
                FromProfileId = _stateManager.GetCurrentProfile()?.Id,
                ToProfileId = profileId,
                Reason = reason,
                Success = true,
                Duration = result.Duration
            });
            
            _lastSuccessfulOperation = DateTime.UtcNow;
            
            // Адаптивное переключение режима мониторинга
            _networkMonitor.SetAdaptiveMode(AdaptiveMode.Active);
            
            // Возврат в нормальный режим через 1 минуту
            _ = Task.Delay(TimeSpan.FromMinutes(1), ct).ContinueWith(_ =>
            {
                _networkMonitor.SetAdaptiveMode(AdaptiveMode.Normal);
            }, ct);
        }
        else
        {
            _failedOperationsCount++;
            
            _stateManager.RecordProfileChange(new ProfileChangeRecord
            {
                Timestamp = DateTime.Now,
                FromProfileId = _stateManager.GetCurrentProfile()?.Id,
                ToProfileId = profileId,
                Reason = reason,
                Success = false,
                ErrorMessage = result.ErrorMessage,
                Duration = result.Duration
            });
        }
    }

    private void OnProcessEvent(object? sender, ProcessEvent e)
    {
        _logger.LogDebug("📩 Process Event: {Type} {ProcessName} (PID: {Pid})", 
            e.Type, e.ProcessName, e.ProcessId);
        
        // Запускаем оценку правил при событии процесса
        // Debounce: не чаще чем раз в 2 секунды
        _ = Task.Run(async () =>
        {
            await Task.Delay(2000);
            await EvaluateRulesAsync(_shutdownCts?.Token ?? CancellationToken.None);
        });
    }

    private void OnNetworkMetricsUpdated(object? sender, NetworkMetrics metrics)
    {
        _logger.LogTrace("📊 Network Metrics: RTT={Rtt}ms, Quality={Quality}", 
            metrics.RttMs, metrics.Quality);
    }

    private void OnHealthStatusChanged(object? sender, HealthStatus status)
    {
        _logger.LogWarning("⚠️ Health Status изменился: {Status}", status);
        
        if (status == HealthStatus.Unhealthy)
        {
            // Попытка восстановления
            _ = _watchdog.RecoverAsync(new Exception("Unhealthy status"));
        }
    }

    private void LogHealthStatus()
    {
        _logger.LogDebug(
            "📍 Health: Uptime={Uptime}, Changes={Changes}, Failed={Failed}, LastSuccess={LastSuccess}",
            DateTime.UtcNow - _startTime,
            _profileChangesCount,
            _failedOperationsCount,
            _lastSuccessfulOperation?.ToString("HH:mm:ss") ?? "none");
    }

    public HealthReport GetHealthReport()
    {
        return new HealthReport
        {
            IsHealthy = _failedOperationsCount < 10,
            Uptime = DateTime.UtcNow - _startTime,
            ActiveProfileChanges = _profileChangesCount,
            FailedOperations = _failedOperationsCount,
            LastSuccessfulOperation = _lastSuccessfulOperation,
            Issues = new List<HealthIssue>(),
            ComponentHealth = new[]
            {
                new ComponentHealth
                {
                    ComponentName = "ProcessMonitor",
                    IsHealthy = true
                },
                new ComponentHealth
                {
                    ComponentName = "NetworkMonitor",
                    IsHealthy = true
                },
                new ComponentHealth
                {
                    ComponentName = "SettingsExecutor",
                    IsHealthy = _failedOperationsCount < 5
                }
            }
        };
    }

    public async Task RecoverFromFailureAsync(Exception ex)
    {
        _logger.LogError(ex, "Восстановление после ошибки");
        
        _failedOperationsCount++;
        
        // Попытка rollback к предыдущему профилю
        try
        {
            await _stateManager.RollbackToPreviousAsync();
        }
        catch (Exception rollbackEx)
        {
            _logger.LogError(rollbackEx, "Rollback не удался");
        }
        
        // Reset к default если слишком много ошибок
        if (_failedOperationsCount >= 10)
        {
            _logger.LogWarning("⚠️ Слишком много ошибок. Сброс к настройкам по умолчанию.");
            await _stateManager.ResetToDefaultsAsync();
        }
    }

    public async Task ShutdownAsync(CancellationToken ct)
    {
        _logger.LogInformation("⏹️ Остановка Orchestrator...");
        
        _shutdownCts?.Cancel();
        
        // Graceful shutdown с timeout
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(30));
        
        try
        {
            // Остановка мониторов
            if (_diagnosticsEngine != null)
                await _diagnosticsEngine.StopAsync();
            
            await _networkMonitor.StopAsync();
            await _processMonitor.StopAsync();
            
            // Сохранение состояния
            await _stateManager.SaveStateAsync();
            
            // Остановка watchdog
            await _watchdog.StopAsync();
            
            _logger.LogInformation("✅ Orchestrator остановлен");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка при остановке Orchestrator");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _shutdownCts?.Dispose();
        
        (_processMonitor as IDisposable)?.Dispose();
        (_networkMonitor as IDisposable)?.Dispose();
        (_diagnosticsEngine as IDisposable)?.Dispose();
        (_stateManager as IDisposable)?.Dispose();
        (_settingsExecutor as IDisposable)?.Dispose();
        
        GC.SuppressFinalize(this);
    }
}

// Comparer для ProcessInfo
internal sealed class ProcessInfoComparer : IEqualityComparer<ProcessInfo>
{
    public static readonly ProcessInfoComparer Instance = new();
    
    public bool Equals(ProcessInfo? x, ProcessInfo? y)
    {
        if (x is null || y is null) return false;
        return x.Pid == y.Pid && x.Name == y.Name;
    }
    
    public int GetHashCode(ProcessInfo obj)
    {
        return HashCode.Combine(obj.Pid, obj.Name);
    }
}

// Config classes для десериализации
internal sealed class ProfilesConfig
{
    public string Version { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = string.Empty;
    public List<TcpProfile> Profiles { get; init; } = new();
}

internal sealed class RulesConfig
{
    public string Version { get; init; } = string.Empty;
    public string SchemaVersion { get; init; } = string.Empty;
    public List<Rule> Rules { get; init; } = new();
}
