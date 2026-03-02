using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Execution.Providers;

/// <summary>
/// Опциональный WFP провайдер с fallback на user-mode
/// 
/// Особенности:
/// - Probe доступности драйвера при инициализации
/// - Fallback на user-mode при отсутствии подписанного драйвера
/// - Четкий контракт взаимодействия
/// - Логирование статуса доступности
/// </summary>
public sealed class WfpSettingsProvider : ISettingsProvider, IDisposable
{
    private readonly ILogger<WfpSettingsProvider> _logger;
    private readonly IWfpDriver? _driver;
    private readonly bool _isAvailable;
    private bool _disposed;

    public string Name => "WFP";
    public SettingType SettingType => SettingType.Wfp;
    public bool IsAvailable => _isAvailable;

    public WfpSettingsProvider(ILogger<WfpSettingsProvider> logger, IWfpDriver? driver = null)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _driver = driver;
        
        // Probe доступности WFP драйвера
        _isAvailable = ProbeDriverAvailability();
        
        if (_isAvailable)
        {
            _logger.LogInformation("✅ WFP драйвер доступен");
        }
        else
        {
            _logger.LogWarning(
                "⚠️ WFP драйвер недоступен. WFP настройки будут пропущены. " +
                "Требуется подписанный драйвер или тестовый режим Windows.");
        }
    }

    private bool ProbeDriverAvailability()
    {
        if (_driver == null)
        {
            _logger.LogDebug("WFP драйвер не предоставлен");
            return false;
        }
        
        try
        {
            // Проверяем связь с драйвером
            var status = _driver.GetStatus();
            
            if (status.IsConnected)
            {
                _logger.LogDebug("WFP драйвер подключен. Версия: {Version}", status.DriverVersion);
                return true;
            }
            
            _logger.LogWarning("WFP драйвер не подключен. Статус: {Status}", status.StatusMessage);
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка probe WFP драйвера");
            return false;
        }
    }

    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes,
        CancellationToken ct)
    {
        var appliedChanges = new List<SettingChange>();
        
        if (!_isAvailable)
        {
            // Fallback: не применяем WFP настройки, но не считаем это ошибкой
            _logger.LogDebug("WFP недоступен, пропускаем {Count} изменений", changes.Count());
            return ProviderResult.CreateSuccess(appliedChanges);
        }
        
        try
        {
            foreach (var change in changes)
            {
                ct.ThrowIfCancellationRequested();
                
                _logger.LogDebug("Применение WFP настройки: {Key} = {Value}", 
                    change.Key, change.NewValue);
                
                var previousValue = await ReadWfpValueAsync(change.Key, ct);
                
                var result = await _driver!.ApplySettingAsync(
                    change.Key, 
                    change.NewValue, 
                    ct);
                
                if (!result)
                {
                    return ProviderResult.CreateFailure(
                        $"Не удалось применить WFP настройку {change.Key}",
                        null);
                }
                
                appliedChanges.Add(change with { PreviousValue = previousValue });
            }
            
            return ProviderResult.CreateSuccess(appliedChanges);
        }
        catch (OperationCanceledException)
        {
            return ProviderResult.CreateFailure("Операция отменена", null);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка применения WFP настроек");
            return ProviderResult.CreateFailure(ex.Message, ex);
        }
    }

    private async Task<string> ReadWfpValueAsync(string key, CancellationToken ct)
    {
        if (_driver == null)
            return "default";
        
        try
        {
            var value = await _driver.ReadSettingAsync(key, ct);
            return value ?? "default";
        }
        catch
        {
            return "default";
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        _driver?.Dispose();
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Интерфейс WFP драйвера (контракт для kernel-mode компонента)
/// </summary>
public interface IWfpDriver : IDisposable
{
    WfpDriverStatus GetStatus();
    Task<bool> ApplySettingAsync(string key, string value, CancellationToken ct);
    Task<string?> ReadSettingAsync(string key, CancellationToken ct);
}

/// <summary>
/// Статус WFP драйвера
/// </summary>
public record WfpDriverStatus
{
    public bool IsConnected { get; init; }
    public string DriverVersion { get; init; } = string.Empty;
    public string StatusMessage { get; init; } = string.Empty;
}

/// <summary>
/// Stub реализация для fallback (когда драйвер недоступен)
/// </summary>
public sealed class StubWfpDriver : IWfpDriver
{
    public WfpDriverStatus GetStatus() => new()
    {
        IsConnected = false,
        DriverVersion = "N/A",
        StatusMessage = "Stub driver - not available"
    };

    public Task<bool> ApplySettingAsync(string key, string value, CancellationToken ct) =>
        Task.FromResult(false);

    public Task<string?> ReadSettingAsync(string key, CancellationToken ct) =>
        Task.FromResult<string?>(null);

    public void Dispose() { }
}
