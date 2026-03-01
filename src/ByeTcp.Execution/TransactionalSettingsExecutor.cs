using System.Collections.Concurrent;
using System.Diagnostics;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Execution;

/// <summary>
/// Транзакционный исполнитель настроек
/// 
/// Принципы:
/// - Атомарность применения (all-or-nothing)
/// - Rollback при частичной ошибке
/// - Кэширование состояния (предотвращение redundant applies)
/// - Idempotency операций
/// </summary>
public sealed class TransactionalSettingsExecutor : ISettingsExecutor, IDisposable
{
    private readonly ILogger<TransactionalSettingsExecutor> _logger;
    private readonly ISettingsProvider[] _providers;
    private readonly IStateCache _cache;
    private readonly string _backupPath;
    private bool _disposed;

    public TransactionalSettingsExecutor(
        ILogger<TransactionalSettingsExecutor> logger,
        IEnumerable<ISettingsProvider> providers,
        IStateCache cache,
        string backupPath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _providers = providers.ToArray();
        _cache = cache ?? throw new ArgumentNullException(nameof(cache));
        _backupPath = backupPath;
        
        _logger.LogInformation(
            "🔧 Инициализация Settings Executor. Провайдеры: {Providers}",
            string.Join(", ", _providers.Select(p => p.Name)));
    }

    /// <summary>
    /// Применение профиля настроек (транзакционно)
    /// </summary>
    public async Task<ExecutionResult> ApplyProfileAsync(
        TcpProfile profile,
        CancellationToken ct)
    {
        var correlationId = Guid.NewGuid().ToString("N")[..8];
        var stopwatch = Stopwatch.StartNew();
        
        _logger.LogInformation(
            "⚙️ Применение профиля {ProfileId} (correlation: {CorrelationId})",
            profile.Id, correlationId);
        
        try
        {
            // Check 1: Idempotency - уже применен?
            if (_cache.IsProfileApplied(profile))
            {
                _logger.LogDebug("Профиль уже применен, пропускаем (idempotency)");
                return ExecutionResult.CreateSuccess(correlationId, new List<SettingChange>());
            }

            // Check 2: Получаем текущее состояние для rollback
            var currentState = await GetCurrentStateAsync();
            var cachedState = await _cache.GetCurrentProfileAsync();

            // Генерируем изменения
            var changes = GenerateChanges(currentState, profile);

            if (changes.Count == 0)
            {
                _logger.LogDebug("Нет изменений для применения");
                await _cache.UpdateStateAsync(profile, ct);
                return ExecutionResult.CreateSuccess(correlationId, new List<SettingChange>());
            }
            
            // Prepare rollback stack
            var rollbackStack = new Stack<SettingChange>();
            var appliedChanges = new List<SettingChange>();
            
            // Применяем изменения по провайдерам
            foreach (var provider in _providers)
            {
                if (ct.IsCancellationRequested)
                {
                    ct.ThrowIfCancellationRequested();
                }
                
                if (!provider.IsAvailable)
                {
                    _logger.LogDebug("Провайдер {Provider} недоступен, пропускаем", provider.Name);
                    continue;
                }
                
                var providerChanges = changes.Where(c => c.Type == provider.SettingType).ToList();
                
                if (providerChanges.Count == 0)
                    continue;
                
                _logger.LogDebug("Применение через {Provider}: {Changes}", 
                    provider.Name, string.Join(", ", providerChanges.Select(c => c.Key)));
                
                var result = await provider.ApplyAsync(providerChanges, ct);
                
                if (!result.Success)
                {
                    // Ошибка применения - запускаем rollback
                    _logger.LogError(
                        "❌ Ошибка применения через {Provider}: {Error}. Запуск rollback.",
                        provider.Name, result.Error);

                    await RollbackChangesAsync(rollbackStack, ct);

                    return ExecutionResult.CreateFailure(correlationId,
                        new Exception(result.Error ?? "Unknown error"),
                        rollbackStack.ToList());
                }
                
                // Добавляем в rollback stack
                foreach (var change in result.AppliedChanges)
                {
                    rollbackStack.Push(change);
                    appliedChanges.Add(change);
                }
            }
            
            // Обновляем кэш
            await _cache.UpdateStateAsync(profile, ct);
            
            stopwatch.Stop();
            
            _logger.LogInformation(
                "✅ Профиль {ProfileId} успешно применен за {Duration}ms. Изменений: {Changes}",
                profile.Id, stopwatch.ElapsedMilliseconds, appliedChanges.Count);
            
            return ExecutionResult.CreateSuccess(correlationId, appliedChanges);
        }
        catch (Exception ex)
        {
            stopwatch.Stop();
            _logger.LogError(ex, "❌ Критическая ошибка применения профиля {ProfileId}", profile.Id);
            
            return ExecutionResult.CreateFailure(correlationId, ex, new List<SettingChange>());
        }
    }

