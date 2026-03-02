using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using ByeTcp.Core.Interfaces;
using ByeTcp.Core.Models;

namespace ByeTcp.Core.Engine;

/// <summary>
/// Движок правил для оценки состояния и выбора профиля
/// </summary>
public sealed class RuleEngine : IRuleEngine
{
    private readonly ILogger<RuleEngine> _logger;
    private readonly List<Rule> _rules = new();
    private readonly Dictionary<string, TcpProfile> _profiles = new();
    private readonly object _lock = new();
    
    private TcpProfile? _currentProfile;
    private TcpProfile? _defaultProfile;

    public event EventHandler<RuleEvaluationResult>? ProfileChanged;

    public RuleEngine(ILogger<RuleEngine> logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        
        // Создаем профиль по умолчанию
        _defaultProfile = new TcpProfile
        {
            Id = "default",
            Name = "Windows Default",
            Description = "Стандартные настройки Windows"
        };
        _profiles["default"] = _defaultProfile;
        _currentProfile = _defaultProfile;
    }

    /// <summary>
    /// Загрузить правила из JSON конфигурации
    /// </summary>
    public async Task LoadRulesAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("Файл правил не найден: {Path}. Используем правила по умолчанию.", configPath);
                LoadDefaultRules();
                return;
            }
            
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonConvert.DeserializeObject<RulesConfig>(json);
            
            if (config?.Rules != null)
            {
                lock (_lock)
                {
                    _rules.Clear();
                    _rules.AddRange(config.Rules);
                    
                    // Сортируем по приоритету (высший приоритет первый)
                    _rules.Sort((a, b) => b.Priority.CompareTo(a.Priority));
                }
                
                _logger.LogInformation("📋 Загружено {Count} правил из {Path}", _rules.Count, configPath);
            }
            else
            {
                _logger.LogWarning("Правила в конфигурации пусты. Используем правила по умолчанию.");
                LoadDefaultRules();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка загрузки правил из {Path}", configPath);
            LoadDefaultRules();
        }
    }

    /// <summary>
    /// Загрузить профили из JSON конфигурации
    /// </summary>
    public async Task LoadProfilesAsync(string configPath)
    {
        try
        {
            if (!File.Exists(configPath))
            {
                _logger.LogWarning("Файл профилей не найден: {Path}. Используем профили по умолчанию.", configPath);
                LoadDefaultProfiles();
                return;
            }
            
            var json = await File.ReadAllTextAsync(configPath);
            var config = JsonConvert.DeserializeObject<ProfilesConfig>(json);
            
            if (config?.Profiles != null)
            {
                lock (_lock)
                {
                    foreach (var profile in config.Profiles)
                    {
                        _profiles[profile.Id] = profile;
                    }
                }
                
                _logger.LogInformation("📦 Загружено {Count} профилей из {Path}", config.Profiles.Count, configPath);
            }
            else
            {
                _logger.LogWarning("Профили в конфигурации пусты. Используем профили по умолчанию.");
                LoadDefaultProfiles();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка загрузки профилей из {Path}", configPath);
            LoadDefaultProfiles();
        }
    }

    /// <summary>
    /// Оценить текущее состояние и выбрать профиль
    /// </summary>
    public RuleEvaluationResult Evaluate(IReadOnlyList<ProcessInfo> processes, NetworkMetrics metrics)
    {
        lock (_lock)
        {
            var runningProcessNames = processes
                .Where(p => p.State == ProcessState.Running)
                .Select(p => p.Name.ToLowerInvariant())
                .ToHashSet();
            
            _logger.LogDebug("🔍 Оценка правил. Активные процессы: {Processes}", 
                string.Join(", ", runningProcessNames));
            
            // Ищем первое подходящее правило (по приоритету)
            foreach (var rule in _rules)
            {
                if (MatchesRule(rule, runningProcessNames, metrics))
                {
                    var profile = GetProfile(rule.ProfileId);
                    if (profile != null)
                    {
                        var shouldSwitch = _currentProfile?.Id != profile.Id;
                        
                        var result = new RuleEvaluationResult
                        {
                            RuleId = rule.Id,
                            ProfileId = profile.Id,
                            Actions = GenerateActions(profile),
                            Priority = rule.Priority,
                            Reason = rule.Description ?? $"Rule {rule.Id} matched"
                        };
                        
                        if (shouldSwitch)
                        {
                            _logger.LogInformation(
                                "🔄 Переключение профиля: {From} → {To} (правило: {RuleId}, причина: {Reason})",
                                _currentProfile?.Name ?? "none",
                                profile.Name,
                                rule.Id,
                                result.Reason
                            );
                            
                            _currentProfile = profile;
                            ProfileChanged?.Invoke(this, result);
                        }
                        
                        return result;
                    }
                }
            }
            
            // Если ни одно правило не подошло, используем default
            if (_currentProfile?.Id != "default" && _defaultProfile != null)
            {
                _logger.LogInformation("🔄 Возврат к профилю по умолчанию (нет подходящих правил)");
                
                _currentProfile = _defaultProfile;
                
                var defaultResult = new RuleEvaluationResult
                {
                    RuleId = null,
                    ProfileId = "default",
                    Actions = GenerateActions(_defaultProfile),
                    Priority = 0,
                    Reason = "No rules matched"
                };
                
                ProfileChanged?.Invoke(this, defaultResult);
                return defaultResult;
            }
            
            return new RuleEvaluationResult
            {
                RuleId = null,
                ProfileId = _currentProfile?.Id,
                Actions = GenerateActions(_currentProfile ?? _defaultProfile!),
                Priority = 0,
                Reason = "No change"
            };
        }
    }

    /// <summary>
    /// Проверка соответствия правилу
    /// </summary>
    private static bool MatchesRule(Rule rule, HashSet<string> runningProcesses, NetworkMetrics metrics)
    {
        // Проверка условия процесса
        if (rule.Conditions?.Process != null)
        {
            var processCondition = rule.Conditions.Process;
            
            if (!string.IsNullOrEmpty(processCondition.Name))
            {
                var matches = runningProcesses.Contains(processCondition.Name.ToLowerInvariant());
                
                if (processCondition.State == ProcessState.Exited)
                {
                    // Правило срабатывает, если процесс НЕ запущен
                    if (matches) return false;
                }
                else
                {
                    // Правило срабатывает, если процесс запущен
                    if (!matches) return false;
                }
            }
        }
        
        // Проверка условий сети (опционально)
        if (rule.Conditions?.Network != null)
        {
            var networkCondition = rule.Conditions.Network;
            
            if (networkCondition.MinRttMs.HasValue && metrics.RttMs < networkCondition.MinRttMs)
                return false;
            
            if (networkCondition.MaxRttMs.HasValue && metrics.RttMs > networkCondition.MaxRttMs)
                return false;
            
            if (networkCondition.MaxPacketLossPercent.HasValue && 
                metrics.PacketLossPercent > networkCondition.MaxPacketLossPercent)
                return false;
        }
        
        return true;
    }

    /// <summary>
    /// Генерация действий из профиля
    /// </summary>
    private static List<ProfileAction> GenerateActions(TcpProfile profile)
    {
        var actions = new List<ProfileAction>();
        
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
        
        return actions;
    }

    /// <summary>
    /// Принудительно применить профиль
    /// </summary>
    public void ForceApplyProfile(string profileId, string reason)
    {
        lock (_lock)
        {
            var profile = GetProfile(profileId);
            if (profile == null)
            {
                _logger.LogWarning("Профиль {ProfileId} не найден для принудительного применения", profileId);
                return;
            }
            
            _logger.LogInformation("⚡ Принудительное применение профиля {ProfileName}: {Reason}", 
                profile.Name, reason);
            
            _currentProfile = profile;
            
            var result = new RuleEvaluationResult
            {
                RuleId = "manual",
                ProfileId = profileId,
                Actions = GenerateActions(profile),
                Priority = 999,
                Reason = reason
            };
            
            ProfileChanged?.Invoke(this, result);
        }
    }

    /// <summary>
    /// Получить профиль по ID
    /// </summary>
    private TcpProfile? GetProfile(string profileId)
    {
        lock (_lock)
        {
            return _profiles.GetValueOrDefault(profileId);
        }
    }

    /// <summary>
    /// Получить текущий активный профиль
    /// </summary>
    public TcpProfile? GetCurrentProfile()
    {
        lock (_lock)
        {
            return _currentProfile;
        }
    }

    /// <summary>
    /// Получить все загруженные профили
    /// </summary>
    public IReadOnlyList<TcpProfile> GetAllProfiles()
    {
        lock (_lock)
        {
            return _profiles.Values.ToList().AsReadOnly();
        }
    }

    /// <summary>
    /// Загрузить правила по умолчанию
    /// </summary>
    private void LoadDefaultRules()
    {
        _rules.Clear();
        _rules.AddRange(new[]
        {
            new Rule
            {
                Id = "gaming_cs2",
                Priority = 100,
                Description = "Оптимизация для Counter-Strike 2",
                Conditions = new RuleConditions
                {
                    Process = new ProcessCondition
                    {
                        Name = "cs2.exe",
                        State = ProcessState.Running
                    }
                },
                ProfileId = "gaming_low_latency"
            },
            new Rule
            {
                Id = "torrent_qbittorrent",
                Priority = 90,
                Description = "Оптимизация для qBittorrent",
                Conditions = new RuleConditions
                {
                    Process = new ProcessCondition
                    {
                        Name = "qbittorrent.exe",
                        State = ProcessState.Running
                    }
                },
                ProfileId = "torrent_high_throughput"
            }
        });
        
        _logger.LogInformation("Загружены {Count} правила по умолчанию", _rules.Count);
    }

    /// <summary>
    /// Загрузить профили по умолчанию
    /// </summary>
    private void LoadDefaultProfiles()
    {
        _profiles.Clear();
        
        _profiles["default"] = new TcpProfile
        {
            Id = "default",
            Name = "Windows Default",
            Description = "Стандартные настройки Windows"
        };
        
        _profiles["gaming_low_latency"] = new TcpProfile
        {
            Id = "gaming_low_latency",
            Name = "Gaming (Low Latency)",
            Description = "Оптимизация для онлайн-игр с минимальной задержкой",
            TcpAckFrequency = 1,
            TcpNoDelay = 1,
            TcpDelAckTicks = 0,
            ReceiveWindowAutoTuningLevel = "normal",
            CongestionProvider = "ctcp",
            EcnCapability = "disabled"
        };
        
        _profiles["torrent_high_throughput"] = new TcpProfile
        {
            Id = "torrent_high_throughput",
            Name = "Torrent (High Throughput)",
            Description = "Оптимизация для торрентов с максимальной пропускной способностью",
            TcpAckFrequency = 2,
            TcpNoDelay = 0,
            TcpDelAckTicks = 2,
            ReceiveWindowAutoTuningLevel = "experimental",
            CongestionProvider = "cubic",
            EcnCapability = "enabled"
        };
        
        _defaultProfile = _profiles["default"];
        _currentProfile = _defaultProfile;
        
        _logger.LogInformation("Загружено {Count} профилей по умолчанию", _profiles.Count);
    }
}

