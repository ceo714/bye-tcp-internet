namespace ByeTcp.Client.Models;

/// <summary>
/// Базовая модель для IPC сообщений
/// </summary>
public record IpcMessage
{
    public string MessageType { get; init; } = string.Empty;
    public string CorrelationId { get; init; } = Guid.NewGuid().ToString("N")[..8];
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Запрос статуса службы
/// </summary>
public record ServiceStatusRequest : IpcMessage
{
    public ServiceStatusRequest() => MessageType = "ServiceStatusRequest";
}

/// <summary>
/// Ответ со статусом службы
/// </summary>
public record ServiceStatusResponse : IpcMessage
{
    public bool IsRunning { get; init; }
    public string? CurrentProfileId { get; init; }
    public string? CurrentProfileName { get; init; }
    public TimeSpan Uptime { get; init; }
    public int ProfileChangesCount { get; init; }
    public bool IsHealthy { get; init; }
    public string? ErrorMessage { get; init; }
    
    public ServiceStatusResponse() => MessageType = "ServiceStatusResponse";
}

/// <summary>
/// Запрос сетевых метрик
/// </summary>
public record NetworkMetricsRequest : IpcMessage
{
    public NetworkMetricsRequest() => MessageType = "NetworkMetricsRequest";
}

/// <summary>
/// Ответ с сетевыми метриками
/// </summary>
public record NetworkMetricsResponse : IpcMessage
{
    public double RttMs { get; init; }
    public double JitterMs { get; init; }
    public double PacketLossPercent { get; init; }
    public NetworkQuality Quality { get; init; }
    public List<MetricDataPoint> History { get; init; } = new();
    
    public NetworkMetricsResponse() => MessageType = "NetworkMetricsResponse";
}

/// <summary>
/// Точка данных для графика
/// </summary>
public record MetricDataPoint
{
    public DateTime Timestamp { get; init; }
    public double Value { get; init; }
    public string MetricType { get; init; } = string.Empty;
}

/// <summary>
/// Качество сети
/// </summary>
public enum NetworkQuality
{
    Unknown = 0,
    Excellent = 1,
    Good = 2,
    Fair = 3,
    Poor = 4,
    Critical = 5
}

/// <summary>
/// Запрос списка профилей
/// </summary>
public record GetProfilesRequest : IpcMessage
{
    public GetProfilesRequest() => MessageType = "GetProfilesRequest";
}

/// <summary>
/// Ответ со списком профилей
/// </summary>
public record GetProfilesResponse : IpcMessage
{
    public List<TcpProfileDto> Profiles { get; init; } = new();
    public string ActiveProfileId { get; init; } = string.Empty;
    
    public GetProfilesResponse() => MessageType = "GetProfilesResponse";
}

/// <summary>
/// DTO профиля TCP
/// </summary>
public record TcpProfileDto
{
    public string Id { get; init; } = string.Empty;
    public string Name { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int? TcpAckFrequency { get; init; }
    public int? TcpNoDelay { get; init; }
    public int? TcpDelAckTicks { get; init; }
    public string? ReceiveWindowAutoTuningLevel { get; init; }
    public string? CongestionProvider { get; init; }
    public string? EcnCapability { get; init; }
    public bool IsReadOnly { get; init; }
}

/// <summary>
/// Запрос применения профиля
/// </summary>
public record ApplyProfileRequest : IpcMessage
{
    public string ProfileId { get; init; } = string.Empty;
    public bool DryRun { get; init; }
    public int TimeoutSeconds { get; init; } = 30;
    
    public ApplyProfileRequest() => MessageType = "ApplyProfileRequest";
}

/// <summary>
/// Ответ применения профиля
/// </summary>
public record ApplyProfileResponse : IpcMessage
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public List<SettingChangeDto> AppliedChanges { get; init; } = new();
    public TimeSpan Duration { get; init; }
    
    public ApplyProfileResponse() => MessageType = "ApplyProfileResponse";
}

/// <summary>
/// DTO изменения настройки
/// </summary>
public record SettingChangeDto
{
    public string Type { get; init; } = string.Empty;
    public string Key { get; init; } = string.Empty;
    public string? PreviousValue { get; init; }
    public string NewValue { get; init; } = string.Empty;
}

/// <summary>
/// Запрос списка правил
/// </summary>
public record GetRulesRequest : IpcMessage
{
    public GetRulesRequest() => MessageType = "GetRulesRequest";
}

/// <summary>
/// Ответ со списком правил
/// </summary>
public record GetRulesResponse : IpcMessage
{
    public List<RuleDto> Rules { get; init; } = new();
    
