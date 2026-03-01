namespace ByeTcp.Contracts;

/// <summary>
/// Интерфейс монитора процессов (ETW-based)
/// </summary>
public interface IProcessMonitor
{
    event EventHandler<ProcessEvent>? ProcessEventReceived;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    IReadOnlySet<string> GetMonitoredProcesses();
    void AddProcessFilter(string processName);
    void RemoveProcessFilter(string processName);
    Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync();
}

/// <summary>
/// Интерфейс монитора сети (adaptive)
/// </summary>
public interface INetworkMonitor
{
    event EventHandler<NetworkMetrics>? MetricsUpdated;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    void SetAdaptiveMode(AdaptiveMode mode);
    NetworkMetrics GetCurrentMetrics();
    NetworkQuality GetCurrentQuality();
}

/// <summary>
/// Интерфейс диагностического движка
/// </summary>
public interface IDiagnosticsEngine
{
    event EventHandler<DiagnosticResult>? DiagnosticsCompleted;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    Task<DiagnosticResult> RunOnceAsync(CancellationToken ct);
    DiagnosticResult? GetLastDiagnostics();
}

public record DiagnosticResult
{
    public DateTime Timestamp { get; init; }
    public TimeSpan PingPrimary { get; init; }
    public TimeSpan PingSecondary { get; init; }
    public bool GatewayReachable { get; init; }
    public bool DnsWorking { get; init; }
    public List<HopInfo>? Traceroute { get; init; }
    public string? ErrorMessage { get; init; }
}

public record HopInfo
{
    public int HopNumber { get; init; }
    public string? Address { get; init; }
    public TimeSpan Rtt { get; init; }
    public bool TimedOut { get; init; }
}

/// <summary>
/// Интерфейс Rule Engine (pure functions)
/// </summary>
public interface IRuleEngine
{
    RuleEvaluationResult Evaluate(
        EvaluationContext context,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, TcpProfile> profiles
    );
    ValidationResult ValidateRules(IReadOnlyList<Rule> rules);
}

public record ValidationResult
{
    public bool IsValid { get; init; }
    public List<string> Errors { get; init; } = new();
}

/// <summary>
/// Интерфейс State Manager
/// </summary>
public interface IStateManager
{
    TcpProfile? GetCurrentProfile();
    TcpProfile? GetPreviousProfile();
    TcpProfile GetDefaultProfile();
    IReadOnlyList<TcpProfile> GetAllProfiles();
    
    bool CanSwitchProfile(string newProfileId, string? currentProfileId);
    void RecordProfileChange(ProfileChangeRecord record);
    Task RollbackToPreviousAsync();
    Task ResetToDefaultsAsync();
    
    IReadOnlyList<ProfileChangeRecord> GetHistory(int count);
    StateManagerState GetState();
    
    Task SaveStateAsync();
    Task LoadStateAsync();
}

/// <summary>
/// Интерфейс Settings Executor (transactional)
/// </summary>
public interface ISettingsExecutor
{
    Task<ExecutionResult> ApplyProfileAsync(
        TcpProfile profile,
        CancellationToken ct
    );
    Task<ExecutionResult> RollbackAsync(
        StateManagerState previousState,
        CancellationToken ct
    );
    Task<TcpProfile?> GetCurrentStateAsync();
    Task<Dictionary<string, string>> GetCachedStateAsync();
}

/// <summary>
/// Интерфейс Settings Provider
/// </summary>
public interface ISettingsProvider
{
    string Name { get; }
    SettingType SettingType { get; }
    bool IsAvailable { get; }
    Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes,
        CancellationToken ct
    );
}

public record ProviderResult
{
    public bool Success { get; init; }
    public List<SettingChange> AppliedChanges { get; init; } = new();
    public string? Error { get; init; }
    public Exception? Exception { get; init; }
    
    public static ProviderResult CreateSuccess(List<SettingChange> changes) =>
        new() { Success = true, AppliedChanges = changes };
    
    public static ProviderResult CreateFailure(string error, Exception? ex = null) =>
        new() { Success = false, Error = error, Exception = ex };
}

/// <summary>
/// Интерфейс Config Manager (versioned)
/// </summary>
public interface IConfigManager
{
    Task<ConfigResult<T>> LoadConfigAsync<T>(
        string path,
        object schema,
        CancellationToken ct
    ) where T : class;

    ValidationResult Validate<T>(T config, object schema);
    T MergeConfigs<T>(T defaultConfig, T userConfig);
}

public record ConfigResult<T> where T : class
{
    public bool Success { get; init; }
    public T? Config { get; init; }
    public List<string> Errors { get; init; } = new();
    
    public static ConfigResult<T> SuccessResult(T config) =>
        new() { Success = true, Config = config };
    
    public static ConfigResult<T> ErrorResult(params string[] errors) =>
        new() { Success = false, Errors = errors.ToList() };
}

/// <summary>
/// Интерфейс State Cache
/// </summary>
public interface IStateCache
{
    Task<TcpProfile?> GetCurrentProfileAsync();
    Task UpdateStateAsync(TcpProfile profile, CancellationToken ct);
    Task ClearAsync();
    bool IsProfileApplied(TcpProfile profile);
}

/// <summary>
/// Интерфейс Orchestrator
/// </summary>
public interface IOrchestrator
{
    Task InitializeAsync(CancellationToken ct);
    Task RunAsync(CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
    HealthReport GetHealthReport();
    Task RecoverFromFailureAsync(Exception ex);
}

/// <summary>
/// Интерфейс Watchdog
/// </summary>
public interface IWatchdog
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    event EventHandler<HealthStatus>? HealthStatusChanged;
    Task RecoverAsync(Exception ex);
}

/// <summary>
/// Интерфейс Rate Limiter
/// </summary>
public interface IRateLimiter
{
    bool AllowAction(string key, TimeSpan window, int maxActions);
    TimeSpan GetRemainingCooldown(string key);
    void Reset(string key);
}
