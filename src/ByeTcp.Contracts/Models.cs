namespace ByeTcp.Contracts;

/// <summary>
/// События процесса (ETW Process Events)
/// </summary>
public record ProcessEvent
{
    public ProcessEventType Type { get; init; }
    public string ProcessName { get; init; } = string.Empty;
    public int ProcessId { get; init; }
    public string? FullPath { get; init; }
    public DateTime Timestamp { get; init; }
    public int ParentProcessId { get; init; }
}

public enum ProcessEventType
{
    Start,
    End
}

/// <summary>
/// Сетевые метрики
/// </summary>
public record NetworkMetrics
{
    public double RttMs { get; init; }
    public double JitterMs { get; init; }
    public double PacketLossPercent { get; init; }
    public long TcpRetransmissionsPerSec { get; init; }
    public double BandwidthMbps { get; init; }
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
    public NetworkQuality Quality { get; init; } = NetworkQuality.Unknown;
}

public enum NetworkQuality
{
    Unknown,
    Excellent,    // RTT < 20ms, Loss < 0.1%
    Good,         // RTT < 50ms, Loss < 1%
    Fair,         // RTT < 100ms, Loss < 3%
    Poor,         // RTT >= 100ms или Loss >= 3%
    Critical      // RTT >= 200ms или Loss >= 10%
}

/// <summary>
/// Информация о процессе
/// </summary>
public record ProcessInfo
{
    public string Name { get; init; } = string.Empty;
    public string Path { get; init; } = string.Empty;
    public int Pid { get; init; }
    public DateTime StartTime { get; init; }
    public ProcessState State { get; init; }
}

public enum ProcessState
{
    Running,
    Exited
}

/// <summary>
/// TCP Профиль
/// </summary>
public record TcpProfile
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public string? Version { get; init; }
    
    // Registry settings
    public int? TcpAckFrequency { get; init; }
    public int? TcpNoDelay { get; init; }
    public int? TcpDelAckTicks { get; init; }
    
    // NetSh settings
    public string? ReceiveWindowAutoTuningLevel { get; init; }
    public string? CongestionProvider { get; init; }
    public string? EcnCapability { get; init; }
    public string? ReceiveWindowScaling { get; init; }
    public string? Sack { get; init; }
    public string? Timestamps { get; init; }
    public int? MaxMss { get; init; }
}

/// <summary>
/// Правило переключения
/// </summary>
public record Rule
{
    public string Id { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string? Description { get; init; }
    public RuleConditions? Conditions { get; init; }
    public string ProfileId { get; init; } = string.Empty;
    public TimeSpan? MinDuration { get; init; }  // Минимальная длительность применения
    public TimeSpan? Cooldown { get; init; }     // Задержка перед повторным применением
}

public record RuleConditions
{
    public ProcessCondition? Process { get; init; }
    public NetworkCondition? Network { get; init; }
    public TimeCondition? Time { get; init; }
}

public record ProcessCondition
{
    public string Name { get; init; } = string.Empty;
    public ProcessState State { get; init; } = ProcessState.Running;
    public string? PathPattern { get; init; }  // Wildcard pattern для пути
}

public record NetworkCondition
{
    public double? MinRttMs { get; init; }
    public double? MaxRttMs { get; init; }
    public double? MinPacketLossPercent { get; init; }
    public double? MaxPacketLossPercent { get; init; }
    public NetworkQuality? MinQuality { get; init; }
    public NetworkQuality? MaxQuality { get; init; }
}

public record TimeCondition
{
    public TimeSpan? StartTime { get; init; }
    public TimeSpan? EndTime { get; init; }
    public List<DayOfWeek>? DaysOfWeek { get; init; }
}

/// <summary>
/// Результат оценки правил
/// </summary>
public record RuleEvaluationResult
{
    public string? SelectedProfileId { get; init; }
    public string? MatchingRuleId { get; init; }
    public int Priority { get; init; }
    public string Reason { get; init; } = string.Empty;
    public bool ShouldSwitch { get; init; }
    public EvaluationConfidence Confidence { get; init; } = EvaluationConfidence.Normal;
}

public enum EvaluationConfidence
{
    Low,
    Normal,
    High
}

/// <summary>
/// Контекст оценки правил (immutable)
/// </summary>
public record EvaluationContext
{
    public IReadOnlySet<ProcessInfo> RunningProcesses { get; init; }
        = new HashSet<ProcessInfo>();
    public NetworkMetrics NetworkMetrics { get; init; } = new();
    public DateTime EvaluationTime { get; init; } = DateTime.UtcNow;
    public string? CurrentProfileId { get; init; }
}

/// <summary>
/// Изменение настройки
/// </summary>
public record SettingChange
{
    public SettingType Type { get; init; }
    public string Key { get; init; } = string.Empty;
    public string? PreviousValue { get; init; }
    public string NewValue { get; init; } = string.Empty;
    public string? ErrorMessage { get; init; }
}

public enum SettingType
{
    Registry,
    NetSh,
    Wfp,
    PowerShell,
    Custom
}

/// <summary>
/// Результат применения настроек
/// </summary>
public record ExecutionResult
{
    public bool Success { get; init; }
    public string CorrelationId { get; init; } = string.Empty;
    public List<SettingChange> AppliedChanges { get; init; } = new();
    public List<SettingChange> RolledBackChanges { get; init; } = new();
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
    public TimeSpan Duration { get; init; }
    