    public GetRulesResponse() => MessageType = "GetRulesResponse";
}

/// <summary>
/// DTO правила
/// </summary>
public record RuleDto
{
    public string Id { get; init; } = string.Empty;
    public string Description { get; init; } = string.Empty;
    public int Priority { get; init; }
    public string ProfileId { get; init; } = string.Empty;
    public string? ProcessName { get; init; }
    public NetworkConditionDto? NetworkCondition { get; init; }
}

/// <summary>
/// DTO условия сети
/// </summary>
public record NetworkConditionDto
{
    public double? MinRttMs { get; init; }
    public double? MaxRttMs { get; init; }
    public double? MaxPacketLossPercent { get; init; }
}

/// <summary>
/// Запрос логов
/// </summary>
public record GetLogsRequest : IpcMessage
{
    public int Count { get; init; } = 100;
    public string? LevelFilter { get; init; }
    public string? ComponentFilter { get; init; }
    public DateTime? FromTime { get; init; }
    
    public GetLogsRequest() => MessageType = "GetLogsRequest";
}

/// <summary>
/// Ответ с логами
/// </summary>
public record GetLogsResponse : IpcMessage
{
    public List<LogEntryDto> Logs { get; init; } = new();
    
    public GetLogsResponse() => MessageType = "GetLogsResponse";
}

/// <summary>
/// DTO записи лога
/// </summary>
public record LogEntryDto
{
    public DateTime Timestamp { get; init; }
    public string Level { get; init; } = string.Empty;
    public string Component { get; init; } = string.Empty;
    public string Message { get; init; } = string.Empty;
    public string? Exception { get; init; }
}

/// <summary>
/// Запрос диагностики (Ping)
/// </summary>
public record PingRequest : IpcMessage
{
    public string Target { get; init; } = "8.8.8.8";
    public int Count { get; init; } = 4;
    public int TimeoutMs { get; init; } = 3000;
    
    public PingRequest() => MessageType = "PingRequest";
}

/// <summary>
/// Ответ диагностики (Ping)
/// </summary>
public record PingResponse : IpcMessage
{
    public bool Success { get; init; }
    public int MinMs { get; init; }
    public int MaxMs { get; init; }
    public int AvgMs { get; init; }
    public int PacketLoss { get; init; }
    public List<PingResultDto> Results { get; init; } = new();
    
    public PingResponse() => MessageType = "PingResponse";
}

/// <summary>
/// Результат отдельного Ping
/// </summary>
public record PingResultDto
{
    public int SequenceNumber { get; init; }
    public int ResponseTimeMs { get; init; }
    public bool TimedOut { get; init; }
    public string? ErrorMessage { get; init; }
}

/// <summary>
/// Запрос резервной копии
/// </summary>
public record CreateBackupRequest : IpcMessage
{
    public CreateBackupRequest() => MessageType = "CreateBackupRequest";
}

/// <summary>
/// Ответ резервной копии
/// </summary>
public record CreateBackupResponse : IpcMessage
{
    public bool Success { get; init; }
    public string? BackupPath { get; init; }
    public string? ErrorMessage { get; init; }
    
    public CreateBackupResponse() => MessageType = "CreateBackupResponse";
}

/// <summary>
/// Запрос отката
/// </summary>
public record RollbackRequest : IpcMessage
{
    public RollbackRequest() => MessageType = "RollbackRequest";
}

/// <summary>
/// Ответ отката
/// </summary>
public record RollbackResponse : IpcMessage
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    
    public RollbackResponse() => MessageType = "RollbackResponse";
}

/// <summary>
/// Запрос сброса к defaults
/// </summary>
public record ResetToDefaultsRequest : IpcMessage
{
    public ResetToDefaultsRequest() => MessageType = "ResetToDefaultsRequest";
}

/// <summary>
/// Ответ сброса
/// </summary>
public record ResetToDefaultsResponse : IpcMessage
{
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    
    public ResetToDefaultsResponse() => MessageType = "ResetToDefaultsResponse";
}