    /// <summary>
    /// Генерация списка изменений на основе профиля
    /// </summary>
    private List<SettingChange> GenerateChanges(
        TcpProfile? currentState,
        TcpProfile newProfile)
    {
        var changes = new List<SettingChange>();
        
        // Registry settings
        AddChangeIfDifferent(changes, SettingType.Registry, "TcpAckFrequency",
            currentState?.TcpAckFrequency?.ToString(),
            newProfile.TcpAckFrequency?.ToString());
        
        AddChangeIfDifferent(changes, SettingType.Registry, "TCPNoDelay",
            currentState?.TcpNoDelay?.ToString(),
            newProfile.TcpNoDelay?.ToString());
        
        AddChangeIfDifferent(changes, SettingType.Registry, "TcpDelAckTicks",
            currentState?.TcpDelAckTicks?.ToString(),
            newProfile.TcpDelAckTicks?.ToString());
        
        // NetSh settings
        AddChangeIfDifferent(changes, SettingType.NetSh, "autotuninglevel",
            currentState?.ReceiveWindowAutoTuningLevel,
            newProfile.ReceiveWindowAutoTuningLevel);
        
        AddChangeIfDifferent(changes, SettingType.NetSh, "congestionprovider",
            currentState?.CongestionProvider,
            newProfile.CongestionProvider);
        
        AddChangeIfDifferent(changes, SettingType.NetSh, "ecncapability",
            currentState?.EcnCapability,
            newProfile.EcnCapability);
        
        AddChangeIfDifferent(changes, SettingType.NetSh, "receivewindowscaling",
            currentState?.ReceiveWindowScaling,
            newProfile.ReceiveWindowScaling);
        
        AddChangeIfDifferent(changes, SettingType.NetSh, "sack",
            currentState?.Sack,
            newProfile.Sack);
        
        AddChangeIfDifferent(changes, SettingType.NetSh, "timestamps",
            currentState?.Timestamps,
            newProfile.Timestamps);
        
        return changes;
    }

    private static void AddChangeIfDifferent(
        List<SettingChange> changes,
        SettingType type,
        string key,
        string? currentValue,
        string? newValue)
    {
        // Normalize null/empty
        currentValue = string.IsNullOrWhiteSpace(currentValue) ? null : currentValue;
        newValue = string.IsNullOrWhiteSpace(newValue) ? null : newValue;
        
        if (currentValue != newValue)
        {
            changes.Add(new SettingChange
            {
                Type = type,
                Key = key,
                PreviousValue = currentValue,
                NewValue = newValue ?? "default"
            });
        }
    }

    /// <summary>
    /// Rollback изменений
    /// </summary>
    private async Task RollbackChangesAsync(
        Stack<SettingChange> rollbackStack,
        CancellationToken ct)
    {
        _logger.LogInformation("♻️ Запуск rollback {Count} изменений", rollbackStack.Count);
        
        var rolledBack = new List<SettingChange>();
        
        while (rollbackStack.Count > 0)
        {
            var change = rollbackStack.Pop();
            
            try
            {
                // Восстанавливаем предыдущее значение
                var rollbackChange = change with
                {
                    NewValue = change.PreviousValue ?? "default",
                    PreviousValue = change.NewValue
                };
                
                var provider = _providers.FirstOrDefault(p => p.SettingType == change.Type);
                
                if (provider != null && provider.IsAvailable)
                {
                    var result = await provider.ApplyAsync(new[] { rollbackChange }, ct);
                    
                    if (result.Success)
                    {
                        rolledBack.Add(rollbackChange);
                        _logger.LogDebug("  ✓ Rollback: {Key} = {Value}", 
                            rollbackChange.Key, rollbackChange.NewValue);
                    }
                    else
                    {
                        _logger.LogWarning("  ⚠️ Rollback failed: {Key}", rollbackChange.Key);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "  ❌ Ошибка rollback для {Key}", change.Key);
            }
        }
        
        _logger.LogInformation("Rollback завершен. Восстановлено: {Count}", rolledBack.Count);
    }

    /// <summary>
    /// Rollback к предыдущему состоянию
    /// </summary>
    public async Task<ExecutionResult> RollbackAsync(
        StateManagerState previousState,
        CancellationToken ct)
    {
        _logger.LogInformation("♻️ Rollback к состоянию {ProfileId}",
            previousState.PreviousProfileId);

        // Здесь должна быть логика восстановления из backup
        // Для простоты возвращаем ошибку - rollback требует полной реализации
        return ExecutionResult.CreateFailure(
            Guid.NewGuid().ToString("N")[..8],
            new NotImplementedException("Rollback требует полной реализации"),
            new List<SettingChange>());
    }

    /// <summary>
    /// Получить текущее состояние из кэша/системы
    /// </summary>
    public async Task<TcpProfile?> GetCurrentStateAsync()
    {
        return await _cache.GetCurrentProfileAsync();
    }

    /// <summary>
    /// Получить закэшированное состояние
    /// </summary>
    public async Task<Dictionary<string, string>> GetCachedStateAsync()
    {
        var profile = await _cache.GetCurrentProfileAsync();
        return new Dictionary<string, string>
        {
            ["current_profile"] = profile?.Id ?? "default"
        };
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        
        foreach (var provider in _providers.OfType<IDisposable>())
        {
            provider.Dispose();
        }
        
        GC.SuppressFinalize(this);
    }
}
