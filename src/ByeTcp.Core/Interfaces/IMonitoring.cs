using ByeTcp.Core.Models;

namespace ByeTcp.Core.Interfaces;

/// <summary>
/// Интерфейс монитора процессов
/// </summary>
public interface IProcessMonitor : IDisposable
{
    /// <summary>
    /// Событие запуска процесса
    /// </summary>
    event EventHandler<ProcessInfo>? ProcessStarted;
    
    /// <summary>
    /// Событие завершения процесса
    /// </summary>
    event EventHandler<ProcessInfo>? ProcessExited;
    
    /// <summary>
    /// Запуск мониторинга
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Остановка мониторинга
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Получить список активных monitored процессов
    /// </summary>
    IReadOnlyList<ProcessInfo> GetRunningProcesses();
    
    /// <summary>
    /// Добавить процесс для мониторинга
    /// </summary>
    void AddProcessToMonitor(string processName);
    
    /// <summary>
    /// Удалить процесс из мониторинга
    /// </summary>
    void RemoveProcessFromMonitor(string processName);
}

/// <summary>
/// Интерфейс монитора сети
/// </summary>
public interface INetworkMonitor : IDisposable
{
    /// <summary>
    /// Событие обновления метрик
    /// </summary>
    event EventHandler<NetworkMetrics>? MetricsUpdated;
    
    /// <summary>
    /// Запуск мониторинга
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Остановка мониторинга
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Получить текущие метрики
    /// </summary>
    NetworkMetrics GetCurrentMetrics();
}

/// <summary>
/// Интерфейс движка правил
/// </summary>
public interface IRuleEngine
{
    /// <summary>
    /// Событие изменения активного профиля
    /// </summary>
    event EventHandler<RuleEvaluationResult>? ProfileChanged;
    
    /// <summary>
    /// Загрузить правила из конфигурации
    /// </summary>
    Task LoadRulesAsync(string configPath);
    
    /// <summary>
    /// Загрузить профили из конфигурации
    /// </summary>
    Task LoadProfilesAsync(string configPath);
    
    /// <summary>
    /// Оценить текущее состояние и выбрать профиль
    /// </summary>
    RuleEvaluationResult Evaluate(IReadOnlyList<ProcessInfo> processes, NetworkMetrics metrics);
    
    /// <summary>
    /// Принудительно применить профиль
    /// </summary>
    void ForceApplyProfile(string profileId, string reason);
    
    /// <summary>
    /// Получить текущий активный профиль
    /// </summary>
    TcpProfile? GetCurrentProfile();
    
    /// <summary>
    /// Получить все загруженные профили
    /// </summary>
    IReadOnlyList<TcpProfile> GetAllProfiles();
}

/// <summary>
/// Интерфейс модуля применения настроек
/// </summary>
public interface ISettingsApplier
{
    /// <summary>
    /// Применить профиль настроек
    /// </summary>
    Task<bool> ApplyProfileAsync(TcpProfile profile, CancellationToken cancellationToken);
    
    /// <summary>
    /// Применить отдельное действие
    /// </summary>
    Task<bool> ApplyActionAsync(ProfileAction action, CancellationToken cancellationToken);
    
    /// <summary>
    /// Экспортировать текущие настройки в файл
    /// </summary>
    Task<string> ExportCurrentSettingsAsync(string backupPath);
    
    /// <summary>
    /// Восстановить настройки из резервной копии
    /// </summary>
    Task<bool> RestoreFromBackupAsync(string backupPath);
    
    /// <summary>
    /// Сбросить к настройкам по умолчанию
    /// </summary>
    Task<bool> ResetToDefaultsAsync();
    
    /// <summary>
    /// Получить текущие значения параметров
    /// </summary>
    Task<Dictionary<string, string>> GetCurrentSettingsAsync();
}

/// <summary>
/// Интерфейс диагностического движка
/// </summary>
public interface IDiagnosticsEngine : IDisposable
{
    /// <summary>
    /// Событие завершения диагностического цикла
    /// </summary>
    event EventHandler<DiagnosticResult>? DiagnosticsCompleted;
    
    /// <summary>
    /// Запуск диагностики
    /// </summary>
    Task StartAsync(CancellationToken cancellationToken);
    
    /// <summary>
    /// Остановка диагностики
    /// </summary>
    Task StopAsync();
    
    /// <summary>
    /// Выполнить однократную диагностику
    /// </summary>
    Task<DiagnosticResult> RunOnceAsync();
    
    /// <summary>
    /// Получить последнюю диагностику
    /// </summary>
    DiagnosticResult? GetLastDiagnostics();
}

/// <summary>
/// Результат диагностики
/// </summary>
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

/// <summary>
/// Информация о ходе трассировки
/// </summary>
public record HopInfo
{
    public int HopNumber { get; init; }
    public string? Address { get; init; }
    public TimeSpan Rtt { get; init; }
    public bool TimedOut { get; init; }
}