/// <summary>
/// Конфигурация правил
/// </summary>
public record RulesConfig
{
    [JsonProperty("rules")]
    public List<Rule> Rules { get; init; } = new();
}

/// <summary>
/// Конфигурация профилей
/// </summary>
public record ProfilesConfig
{
    [JsonProperty("profiles")]
    public List<TcpProfile> Profiles { get; init; } = new();
}

/// <summary>
/// Правило переключения профиля
/// </summary>
public record Rule
{
    [JsonProperty("id")]
    public string Id { get; init; } = string.Empty;
    
    [JsonProperty("priority")]
    public int Priority { get; init; }
    
    [JsonProperty("description")]
    public string? Description { get; init; }
    
    [JsonProperty("conditions")]
    public RuleConditions? Conditions { get; init; }
    
    [JsonProperty("profile")]
    public string ProfileId { get; init; } = string.Empty;
}

/// <summary>
/// Условия правила
/// </summary>
public record RuleConditions
{
    [JsonProperty("process")]
    public ProcessCondition? Process { get; init; }
    
    [JsonProperty("network")]
    public NetworkCondition? Network { get; init; }
}

/// <summary>
/// Условие процесса
/// </summary>
public record ProcessCondition
{
    [JsonProperty("name")]
    public string Name { get; init; } = string.Empty;
    
    [JsonProperty("state")]
    public ProcessState State { get; init; } = ProcessState.Running;
}

/// <summary>
/// Условие сети
/// </summary>
public record NetworkCondition
{
    [JsonProperty("minRttMs")]
    public double? MinRttMs { get; init; }
    
    [JsonProperty("maxRttMs")]
    public double? MaxRttMs { get; init; }
    
    [JsonProperty("maxPacketLossPercent")]
    public double? MaxPacketLossPercent { get; init; }
}
