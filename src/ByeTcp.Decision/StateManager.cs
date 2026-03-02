using System.Collections.Concurrent;
using Microsoft.Extensions.Logging;
using ByeTcp.Contracts;
using System.Collections.Frozen;

namespace ByeTcp.Decision;

/// <summary>
/// State Manager для управления состоянием профилей
/// 
/// Ответственность:
/// - Tracking активного/предыдущего профиля
/// - Windows Defaults baseline
/// - Rate limiting (debounce) для предотвращения thrashing
/// - История изменений
/// - Persistence состояния
/// </summary>
public sealed class StateManager : IStateManager, IDisposable
{
    private readonly ILogger<StateManager> _logger;
    private readonly IRateLimiter _rateLimiter;
    private readonly string _stateFilePath;
    private readonly object _lock = new();
    
    private StateManagerState _currentState = new();
    private readonly ConcurrentDictionary<string, TcpProfile> _profiles = new();
    private bool _disposed;

    // Конфигурация rate limiting
    private readonly TimeSpan _minChangeInterval = TimeSpan.FromSeconds(5);
    private readonly TimeSpan _rateLimitWindow = TimeSpan.FromMinutes(1);
    private readonly int _maxChangesPerWindow = 10;

    public StateManager(
        ILogger<StateManager> logger,
        IRateLimiter rateLimiter,
        string stateFilePath)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _rateLimiter = rateLimiter ?? throw new ArgumentNullException(nameof(rateLimiter));
        _stateFilePath = stateFilePath;
        
