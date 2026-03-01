using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;

namespace ByeTcp.Execution;

/// <summary>
/// In-Memory кэш состояния с persistence
/// 
/// Назначение:
/// - Кэширование текущего примененного профиля
/// - Проверка idempotency (избежание redundant applies)
/// - Быстрый доступ к состоянию без чтения реестра
/// </summary>
public sealed class InMemoryStateCache : IStateCache, IDisposable
{
    private readonly ILogger<InMemoryStateCache> _logger;
    private readonly string _cacheFilePath;
    private readonly ConcurrentDictionary<string, string> _cachedState = new();
    private TcpProfile? _currentProfile;
    private DateTime _lastUpdateTime;
    private bool _disposed;

    public InMemoryStateCache(
        ILogger<InMemoryStateCache> logger,
        string cacheFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _cacheFilePath = cacheFilePath;
        
        LoadCache();
    }

    public async Task<TcpProfile?> GetCurrentProfileAsync()
    {
        return await Task.FromResult(_currentProfile);
    }

    public async Task UpdateStateAsync(TcpProfile profile, CancellationToken ct)
    {
        await Task.Run(() =>
        {
            _currentProfile = profile;
            _lastUpdateTime = DateTime.UtcNow;
            
            // Обновляем кэш ключ-значение
            _cachedState["current_profile_id"] = profile.Id;
            _cachedState["last_update_time"] = _lastUpdateTime.ToString("O");
            
            // Кэшируем значения настроек
            if (profile.TcpAckFrequency.HasValue)
                _cachedState["TcpAckFrequency"] = profile.TcpAckFrequency.Value.ToString();
            
            if (profile.TcpNoDelay.HasValue)
                _cachedState["TCPNoDelay"] = profile.TcpNoDelay.Value.ToString();
            
            if (profile.TcpDelAckTicks.HasValue)
                _cachedState["TcpDelAckTicks"] = profile.TcpDelAckTicks.Value.ToString();
            
            if (!string.IsNullOrEmpty(profile.ReceiveWindowAutoTuningLevel))
                _cachedState["autotuninglevel"] = profile.ReceiveWindowAutoTuningLevel;
            
            if (!string.IsNullOrEmpty(profile.CongestionProvider))
                _cachedState["congestionprovider"] = profile.CongestionProvider;
            
            _logger.LogDebug("Кэш обновлен: профиль={ProfileId}", profile.Id);
            
            // Асинхронное сохранение
            SaveCache();
        }, ct);
    }

    public async Task ClearAsync()
    {
        await Task.Run(() =>
        {
            _currentProfile = null;
            _cachedState.Clear();
            _lastUpdateTime = DateTime.UtcNow;
            
            _logger.LogDebug("Кэш очищен");
            
            SaveCache();
        });
    }

    /// <summary>
    /// Проверка, применен ли уже профиль (idempotency check)
    /// </summary>
    public bool IsProfileApplied(TcpProfile profile)
    {
        if (_currentProfile == null)
            return false;
        
        if (_currentProfile.Id != profile.Id)
            return false;
        
        // Сравниваем ключевые настройки
        if (profile.TcpAckFrequency.HasValue &&
            _currentProfile.TcpAckFrequency != profile.TcpAckFrequency)
            return false;
        
        if (profile.TcpNoDelay.HasValue &&
            _currentProfile.TcpNoDelay != profile.TcpNoDelay)
            return false;
        
        if (profile.TcpDelAckTicks.HasValue &&
            _currentProfile.TcpDelAckTicks != profile.TcpDelAckTicks)
            return false;
        
        if (!string.IsNullOrEmpty(profile.ReceiveWindowAutoTuningLevel) &&
            _currentProfile.ReceiveWindowAutoTuningLevel != profile.ReceiveWindowAutoTuningLevel)
            return false;
        
        if (!string.IsNullOrEmpty(profile.CongestionProvider) &&
            _currentProfile.CongestionProvider != profile.CongestionProvider)
            return false;
        
        _logger.LogDebug("Профиль {ProfileId} уже применен (idempotency)", profile.Id);
        return true;
    }

    public async Task<Dictionary<string, string>> GetCachedStateAsync()
    {
        return await Task.FromResult(_cachedState.ToDictionary(k => k.Key, v => v.Value));
    }

    private void LoadCache()
    {
        try
        {
            if (!File.Exists(_cacheFilePath))
            {
                _logger.LogDebug("Файл кэша не найден: {Path}", _cacheFilePath);
                return;
            }
            
            var json = File.ReadAllText(_cacheFilePath);
            var cacheData = System.Text.Json.JsonSerializer.Deserialize<Dictionary<string, string>>(json);
            
            if (cacheData != null)
            {
                foreach (var kvp in cacheData)
                {
                    _cachedState[kvp.Key] = kvp.Value;
                }
                
                if (_cachedState.TryGetValue("current_profile_id", out var profileId))
                {
                    _currentProfile = new TcpProfile { Id = profileId, Name = profileId };
                }
                
                if (_cachedState.TryGetValue("last_update_time", out var timeStr) &&
                    DateTime.TryParse(timeStr, out var time))
                {
                    _lastUpdateTime = time;
                }
                
                _logger.LogDebug("Кэш загружен из {Path}", _cacheFilePath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить кэш");
        }
    }

    private void SaveCache()
    {
        try
        {
            var directory = Path.GetDirectoryName(_cacheFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            var json = System.Text.Json.JsonSerializer.Serialize(_cachedState, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            File.WriteAllText(_cacheFilePath, json);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить кэш");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        SaveCache();
        GC.SuppressFinalize(this);
    }
}
