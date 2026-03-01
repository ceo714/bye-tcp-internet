using Microsoft.Extensions.Logging;
using Microsoft.Win32;
using ByeTcp.Contracts;

namespace ByeTcp.Execution.Providers;

/// <summary>
/// Провайдер настроек через прямой доступ к реестру (без reg.exe процесса)
/// 
/// Преимущества:
/// - Быстрее чем вызов процесса
/// - Лучшая обработка ошибок
/// - Транзакционность (через RegistryKey)
/// </summary>
public sealed class RegistrySettingsProvider : ISettingsProvider
{
    private readonly ILogger<RegistrySettingsProvider> _logger;
    private const string TcpipParametersPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters\Interfaces";
    private const string GlobalTcpipParametersPath = @"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters";

    public string Name => "Registry";
    public SettingType SettingType => SettingType.Registry;
    public bool IsAvailable => true;

    public RegistrySettingsProvider(ILogger<RegistrySettingsProvider> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes,
        CancellationToken ct)
    {
        var appliedChanges = new List<SettingChange>();
        var changesList = changes.ToList();
        
        try
        {
            // Получаем GUID сетевых интерфейсов
            var interfaceGuids = GetNetworkInterfaceGuids();
            
            foreach (var change in changesList)
            {
                ct.ThrowIfCancellationRequested();
                
                // Читаем предыдущее значение
                var previousValue = await ReadRegistryValueAsync(change.Key, ct);
                
                // Применяем ко всем интерфейсам
                foreach (var guid in interfaceGuids)
                {
                    var keyPath = $@"{TcpipParametersPath}\{guid}";
                    
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath, true);
                    if (key != null)
                    {
                        var value = ParseRegistryValue(change.NewValue);
                        key.SetValue(change.Key, value, RegistryValueKind.DWord);
                        
                        _logger.LogDebug("  → Интерфейс {Guid}: {Key} = {Value}", 
                            guid, change.Key, value);
                    }
                }
                
                // Для глобальных параметров
                if (IsGlobalParameter(change.Key))
                {
                    using var key = Registry.LocalMachine.OpenSubKey(GlobalTcpipParametersPath, true);
                    if (key != null)
                    {
                        var value = ParseRegistryValue(change.NewValue);
                        key.SetValue(change.Key, value, RegistryValueKind.DWord);
                        
                        _logger.LogDebug("  → Глобально: {Key} = {Value}", change.Key, value);
                    }
                }
                
                appliedChanges.Add(change with { PreviousValue = previousValue });
            }
            
            // Небольшая задержка для применения настроек
            await Task.Delay(100, ct);
            
            return ProviderResult.CreateSuccess(appliedChanges);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения registry настроек");
            return ProviderResult.CreateFailure(ex.Message, ex);
        }
    }

    private async Task<string> ReadRegistryValueAsync(string keyName, CancellationToken ct)
    {
        return await Task.Run(() =>
        {
            try
            {
                // Читаем с первого доступного интерфейса
                var interfaceGuids = GetNetworkInterfaceGuids();
                
                foreach (var guid in interfaceGuids)
                {
                    var keyPath = $@"{TcpipParametersPath}\{guid}";
                    
                    using var key = Registry.LocalMachine.OpenSubKey(keyPath, false);
                    if (key != null)
                    {
                        var value = key.GetValue(keyName);
                        if (value != null)
                        {
                            return value.ToString();
                        }
                    }
                }
                
                // Пробуем глобальный ключ
                using var globalKey = Registry.LocalMachine.OpenSubKey(GlobalTcpipParametersPath, false);
                if (globalKey != null)
                {
                    var value = globalKey.GetValue(keyName);
                    if (value != null)
                    {
                        return value.ToString();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка чтения registry значения {Key}", keyName);
            }
            
            return "default";
        }, ct);
    }

    private List<string> GetNetworkInterfaceGuids()
    {
        var guids = new List<string>();
        
        try
        {
            using var baseKey = Registry.LocalMachine.OpenSubKey(TcpipParametersPath, false);
            if (baseKey != null)
            {
                foreach (var subKeyName in baseKey.GetSubKeyNames())
                {
                    if (Guid.TryParse(subKeyName, out _))
                    {
                        using var subKey = baseKey.OpenSubKey(subKeyName, false);
                        if (subKey != null)
                        {
                            // Проверяем наличие DHCPNameServer или NameServer
                            if (subKey.GetValue("DhcpNameServer") != null ||
                                subKey.GetValue("NameServer") != null)
                            {
                                guids.Add(subKeyName);
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

    private static int ParseRegistryValue(string value)
    {
        if (int.TryParse(value, out var result))
        {
            return result;
        }
        
        // Special values
        return value.ToLowerInvariant() switch
        {
            "default" => 0,
            "disabled" => 0,
            "enabled" => 1,
            _ => 0
        };
    }

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
}
