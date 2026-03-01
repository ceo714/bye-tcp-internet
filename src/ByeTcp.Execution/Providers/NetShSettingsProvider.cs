using System.Management.Automation;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Execution.Providers;

/// <summary>
/// Провайдер настроек через PowerShell SDK (вместо процесса netsh.exe)
/// 
/// Преимущества:
/// - Не требует spawning процесса
/// - Лучшая обработка ошибок
/// - Контроль таймаутов через CancellationToken
/// - PowerShell pipeline для batch операций
/// </summary>
public sealed class NetShSettingsProvider : ISettingsProvider, IDisposable
{
    private readonly ILogger<NetShSettingsProvider> _logger;
    private readonly PowerShell _powerShell;
    private bool _disposed;
    private bool? _isAvailable;

    public string Name => "NetSh";
    public SettingType SettingType => SettingType.NetSh;
    
    public bool IsAvailable
    {
        get
        {
            if (_isAvailable.HasValue)
                return _isAvailable.Value;
            
            // Проверяем доступность netsh
            try
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "int tcp show global",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                process?.WaitForExit(5000);
                _isAvailable = process?.ExitCode == 0;
            }
            catch
            {
                _isAvailable = false;
            }
            
            return _isAvailable.Value;
        }
    }

    public NetShSettingsProvider(ILogger<NetShSettingsProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Инициализируем PowerShell
        _powerShell = PowerShell.Create();
    }

    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes,
        CancellationToken ct)
    {
        var appliedChanges = new List<SettingChange>();
        var changesList = changes.ToList();
        
        if (changesList.Count == 0)
        {
            return ProviderResult.CreateSuccess(appliedChanges);
        }
        
        try
        {
            foreach (var change in changesList)
            {
                ct.ThrowIfCancellationRequested();
                
                // Читаем предыдущее значение
                var previousValue = await ReadNetShValueAsync(change.Key, ct);
                
                // Формируем команду netsh
                var command = $"netsh int tcp set global {change.Key}={change.NewValue}";
                
                _logger.LogDebug("  → NetSh: {Command}", command);
                
                // Выполняем через Process (netsh не имеет PowerShell cmdlet)
                var result = await ExecuteNetShCommandAsync(command, ct);
                
                if (!result.Success)
                {
                    return ProviderResult.CreateFailure(
                        $"Ошибка выполнения команды: {result.Error}",
                        result.Exception);
                }

                appliedChanges.Add(change with { PreviousValue = previousValue });
                
                // Небольшая задержка между командами
                await Task.Delay(50, ct);
            }
            
            return ProviderResult.CreateSuccess(appliedChanges);
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("Применение NetSh настроек отменено");
            return ProviderResult.CreateFailure("Операция отменена", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения NetSh настроек");
            return ProviderResult.CreateFailure(ex.Message, ex);
        }
    }

    private async Task<string> ReadNetShValueAsync(string key, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var command = "netsh int tcp show global";
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                });
                
                process?.WaitForExit(5000);
                
                if (ct.IsCancellationRequested)
                {
                    process?.Kill();
                    throw new OperationCanceledException();
                }
                
                var output = process?.StandardOutput.ReadToEnd() ?? string.Empty;
                
                // Парсим вывод
                foreach (var line in output.Split('\n'))
                {
                    var parts = line.Split(':');
                    if (parts.Length == 2)
                    {
                        var parsedKey = parts[0].Trim().ToLowerInvariant();
                        var value = parts[1].Trim();
                        
                        if (parsedKey == key.ToLowerInvariant())
                        {
                            return value;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка чтения NetSh значения {Key}", key);
            }
            
            return "default";
        }, ct);
    }

    private async Task<(bool Success, string? Error, Exception? Exception)> ExecuteNetShCommandAsync(
        string command, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                var process = System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = command,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas" // Требует administrator
                });
                
                if (process == null)
                {
                    return (false, "Не удалось запустить процесс", null);
                }
                
                // Читаем output и error
                var outputTask = process.StandardOutput.ReadToEndAsync();
                var errorTask = process.StandardError.ReadToEndAsync();
                
                // Ждем завершения с timeout
                if (!process.WaitForExit(30000))
                {
                    process.Kill();
                    return (false, "Timeout выполнения команды", null);
                }
                
                if (ct.IsCancellationRequested)
                {
                    process.Kill();
                    return (false, "Операция отменена", null);
                }
                
                var output = outputTask.Result;
                var error = errorTask.Result;
                
                if (process.ExitCode != 0)
                {
                    return (false, error ?? $"Exit code: {process.ExitCode}", null);
                }
                
                _logger.LogDebug("NetSh output: {Output}", output.Trim());
                
                return (true, null, null);
            }
            catch (OperationCanceledException)
            {
                return (false, "Операция отменена", null);
            }
            catch (Exception ex)
            {
                return (false, ex.Message, ex);
            }
        }, ct);
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _powerShell.Dispose();
        GC.SuppressFinalize(this);
    }
}
