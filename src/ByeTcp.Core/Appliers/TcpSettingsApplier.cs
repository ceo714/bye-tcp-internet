using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using System.Diagnostics;
using System.Text;
using ByeTcp.Core.Interfaces;
using ByeTcp.Core.Models;

namespace ByeTcp.Core.Appliers;

/// <summary>
/// Модуль применения настроек TCP/IP через Registry и NetSh
/// </summary>
public sealed class TcpSettingsApplier : ISettingsApplier
{
    private readonly ILogger<TcpSettingsApplier> _logger;
    
    // Registry path для TCP/IP параметров
    private const string TcpipParametersPath = 
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    
    private const string GlobalTcpipParametersPath = 
        @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    public TcpSettingsApplier(ILogger<TcpSettingsApplier> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Применить профиль настроек TCP/IP
    /// </summary>
    public async Task<bool> ApplyProfileAsync(TcpProfile profile, CancellationToken cancellationToken)
    {
        _logger.LogInformation("🔧 Применение профиля: {ProfileName} ({ProfileId})", 
            profile.Name, profile.Id);
        
        var actions = new List<ProfileAction>();
        
        // Генерируем действия из профиля
        if (profile.TcpAckFrequency.HasValue)
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.Registry,
                Target = "TcpAckFrequency",
                Value = profile.TcpAckFrequency.Value.ToString()
            });
        }
        
        if (profile.TcpNoDelay.HasValue)
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.Registry,
                Target = "TCPNoDelay",
                Value = profile.TcpNoDelay.Value.ToString()
            });
        }
        
        if (profile.TcpDelAckTicks.HasValue)
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.Registry,
                Target = "TcpDelAckTicks",
                Value = profile.TcpDelAckTicks.Value.ToString()
            });
        }
        
        if (!string.IsNullOrEmpty(profile.ReceiveWindowAutoTuningLevel))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "autotuninglevel",
                Value = profile.ReceiveWindowAutoTuningLevel
            });
        }
        
        if (!string.IsNullOrEmpty(profile.CongestionProvider))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "congestionprovider",
                Value = profile.CongestionProvider
            });
        }
        
        if (!string.IsNullOrEmpty(profile.EcnCapability))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "ecncapability",
                Value = profile.EcnCapability
            });
        }
        
        if (!string.IsNullOrEmpty(profile.ReceiveWindowScaling))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "receivewindowscaling",
                Value = profile.ReceiveWindowScaling
            });
        }
        
        if (!string.IsNullOrEmpty(profile.Sack))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "sack",
                Value = profile.Sack
            });
        }
        
        if (!string.IsNullOrEmpty(profile.Timestamps))
        {
            actions.Add(new ProfileAction
            {
                Type = ActionType.NetSh,
                Target = "timestamps",
                Value = profile.Timestamps
            });
        }
        
        // Применяем все действия
        var success = true;
        foreach (var action in actions)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                _logger.LogWarning("Применение профиля отменено");
                return false;
            }
            
            var result = await ApplyActionAsync(action, cancellationToken);
            if (!result)
            {
                success = false;
                _logger.LogError("Не удалось применить действие: {Target} = {Value}", 
                    action.Target, action.Value);
            }
        }
        
        if (success)
        {
            _logger.LogInformation("✅ Профиль {ProfileName} успешно применен", profile.Name);
        }
        else
        {
            _logger.LogWarning("⚠️ Профиль {ProfileName} применен с ошибками", profile.Name);
        }
        
        return success;
    }

    /// <summary>
    /// Применить отдельное действие
    /// </summary>
    public async Task<bool> ApplyActionAsync(ProfileAction action, CancellationToken cancellationToken)
    {
        return action.Type switch
        {
            ActionType.Registry => await ApplyRegistrySettingAsync(action, cancellationToken),
            ActionType.NetSh => await ApplyNetShSettingAsync(action, cancellationToken),
            ActionType.Wfp => await ApplyWfpSettingAsync(action, cancellationToken),
            ActionType.Custom => await ApplyCustomSettingAsync(action, cancellationToken),
            _ => throw new NotSupportedException($"Неподдерживаемый тип действия: {action.Type}")
        };
    }

    /// <summary>
    /// Применение настройки через реестр
    /// </summary>
    private async Task<bool> ApplyRegistrySettingAsync(ProfileAction action, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("📝 Registry: {Target} = {Value}", action.Target, action.Value);
            
            // Получаем все GUID сетевых интерфейсов
            var interfaceGuids = GetNetworkInterfaceGuids();
            
            foreach (var guid in interfaceGuids)
            {
                if (cancellationToken.IsCancellationRequested)
                    return false;
                
                var keyPath = $@"{TcpipParametersPath}\{guid}";
                
                using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                if (key != null)
                {
                    var value = int.Parse(action.Value ?? "0");
                    key.SetValue(action.Target, value, RegistryValueKind.DWord);
                    _logger.LogDebug("  → Интерфейс {Guid}: {Target} = {Value}", guid, action.Target, value);
                }
            }
            
            // Для глобальных параметров
            if (IsGlobalParameter(action.Target))
            {
                using var key = Registry.LocalMachine.OpenSubKey(GlobalTcpipParametersPath, true);
                if (key != null)
                {
                    var value = int.Parse(action.Value ?? "0");
                    key.SetValue(action.Target, value, RegistryValueKind.DWord);
                    _logger.LogDebug("  → Глобально: {Target} = {Value}", action.Target, value);
                }
            }
            
            await Task.Delay(100, cancellationToken); // Небольшая задержка для применения
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения registry настройки: {Target}", action.Target);
            return false;
        }
    }

    /// <summary>
    /// Применение настройки через NetSh
    /// </summary>
    private async Task<bool> ApplyNetShSettingAsync(ProfileAction action, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogDebug("🌐 NetSh: int tcp set global {Target}={Value}", action.Target, action.Value);
            
            var command = $"int tcp set global {action.Target}={action.Value}";
            var result = await RunNetShCommandAsync(command, cancellationToken);
            
            if (result.ExitCode == 0)
            {
                _logger.LogDebug("  → NetSh команда выполнена успешно");
                return true;
            }
            else
            {
                _logger.LogWarning("  → NetSh команда вернула код {ExitCode}: {Error}", 
                    result.ExitCode, result.Error);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения NetSh настройки: {Target}", action.Target);
            return false;
        }
    }

    /// <summary>
    /// Применение настройки через WFP (заглушка для будущей реализации)
    /// </summary>
    private async Task<bool> ApplyWfpSettingAsync(ProfileAction action, CancellationToken cancellationToken)
    {
        _logger.LogWarning("⚠️ WFP настройка запрошена, но модуль WFP еще не реализован: {Target}", action.Target);
        // TODO: Интеграция с WFP драйвером
        return await Task.FromResult(false);
    }

    /// <summary>
    /// Применение кастомной настройки
    /// </summary>
    private async Task<bool> ApplyCustomSettingAsync(ProfileAction action, CancellationToken cancellationToken)
    {
        _logger.LogWarning("⚠️ Кастомная настройка не реализована: {Target}", action.Target);
        return await Task.FromResult(false);
    }

    /// <summary>
    /// Выполнение NetSh команды
    /// </summary>
    private async Task<(int ExitCode, string Output, string Error)> RunNetShCommandAsync(
        string command, CancellationToken cancellationToken)
    {
        var tcs = new TaskCompletionSource<(int, string, string)>();
        
        using var process = new Process
        {
            StartInfo = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = command,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                UseShellExecute = false,
                CreateNoWindow = true,
                Verb = "runas" // Требует administrator privileges
            },
            EnableRaisingEvents = true
        };
        
        var outputBuilder = new StringBuilder();
        var errorBuilder = new StringBuilder();
        
        process.OutputDataReceived += (s, e) =>
        {
            if (e.Data != null)
                outputBuilder.AppendLine(e.Data);
        };
        
        process.ErrorDataReceived += (s, e) =>
        {
            if (e.Data != null)
                errorBuilder.AppendLine(e.Data);
        };
        
        process.Exited += (s, e) =>
        {
            tcs.TrySetResult((
                process.ExitCode,
                outputBuilder.ToString().Trim(),
                errorBuilder.ToString().Trim()
            ));
        };
        
        try
        {
            process.Start();
            process.BeginOutputReadLine();
            process.BeginErrorReadLine();
            
            using (cancellationToken.Register(() =>
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    tcs.TrySetException(new OperationCanceledException("NetSh команда отменена"));
                }
            }))
            {
                return await tcs.Task;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка выполнения NetSh команды: {Command}", command);
            throw;
        }
    }

    /// <summary>
    /// Получить GUID всех сетевых интерфейсов
    /// </summary>
    private List<string> GetNetworkInterfaceGuids()
    {
        var guids = new List<string>();
        
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipParametersPath);
            if (baseKey != null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    // Проверяем, что это GUID сетевого интерфейса
                    if (Guid.TryParse(subKeyName, out _))
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName);
                        if (subKey != null)
                        {
                            // Проверяем наличие DHCPNameServer или NameServer
                            if (subKey.GetValue("DhcpNameServer") != null || 
                                subKey.GetValue("NameServer") != null)
                            {
                                guids.Add(subKeyName);
                                _logger.LogDebug("Найден сетевой интерфейс: {Guid}", subKeyName);
                            }
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения списка сетевых интерфейсов");
        }
        
        return guids;
    }

    /// <summary>
    /// Проверка, является ли параметр глобальным
    /// </summary>
    private static bool IsGlobalParameter(string parameterName)
    {
        var globalParameters = new[]
        {
            "DefaultTTL",
            "EnableICMPRedirect",
            "EnablePMTUDiscovery",
            "EnableSecurityFilters",
            "IPEnableRouter",
            "PerformRouterDiscovery",
            "TcpMaxConnectRetransmissions",
            "TcpMaxDataRetransmissions",
            "TcpNumConnections"
        };
        
        return globalParameters.Contains(parameterName, StringComparer.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Экспорт текущих настроек в файл
    /// </summary>
    public async Task<string> ExportCurrentSettingsAsync(string backupPath)
    {
        _logger.LogInformation("📦 Экспорт текущих настроек в {Path}", backupPath);
        
        var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
        var fileName = $"backup-{timestamp}.reg";
        var fullPath = Path.Combine(backupPath, fileName);
        
        try
        {
            // Экспорт через reg export
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"export \"HKLM\\{TcpipParametersPath}\" \"{fullPath}\" /y",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                },
                EnableRaisingEvents = true
            };
            
            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                _logger.LogInformation("✅ Настройки экспортированы в {FullPath}", fullPath);
                return fullPath;
            }
            else
            {
                throw new Exception($"reg.exe вернул код {process.ExitCode}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка экспорта настроек");
            throw;
        }
    }

    /// <summary>
    /// Восстановление настроек из резервной копии
    /// </summary>
    public async Task<bool> RestoreFromBackupAsync(string backupPath)
    {
        _logger.LogInformation("♻️ Восстановление настроек из {Path}", backupPath);
        
        try
        {
            using var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "reg.exe",
                    Arguments = $"import \"{backupPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    Verb = "runas"
                },
                EnableRaisingEvents = true
            };
            
            process.Start();
            await process.WaitForExitAsync();
            
            if (process.ExitCode == 0)
            {
                _logger.LogInformation("✅ Настройки восстановлены из {BackupPath}", backupPath);
                return true;
            }
            else
            {
                _logger.LogError("reg.exe вернул код {ExitCode}", process.ExitCode);
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка восстановления настроек");
            return false;
        }
    }

    /// <summary>
    /// Сброс к настройкам по умолчанию
    /// </summary>
    public async Task<bool> ResetToDefaultsAsync()
    {
        _logger.LogInformation("🔄 Сброс к настройкам по умолчанию");
        
        try
        {
            // Сброс через NetSh
            var commands = new[]
            {
                "int tcp set global autotuninglevel=normal",
                "int tcp set global congestionprovider=default",
                "int tcp set global ecncapability=default",
                "int tcp set global receivewindowscaling=enabled",
                "int tcp set global sack=enabled",
                "int tcp set global timestamps=enabled"
            };
            
            foreach (var command in commands)
            {
                await RunNetShCommandAsync(command, CancellationToken.None);
            }
            
            _logger.LogInformation("✅ Настройки сброшены к значениям по умолчанию");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка сброса настроек");
            return false;
        }
    }

    /// <summary>
    /// Получить текущие значения параметров
    /// </summary>
    public async Task<Dictionary<string, string>> GetCurrentSettingsAsync()
    {
        var settings = new Dictionary<string, string>();
        
        try
        {
            // Получаем текущие NetSh настройки
            var process = new Process
            {
                StartInfo = new ProcessStartInfo
                {
                    FileName = "netsh.exe",
                    Arguments = "int tcp show global",
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                    CreateNoWindow = true
                }
            };
            
            process.Start();
            var output = await process.StandardOutput.ReadToEndAsync();
            await process.WaitForExitAsync();
            
            // Парсим вывод
            foreach (var line in output.Split('\n'))
            {
                var parts = line.Split(':');
                if (parts.Length == 2)
                {
                    var key = parts[0].Trim();
                    var value = parts[1].Trim();
                    settings[key] = value;
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка получения текущих настроек");
        }
        
        return settings;
    }
}
