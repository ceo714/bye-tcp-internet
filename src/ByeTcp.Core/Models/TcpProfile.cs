namespace ByeTcp.Core.Models;

/// <summary>
/// Представляет профиль оптимизации TCP/IP стека
/// </summary>
public record TcpProfile
{
    /// <summary>
    /// Уникальный идентификатор профиля
    /// </summary>
    public string Id { get; init; } = string.Empty;
    
    /// <summary>
    /// Отображаемое имя профиля
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Описание профиля
    /// </summary>
    public string Description { get; init; } = string.Empty;
    
    /// <summary>
    /// Частота подтверждения TCP (TcpAckFrequency)
    /// 1 = ACK на каждый пакет, 2 = каждый второй пакет
    /// </summary>
    public int? TcpAckFrequency { get; init; }
    
    /// <summary>
    /// Отключение алгоритма Нагла (TCPNoDelay)
    /// 1 = отключен (низкая задержка), 0 = включен (эффективность)
    /// </summary>
    public int? TcpNoDelay { get; init; }
    
    /// <summary>
    /// Задержка отложенных ACK в тиках (TcpDelAckTicks)
    /// 0 = отключено, 2 = стандартная задержка
    /// </summary>
    public int? TcpDelAckTicks { get; init; }
    
    /// <summary>
    /// Уровень автонастройки окна получения
    /// disabled, highlyrestricted, restricted, normal, experimental
    /// </summary>
    public string? ReceiveWindowAutoTuningLevel { get; init; }
    
    /// <summary>
    /// Алгоритм управления перегрузкой
    /// default, ctcp, cubic, ledbat
    /// </summary>
    public string? CongestionProvider { get; init; }
    
    /// <summary>
    /// Поддержка ECN (Explicit Congestion Notification)
    /// disabled, enabled
    /// </summary>
    public string? EcnCapability { get; init; }
    
    /// <summary>
    /// Максимальный размер сегмента (MSS)
    /// </summary>
    public int? MaxMss { get; init; }
    
    /// <summary>
    /// Масштабирование окна получения
    /// disabled, enabled
    /// </summary>
    public string? ReceiveWindowScaling { get; init; }
    
    /// <summary>
    /// Селективные подтверждения (SACK)
    /// disabled, enabled
    /// </summary>
    public string? Sack { get; init; }
    
    /// <summary>
    /// Временные метки TCP
    /// disabled, enabled
    /// </summary>
    public string? Timestamps { get; init; }
    
    /// <summary>
    /// Дополнительная защита от атак (RFC 5961)
    /// disabled, enabled
    /// </summary>
    public string? Rsc { get; init; }
    
    /// <summary>
    /// Offload параметров на сетевую карту
    /// disabled, enabled
    /// </summary>
    public string? Offload { get; init; }
}

/// <summary>
/// Сетевые метрики в реальном времени
/// </summary>
public record NetworkMetrics
{
    /// <summary>
    /// Время круговой задержки (мс)
    /// </summary>
    public double RttMs { get; init; }
    
    /// <summary>
    /// Джиттер (стандартное отклонение RTT, мс)
    /// </summary>
    public double JitterMs { get; init; }
    
    /// <summary>
    /// Процент потерянных пакетов
    /// </summary>
    public double PacketLossPercent { get; init; }
    
    /// <summary>
    /// Количество ретрансмиссий TCP
    /// </summary>
    public long TcpRetransmissions { get; init; }
    
    /// <summary>
    /// Использование пропускной способности (Mbps)
    /// </summary>
    public double BandwidthMbps { get; init; }
    
    /// <summary>
    /// Время измерения
    /// </summary>
    public DateTime Timestamp { get; init; } = DateTime.UtcNow;
}

/// <summary>
/// Информация о процессе
/// </summary>
public record ProcessInfo
{
    /// <summary>
    /// Имя исполняемого файла
    /// </summary>
    public string Name { get; init; } = string.Empty;
    
    /// <summary>
    /// Полный путь к исполняемому файлу
    /// </summary>
    public string Path { get; init; } = string.Empty;
    
    /// <summary>
    /// Идентификатор процесса
    /// </summary>
    public int Pid { get; init; }
    
    /// <summary>
    /// Время запуска процесса
    /// </summary>
    public DateTime StartTime { get; init; }
    
    /// <summary>
    /// Статус процесса (running/exited)
    /// </summary>
    public ProcessState State { get; init; }
}

public enum ProcessState
{
    Running,
    Exited
}

/// <summary>
/// Результат оценки правил
/// </summary>
public record RuleEvaluationResult
{
    /// <summary>
    /// ID примененного правила
    /// </summary>
    public string? RuleId { get; init; }
    
    /// <summary>
    /// ID примененного профиля
    /// </summary>
    public string? ProfileId { get; init; }
    
    /// <summary>
    /// Список действий для применения
    /// </summary>
    public List<ProfileAction> Actions { get; init; } = new();
    
    /// <summary>
    /// Приоритет примененного правила
    /// </summary>
    public int Priority { get; init; }
    
    /// <summary>
    /// Причина переключения профиля
    /// </summary>
    public string Reason { get; init; } = string.Empty;
}

/// <summary>
/// Действие профиля
/// </summary>
public record ProfileAction
{
    /// <summary>
    /// Тип действия (registry, netsh, wfp)
    /// </summary>
    public ActionType Type { get; init; }
    
    /// <summary>
    /// Путь к параметру или команда
    /// </summary>
    public string Target { get; init; } = string.Empty;
    
    /// <summary>
    /// Значение для установки
    /// </summary>
    public string? Value { get; init; }
}

public enum ActionType
{
    Registry,
    NetSh,
    Wfp,
    Custom
}
