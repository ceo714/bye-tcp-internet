using System.Collections.Concurrent;
using System.Management;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Monitoring;

/// <summary>
/// Монитор процессов на основе WMI
/// Использует event-driven модель вместо polling
/// </summary>
public sealed class WmiProcessMonitor : IProcessMonitor, IDisposable
{
    private readonly ILogger<WmiProcessMonitor> _logger;
    private readonly ConcurrentDictionary<string, byte> _filters = new(StringComparer.OrdinalIgnoreCase);
    private readonly ConcurrentDictionary<int, ProcessInfo> _runningProcesses = new();
    private readonly List<ManagementEventWatcher> _watchers = new();
    private bool _disposed;
    private CancellationTokenSource? _shutdownCts;

    public event EventHandler<ProcessEvent>? ProcessEventReceived;

    public WmiProcessMonitor(ILogger<WmiProcessMonitor> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Добавляем фильтры по умолчанию
        AddProcessFilter("cs2.exe");
        AddProcessFilter("qbittorrent.exe");
        AddProcessFilter("Discord.exe");
    }

    public Task StartAsync(CancellationToken ct)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        
        _logger.LogInformation("🔍 Запуск WMI Process Monitor");
        _shutdownCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        
        // Создаем WMI watchers для каждого процесса
        foreach (var processName in _filters.Keys)
        {
            CreateEventWatchers(processName);
        }
        
        // Сканируем уже запущенные процессы
        _ = ScanExistingProcessesAsync(ct);
        
        return Task.CompletedTask;
    }

    private void CreateEventWatchers(string processName)
    {
        try
        {
            // WMI query для события создания процесса
            var creationQuery = new WqlEventQuery(
                $"SELECT * FROM __InstanceCreationEvent " +
                $"WITHIN 2 " +
                $"WHERE TargetInstance ISA \"Win32_Process\" " +
                $"AND TargetInstance.Name = \"{processName}\""
            );
            
            var creationWatcher = new ManagementEventWatcher(creationQuery);
            creationWatcher.EventArrived += (s, e) => OnProcessStarted(e);
            
            // WMI query для события удаления процесса
            var deletionQuery = new WqlEventQuery(
                $"SELECT * FROM __InstanceDeletionEvent " +
                $"WITHIN 2 " +
                $"WHERE TargetInstance ISA \"Win32_Process\" " +
                $"AND TargetInstance.Name = \"{processName}\""
            );
            
            var deletionWatcher = new ManagementEventWatcher(deletionQuery);
            deletionWatcher.EventArrived += (s, e) => OnProcessStopped(e);
            
            _watchers.Add(creationWatcher);
            _watchers.Add(deletionWatcher);
            
            creationWatcher.Start();
            deletionWatcher.Start();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "WMI ошибка при создании watchers для {ProcessName}", processName);
        }
    }

    private void OnProcessStarted(EventArrivedEventArgs e)
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
            
            _runningProcesses[(int)processId] = processInfo;
            
            _logger.LogInformation("▶️ Процесс запущен: {ProcessName} (PID: {Pid})", processName, processId);
            
            ProcessEventReceived?.Invoke(this, new ProcessEvent
            {
                Type = ProcessEventType.Start,
                ProcessName = processName,
                ProcessId = (int)processId,
                FullPath = processPath,
                Timestamp = DateTime.UtcNow,
                ParentProcessId = 0
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки события запуска процесса");
        }
    }

    private void OnProcessStopped(EventArrivedEventArgs e)
    {
        try
        {
            var processObj = (ManagementBaseObject)e.NewEvent["TargetInstance"];
            var processName = processObj["Name"]?.ToString() ?? string.Empty;
            var processId = (uint?)processObj["ProcessId"] ?? 0;
            
            if (_runningProcesses.TryRemove((int)processId, out var existingProcess))
            {
                _logger.LogInformation("⏹️ Процесс завершен: {ProcessName} (PID: {Pid})", processName, processId);
                
                ProcessEventReceived?.Invoke(this, new ProcessEvent
                {
                    Type = ProcessEventType.End,
                    ProcessName = processName,
                    ProcessId = (int)processId,
                    FullPath = existingProcess.Path,
                    Timestamp = DateTime.UtcNow,
                    ParentProcessId = 0
                });
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка обработки события завершения процесса");
        }
    }

    private async Task ScanExistingProcessesAsync(CancellationToken ct)
    {
        try
        {
            await Task.Delay(500, ct);
            
            var processes = System.Diagnostics.Process.GetProcessesByName("*");
            
            foreach (var process in processes)
            {
                if (ct.IsCancellationRequested)
                    break;
                
                try
                {
                    var processName = process.ProcessName + ".exe";
                    
                    if (_filters.ContainsKey(processName))
                    {
                        var processInfo = new ProcessInfo
                        {
                            Name = processName,
                            Path = GetProcessPath(process),
                            Pid = process.Id,
                            StartTime = process.StartTime,
                            State = ProcessState.Running
                        };
                        
                        _runningProcesses[process.Id] = processInfo;
                        
                        _logger.LogInformation("🔍 Обнаружен уже запущенный процесс: {ProcessName} (PID: {Pid})", 
                            processName, process.Id);
                        
                        ProcessEventReceived?.Invoke(this, new ProcessEvent
                        {
                            Type = ProcessEventType.Start,
                            ProcessName = processName,
                            ProcessId = process.Id,
                            FullPath = processInfo.Path,
                            Timestamp = DateTime.UtcNow,
                            ParentProcessId = 0
                        });
                    }
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сканирования существующих процессов");
        }
    }

    private static string GetProcessPath(System.Diagnostics.Process process)
    {
        try
        {
            return process.MainModule?.FileName ?? string.Empty;
        }
        catch
        {
            return string.Empty;
        }
    }

    public Task StopAsync()
    {
        _logger.LogInformation("Остановка WMI Process Monitor");
        
        _shutdownCts?.Cancel();
        
        foreach (var watcher in _watchers)
        {
            try
            {
                watcher.Stop();
                watcher.Dispose();
            }
            catch { }
        }
        _watchers.Clear();
        
        return Task.CompletedTask;
    }

    public void AddProcessFilter(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        _filters[processName] = 1;
    }

    public void RemoveProcessFilter(string processName)
    {
        if (string.IsNullOrWhiteSpace(processName)) return;
        _filters.TryRemove(processName, out _);
    }

    public IReadOnlySet<string> GetMonitoredProcesses()
    {
        return _filters.Keys.ToHashSet(StringComparer.OrdinalIgnoreCase);
    }

    public Task<IReadOnlyList<ProcessInfo>> GetRunningProcessesAsync()
    {
        return Task.FromResult<IReadOnlyList<ProcessInfo>>(_runningProcesses.Values.ToList().AsReadOnly());
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