    public static ExecutionResult CreateSuccess(string correlationId, List<SettingChange> changes) =>
        new() { Success = true, CorrelationId = correlationId, AppliedChanges = changes };
    
    public static ExecutionResult CreateFailure(string correlationId, Exception ex, List<SettingChange> rolledBack) =>
        new() { Success = false, CorrelationId = correlationId, Exception = ex, ErrorMessage = ex.Message, RolledBackChanges = rolledBack };
}

/// <summary>
/// Команда на изменение профиля
/// </summary>
public record ProfileChangeCommand
{
    public string ProfileId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public CancellationToken CancellationToken { get; init; }
    public bool Force { get; init; }  // Игнорировать rate limiting
}

/// <summary>
/// Результат изменения профиля
/// </summary>
public record ProfileChangeResult
{
    public string CorrelationId { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
    public string? FromProfileId { get; init; }
    public string ToProfileId { get; init; } = string.Empty;
}

/// <summary>
/// Запись истории изменений профиля
/// </summary>
public record ProfileChangeRecord
{
    public DateTime Timestamp { get; init; }
    public string? FromProfileId { get; init; }
    public string ToProfileId { get; init; } = string.Empty;
    public string Reason { get; init; } = string.Empty;
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}

/// <summary>
/// Состояние менеджера состояний
/// </summary>
public record StateManagerState
{
    public string? CurrentProfileId { get; init; }
    public string? PreviousProfileId { get; init; }
    public DateTime LastChangeTime { get; init; }
    public int ConsecutiveChanges { get; init; }
    public List<ProfileChangeRecord> History { get; init; } = new();
    public TcpProfile? DefaultProfile { get; init; }
}

/// <summary>
/// Отчет о здоровье системы
/// </summary>
public record HealthReport
{
    public bool IsHealthy { get; init; }
    public TimeSpan Uptime { get; init; }
    public int ActiveProfileChanges { get; init; }
    public int FailedOperations { get; init; }
    public DateTime? LastSuccessfulOperation { get; init; }
    public List<HealthIssue> Issues { get; init; } = new();
    public ComponentHealth[] ComponentHealth { get; init; } = Array.Empty<ComponentHealth>();
}

public record HealthIssue
{
    public HealthIssueType Type { get; init; }
    public Severity Severity { get; init; }
    public string Message { get; init; } = string.Empty;
    public DateTime FirstOccurrence { get; init; }
    public int OccurrenceCount { get; init; }
}

public enum HealthIssueType
{
    MonitorFailure,
    ExecutorFailure,
    ConfigError,
    PermissionError,
    NetworkUnreachable,
    ResourceExhaustion
}

public enum Severity
{
    Info,
    Warning,
    Error,
    Critical
}

public record ComponentHealth
{
    public string ComponentName { get; init; } = string.Empty;
    public bool IsHealthy { get; init; }
    public string? ErrorMessage { get; init; }
    public DateTime? LastCheckTime { get; init; }
}

/// <summary>
/// Статус здоровья для watchdog
/// </summary>
public enum HealthStatus
{
    Healthy,
    Degraded,
    Unhealthy,
    Unknown
}

/// <summary>
/// Адаптивный режим для мониторинга
/// </summary>
public enum AdaptiveMode
{
    Idle,       // 60 сек интервал
    Normal,     // 30 сек интервал
    Active,     // 10 сек интервал
    Critical    // 5 сек интервал
}
