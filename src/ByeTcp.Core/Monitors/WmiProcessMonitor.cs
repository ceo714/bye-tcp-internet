using System.Management;
using ByeTcp.Core.Interfaces;
using ByeTcp.Core.Models;
using Microsoft.Extensions.Logging;

namespace ByeTcp.Core.Monitors;

/// <summary>
/// Монитор процессов на основе WMI событий
/// Использует event-driven модель вместо polling для минимизации нагрузки
/// </summary>
public sealed class WmiProcessMonitor : IProcessMonitor
{
    private readonly ILogger<WmiProcessMonitor> _logger;
    private readonly HashSet<string> _monitoredProcesses = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<ManagementEventWatcher> _watchers = new();
    private readonly object _lock = new();
    private readonly Dictionary<string, ProcessInfo> _runningProcesses = new(StringComparer.OrdinalIgnoreCase);
    
    private bool _disposed;
    private CancellationTokenSource? _shutdownCts;

    public event EventHandler<ProcessInfo>? ProcessStarted;
    public event EventHandler<ProcessInfo>? ProcessExited;

    public WmiProcessMonitor(ILogger<WmiProcessMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Добавляем целевые процессы по умолчанию
        AddProcessToMonitor("cs2.exe");
        AddProcessToMonitor("qbittorrent.exe");
    }