        // Инициализация профиля по умолчанию
        _profiles["default"] = new TcpProfile
        {
            Id = "default",
            Name = "Windows Default",
            Description = "Настройки по умолчанию"
        };
        _currentState = _currentState with { DefaultProfile = _profiles["default"] };
    }

    public TcpProfile? GetCurrentProfile()
    {
        lock (_lock)
        {
            return _currentState.CurrentProfileId != null
                ? _profiles.GetValueOrDefault(_currentState.CurrentProfileId)
                : null;
        }
    }

    public TcpProfile? GetPreviousProfile()
    {
        lock (_lock)
        {
            return _currentState.PreviousProfileId != null
                ? _profiles.GetValueOrDefault(_currentState.PreviousProfileId)
                : null;
        }
    }

    public TcpProfile GetDefaultProfile()
    {
        lock (_lock)
        {
            return _currentState.DefaultProfile ?? _profiles["default"];
        }
    }

    public IReadOnlyList<TcpProfile> GetAllProfiles()
    {
        lock (_lock)
        {
            return _profiles.Values.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Проверка возможности переключения профиля (rate limiting)
    /// </summary>
    public bool CanSwitchProfile(string newProfileId, string? currentProfileId)
    {
        lock (_lock)
        {
            // Check 1: Same profile (idempotency)
            if (_currentState.CurrentProfileId == newProfileId)
            {
                _logger.LogDebug("Профиль {ProfileId} уже применен, пропускаем", newProfileId);
                return false;
            }
            
            // Check 2: Min interval since last change (debounce)
            var timeSinceLastChange = DateTime.Now - _currentState.LastChangeTime;
            if (timeSinceLastChange < _minChangeInterval)
            {
                var remaining = _minChangeInterval - timeSinceLastChange;
                _logger.LogDebug(
                    "Rate limit: слишком частое переключение. Подождите {Remaining}", 
                    remaining);
                return false;
            }
            
            // Check 3: Max changes per window (sliding window rate limit)
            if (!_rateLimiter.AllowAction(
                "profile_changes", 
                _rateLimitWindow, 
                _maxChangesPerWindow))
            {
                var cooldown = _rateLimiter.GetRemainingCooldown("profile_changes");
                _logger.LogWarning(
                    "⚠️ Rate limit exceeded: {Max} изменений за {Window}. Cooldown: {Cooldown}",
                    _maxChangesPerWindow, _rateLimitWindow, cooldown);
                return false;
            }
            
            return true;
        }
    }

    /// <summary>
    /// Запись изменения профиля в историю
    /// </summary>
    public void RecordProfileChange(ProfileChangeRecord record)
    {
        lock (_lock)
        {
            var previousProfileId = _currentState.CurrentProfileId;
            
            _currentState = _currentState with
            {
                PreviousProfileId = previousProfileId,
                CurrentProfileId = record.ToProfileId,
                LastChangeTime = DateTime.Now,
                ConsecutiveChanges = _currentState.ConsecutiveChanges + 1,
                History = _currentState.History.Take(99).Prepend(record).ToList() // Keep last 100
            };
            
            _logger.LogInformation(
                "📝 Записано изменение: {From} → {To} ({Reason})",
                previousProfileId ?? "none",
                record.ToProfileId,
                record.Reason);
            
            // Асинхронное сохранение (fire and forget)
            _ = SaveStateAsync();
        }
    }

    /// <summary>
    /// Откат к предыдущему профилю
    /// </summary>
    public Task RollbackToPreviousAsync()
    {
        lock (_lock)
        {
            if (_currentState.PreviousProfileId == null)
            {
                _logger.LogWarning("Нет предыдущего профиля для отката");
                return Task.CompletedTask;
            }
            
            var previousId = _currentState.PreviousProfileId;
            
            _currentState = _currentState with
            {
                PreviousProfileId = _currentState.CurrentProfileId,
                CurrentProfileId = previousId
            };
            
            _logger.LogInformation("♻️ Откат к профилю {ProfileId}", previousId);
            
            return SaveStateAsync();
        }
    }

    /// <summary>
    /// Сброс к настройкам по умолчанию
    /// </summary>
    public Task ResetToDefaultsAsync()
    {
        lock (_lock)
        {
            var previousId = _currentState.CurrentProfileId;
            
            _currentState = _currentState with
            {
                PreviousProfileId = _currentState.CurrentProfileId,
                CurrentProfileId = "default",
                ConsecutiveChanges = 0
            };
            
            _logger.LogInformation("🔄 Сброс к профилю по умолчанию (был: {PreviousId})", previousId);
            
            return SaveStateAsync();
        }
    }

    public IReadOnlyList<ProfileChangeRecord> GetHistory(int count)
    {
        lock (_lock)
        {
            return _currentState.History.Take(count).ToList();
        }
    }

    public StateManagerState GetState()
    {
        lock (_lock)
        {
            return _currentState;
        }
    }

    /// <summary>
    /// Регистрация профиля
    /// </summary>
    public void RegisterProfile(TcpProfile profile)
    {
        _profiles[profile.Id] = profile;
        _logger.LogDebug("Зарегистрирован профиль: {ProfileId}", profile.Id);
    }

    /// <summary>
    /// Сохранение состояния в файл
    /// </summary>
    public async Task SaveStateAsync()
    {
        try
        {
            var stateToSave = GetState();
            var json = System.Text.Json.JsonSerializer.Serialize(stateToSave, new System.Text.Json.JsonSerializerOptions
            {
                WriteIndented = true
            });
            
            var directory = Path.GetDirectoryName(_stateFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            
            await File.WriteAllTextAsync(_stateFilePath, json);
            
            _logger.LogDebug("Состояние сохранено в {Path}", _stateFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось сохранить состояние");
        }
    }

    /// <summary>
    /// Загрузка состояния из файла
    /// </summary>
    public async Task LoadStateAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogDebug("Файл состояния не найден, используем значения по умолчанию");
                return;
            }
            
            var json = await File.ReadAllTextAsync(_stateFilePath);
            var loadedState = System.Text.Json.JsonSerializer.Deserialize<StateManagerState>(json);
            
            if (loadedState != null)
            {
                lock (_lock)
                {
                    _currentState = loadedState with
                    {
                        DefaultProfile = _currentState.DefaultProfile,
                        History = loadedState.History ?? new List<ProfileChangeRecord>()
                    };
                }
                
                _logger.LogInformation(
                    "📂 Загружено состояние: текущий={Current}, предыдущий={Previous}",
                    _currentState.CurrentProfileId,
                    _currentState.PreviousProfileId);
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Не удалось загрузить состояние, используем значения по умолчанию");
        }
    }

    public void Dispose()
    {
        if (_disposed) return;

        _disposed = true;
        
        // Блокирующий вызов запрещён - сохраняем состояние синхронно или игнорируем ошибку
        try
        {
            SaveStateAsync().GetAwaiter().GetResult();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to save state during disposal");
        }
        
        GC.SuppressFinalize(this);
    }
}

/// <summary>
/// Простая реализация Rate Limiter на основе sliding window
/// </summary>
public sealed class SlidingWindowRateLimiter : IRateLimiter
{
    private readonly ConcurrentDictionary<string, Queue<DateTime>> _windows = new();
    private readonly object _lock = new();

    public bool AllowAction(string key, TimeSpan window, int maxActions)
    {
        var now = DateTime.Now;
        var windowStart = now - window;
        
        lock (_lock)
        {
            if (!_windows.TryGetValue(key, out var timestamps))
            {
                timestamps = new Queue<DateTime>();
                _windows[key] = timestamps;
            }
            
            // Удаляем старые записи за пределами окна
            while (timestamps.Count > 0 && timestamps.Peek() < windowStart)
            {
                timestamps.Dequeue();
            }
            
            // Проверяем лимит
            if (timestamps.Count >= maxActions)
            {
                return false;
            }
            
            // Добавляем новую запись
            timestamps.Enqueue(now);
            return true;
        }
    }

    public TimeSpan GetRemainingCooldown(string key)
    {
        lock (_lock)
        {
            if (!_windows.TryGetValue(key, out var timestamps) || timestamps.Count == 0)
            {
                return TimeSpan.Zero;
            }
            
            var oldestInWindow = timestamps.Peek();
            var windowEnd = oldestInWindow + TimeSpan.FromMinutes(1); // Default window
            var remaining = windowEnd - DateTime.Now;
            
            return remaining > TimeSpan.Zero ? remaining : TimeSpan.Zero;
        }
    }

    public void Reset(string key)
    {
        lock (_lock)
        {
            _windows.TryRemove(key, out _);
        }
    }
}