    /// <summary>
    /// Запуск мониторинга процессов через WMI event subscription
    /// </summary>
    public Task StartAsync(CancellationToken cancellationToken)
    {
        if (_disposed) throw new ObjectDisposedException(nameof(WmiProcessMonitor));
        
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        
        _logger.LogInformation("Запуск WMI Process Monitor для {Count} процессов", 
            _monitoredProcesses.Count);
        
        // Создаем WMI watchers для каждого процесса
        foreach (var processName in _monitoredProcesses)
        {
            CreateEventWatchers(processName);
        }
        
        // Также проверяем уже запущенные процессы
        CheckAlreadyRunningProcesses();
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Остановка мониторинга
    /// </summary>
    public Task StopAsync()
    {
        _logger.LogInformation("Остановка WMI Process Monitor");
        
        _shutdownCts?.Cancel();
        
        lock (_lock)
        {
            foreach (var watcher in _watchers)
            {
                try
                {
                    watcher.Stop();
                    watcher.Dispose();
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Ошибка при остановке WMI watcher");
                }
            }
            _watchers.Clear();
        }
        
        return Task.CompletedTask;
    }

    /// <summary>
    /// Создание WMI event watchers для создания и удаления процессов
    /// </summary>
    private void CreateEventWatchers(string processName)
    {
        try
        {
            // WMI query для события создания процесса
            // __InstanceCreationEvent срабатывает при запуске процесса
            var creationQuery = new WqlEventQuery(
                $"SELECT * FROM __InstanceCreationEvent " +
                $"WITHIN 1 " +  // Polling interval в секундах
                $"WHERE TargetInstance ISA \"Win32_Process\" " +
                $"AND TargetInstance.Name = \"{processName}\""
            );
            
            var creationWatcher = new ManagementEventWatcher(creationQuery);
            creationWatcher.EventArrived += OnProcessStarted;
            
            // WMI query для события удаления процесса
            // __InstanceDeletionEvent срабатывает при завершении процесса
            var deletionQuery = new WqlEventQuery(
                $"SELECT * FROM __InstanceDeletionEvent " +
                $"WITHIN 1 " +
                $"WHERE TargetInstance ISA \"Win32_Process\" " +
                $"AND TargetInstance.Name = \"{processName}\""
            );
            
            var deletionWatcher = new ManagementEventWatcher(deletionQuery);
            deletionWatcher.EventArrived += OnProcessExited;
            
            lock (_lock)
            {
                _watchers.Add(creationWatcher);
                _watchers.Add(deletionWatcher);
            }
            
            // Запускаем watchers асинхронно
            _ = Task.Run(async () =>
            {
                try
                {
                    creationWatcher.Start();
                    _logger.LogDebug("Создан watcher для создания {ProcessName}", processName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка запуска creation watcher для {ProcessName}", processName);
                }
            });
            
            _ = Task.Run(async () =>
            {
                try
                {
                    deletionWatcher.Start();
                    _logger.LogDebug("Создан watcher для удаления {ProcessName}", processName);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Ошибка запуска deletion watcher для {ProcessName}", processName);
                }
            });
        }
        catch (ManagementException ex)
        {
            _logger.LogError(ex, "WMI ошибка при создании watchers для {ProcessName}", processName);
            throw;
        }
    }

    /// <summary>
    /// Обработчик события запуска процесса
    /// </summary>
    private void OnProcessStarted(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processObj = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = processObj["Name"]?.ToString() ?? string.Empty;
            var processId = (uint?)processObj["ProcessId"] ?? 0;
            var processPath = processObj["ExecutablePath"]?.ToString() ?? string.Empty;
            
            var processInfo = new ProcessInfo
            {
                Name = processName,
                Path = processPath,
                Pid = (int)processId,
                StartTime = DateTime.Now,
                State = ProcessState.Running
            };
            
            lock (_lock)
            {
                _runningProcesses[processName] = processInfo;
            }
            
            _logger.LogInformation("✅ Обнаружен запуск процесса: {ProcessName} (PID: {Pid})", 
                processName, processId);
            
            ProcessStarted?.Invoke(this, processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки события запуска процесса");
        }
    }

    /// <summary>
    /// Обработчик события завершения процесса
    /// </summary>
    private void OnProcessExited(object sender, EventArrivedEventArgs e)
    {
        try
        {
            var processObj = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = processObj["Name"]?.ToString() ?? string.Empty;
            var processId = (uint?)processObj["ProcessId"] ?? 0;
            var processPath = processObj["ExecutablePath"]?.ToString() ?? string.Empty;
            
            var processInfo = new ProcessInfo
            {
                Name = processName,
                Path = processPath,
                Pid = (int)processId,
                StartTime = DateTime.Now, // Время не точное, т.к. процесс уже завершен
                State = ProcessState.Exited
            };
            
            lock (_lock)
            {
                _runningProcesses.Remove(processName);
            }
            
            _logger.LogInformation("❌ Обнаружено завершение процесса: {ProcessName} (PID: {Pid})", 
                processName, processId);
            
            ProcessExited?.Invoke(this, processInfo);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки события завершения процесса");
        }
    }

    /// <summary>
    /// Проверка уже запущенных процессов при старте
    /// </summary>
    private void CheckAlreadyRunningProcesses()
    {
        try
        {
            var searcher = new ManagementObjectSearcher(
                "SELECT Name, ProcessId, ExecutablePath FROM Win32_Process"
            );
            
            foreach (var obj in searcher.Get().Cast<ManagementObject>())
            {
                var processName = obj["Name"]?.ToString() ?? string.Empty;
                
                if (_monitoredProcesses.Contains(processName))
                {
                    var processId = (uint?)obj["ProcessId"] ?? 0;
                    var processPath = obj["ExecutablePath"]?.ToString() ?? string.Empty;
                    
                    var processInfo = new ProcessInfo
                    {
                        Name = processName,
                        Path = processPath,
                        Pid = (int)processId,
                        StartTime = DateTime.Now,
                        State = ProcessState.Running
                    };
                    
                    lock (_lock)
                    {
                        _runningProcesses[processName] = processInfo;
                    }
                    
                    _logger.LogInformation("🔍 Обнаружен уже запущенный процесс: {ProcessName} (PID: {Pid})", 
                        processName, processId);
                    
                    ProcessStarted?.Invoke(this, processInfo);
                }
            }
        }
        catch (ManagementException ex)
        {
            _logger.LogError(ex, "WMI ошибка при проверке запущенных процессов");
        }
    }

    /// <summary>
    /// Добавить процесс для мониторинга
    /// </summary>
    public void AddProcessToMonitor(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        
        lock (_lock)
        {
            if (_monitoredProcesses.Add(processName))
            {
                _logger.LogDebug("Добавлен процесс для мониторинга: {ProcessName}", processName);
                
                // Если уже запущен, создаем watchers
                if (_shutdownCts != null && !_shutdownCts.IsCancellationRequested)
                {
                    CreateEventWatchers(processName);
                }
            }
        }
    }

    /// <summary>
    /// Удалить процесс из мониторинга
    /// </summary>
    public void RemoveProcessFromMonitor(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        
        lock (_lock)
        {
            if (_monitoredProcesses.Remove(processName))
            {
                _logger.LogDebug("Удален процесс из мониторинга: {ProcessName}", processName);
                _runningProcesses.Remove(processName);
            }
        }
    }

    /// <summary>
    /// Получить список активных monitored процессов
    /// </summary>
    public IReadOnlyList<ProcessInfo> GetRunningProcesses()
    {
        lock (_lock)
        {
            return _runningProcesses.Values.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Освобождение ресурсов
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _shutdownCts?.Cancel();
        _shutdownCts?.Dispose();
        
        StopAsync().Wait();
        
        GC.SuppressFinalize(this);
    }
}
