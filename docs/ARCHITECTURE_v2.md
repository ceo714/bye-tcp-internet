# 📐 Bye-TCP Internet v2.0 — Архитектура после Рефакторинга

## 1. Критика Архитектуры v1.0

### 1.1 Выявленные Проблемы

| Проблема |Severity | Описание |
|----------|---------|----------|
| **Нарушение SRP** | 🔴 High | `ByeTcpWorkerService` содержит бизнес-логику, оркестрацию и управление состоянием |
| **Синхронные вызовы NetSh** | 🔴 High | Блокирующие вызовы процессов могут приводить к deadlock |
| **WMI Polling** | 🟡 Medium | WMI event subscription с polling interval 1 сек создает нагрузку |
| **Отсутствие кэширования** | 🟡 Medium | Повторное применение одинаковых настроек |
| **Нет транзакционности** | 🔴 High | Частичное применение настроек при ошибке оставляет систему в неконсистентном состоянии |
| **Связанность модулей** | 🟡 Medium | Прямые зависимости между мониторами и rule engine |
| **Нет rate limiting** | 🟡 Medium | Thrashing при частом запуске/завершении процессов |
| **Отсутствие state management** | 🟡 Medium | Нет tracking текущего/предыдущего состояния |
| **Слабая обработка ошибок** | 🟡 Medium | Исключения могут привести к остановке службы |

---

## 2. Архитектура v2.0 — Разделение Ответственности

### 2.1 Слои Архитектуры

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         ORCHESTRATION LAYER                              │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Windows Service Host + Watchdog + Health Monitoring              │  │
│  │  • Lifecycle management (Start/Stop/Pause)                        │  │
│  │  • Graceful shutdown с timeout                                    │  │
│  │  • Unhandled exception handling                                   │  │
│  │  • Health checks и self-recovery                                  │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                          MONITORING LAYER                                │
│  ┌─────────────────────┐  ┌─────────────────────┐  ┌─────────────────┐  │
│  │ ETW Process Monitor │  │ Adaptive Network    │  │ Diagnostics     │  │
│  │ (Kernel ETW)        │  │ Monitor (ETW+ICMP)  │  │ Engine          │  │
│  │ • Zero-overhead     │  │ • Adaptive polling  │  │ • On-demand     │  │
│  │ • Event-driven      │  │ • Event-driven      │  │ • Traceroute    │  │
│  │ • No polling        │  │ • RTT/Jitter/Loss   │  │ • DNS check     │  │
│  └─────────────────────┘  └─────────────────────┘  └─────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                          DECISION LAYER                                  │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Rule Engine (Pure Functions, Stateless)                          │  │
│  │  • Deterministic evaluation                                       │  │
│  │  • No side effects                                                │  │
│  │  • Idempotent results                                             │  │
│  │  • Priority-based conflict resolution                             │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  State Manager (Stateful)                                         │  │
│  │  • Active Profile tracking                                        │  │
│  │  • Previous Profile tracking                                      │  │
│  │  • Windows Defaults baseline                                      │  │
│  │  • Rate limiting (debounce)                                       │  │
│  │  • Profile change history                                         │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         EXECUTION LAYER                                  │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Settings Executor (Transactional)                                │  │
│  │  • Atomic profile application                                     │  │
│  │  • Rollback on partial failure                                    │  │
│  │  • State caching (prevent redundant applies)                      │  │
│  │  • Direct WinAPI calls (avoid NetSh where possible)               │  │
│  └───────────────────────────────────────────────────────────────────┘  │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────┐  │
│  │ Registry Provider│ │ NetSh Provider  │ │ WFP Provider (Optional) │  │
│  │ (Direct API)     │ │ (Process+SDK)   │ │ (Fallback to user-mode) │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                         INFRASTRUCTURE LAYER                             │
│  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────────┐  │
│  │ Config Manager  │  │ Logging         │  │ Security                │  │
│  │ (Versioned)     │  │ (Serilog)       │  │ (UAC, ACL)              │  │
│  └─────────────────┘  └─────────────────┘  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
```

---

## 3. Детальное Описание Слоев

### 3.1 Orchestration Layer

**Ответственность:**
- Управление жизненным циклом службы Windows
- Graceful shutdown с корректной очисткой ресурсов
- Health monitoring и self-recovery
- Обработка unhandled exceptions
- Coordination между слоями

**Компоненты:**

```csharp
interface IOrchestrator
{
    Task InitializeAsync(CancellationToken ct);
    Task RunAsync(CancellationToken ct);
    Task ShutdownAsync(CancellationToken ct);
    HealthReport GetHealthReport();
    Task RecoverFromFailureAsync(Exception ex);
}

interface IWatchdog
{
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    event EventHandler<HealthStatus>? HealthStatusChanged;
}

record HealthReport
{
    public bool IsHealthy { get; init; }
    public TimeSpan Uptime { get; init; }
    public int ActiveProfileChanges { get; init; }
    public int FailedOperations { get; init; }
    public DateTime? LastSuccessfulOperation { get; init; }
    public List<HealthIssue> Issues { get; init; }
}
```

**Graceful Shutdown Sequence:**

```
1. Receive Stop/Shutdown signal
        │
        ▼
2. Stop accepting new events (pause monitors)
        │
        ▼
3. Wait for pending operations (timeout: 30s)
        │
        ▼
4. Flush logs and state
        │
        ▼
5. Dispose resources in reverse order
        │
        ▼
6. Report shutdown complete
```

---

### 3.2 Monitoring Layer

#### 3.2.1 ETW Process Monitor (вместо WMI)

**Проблема WMI:**
- Polling interval создает задержки
- overhead ~1-5% CPU при частых запросах
- ManagementException при сбоях WMI службы

**Решение — ETW:**

```csharp
// Kernel ETW Process Provider
// Источник: Microsoft-Windows-Kernel-Process
// Event ID: 1 (ProcessStart), 2 (ProcessEnd)

interface IProcessMonitor
{
    event EventHandler<ProcessEvent>? ProcessEventReceived;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    IReadOnlySet<string> GetMonitoredProcesses();
    void AddProcessFilter(string processName);
    void RemoveProcessFilter(string processName);
}

record ProcessEvent
{
    public ProcessEventType Type { get; init; } // Start/End
    public string ProcessName { get; init; }
    public int ProcessId { get; init; }
    public string? FullPath { get; init; }
    public DateTime Timestamp { get; init; }
    public int ParentProcessId { get; init; }
}
```

**Реализация через TraceEvent:**

```csharp
using Microsoft.Diagnostics.Tracing;
using Microsoft.Diagnostics.Tracing.Parsers;
using Microsoft.Diagnostics.Tracing.Parsers.Kernel;

public sealed class EtwProcessMonitor : IProcessMonitor
{
    private readonly TraceEventSession _session;
    private readonly HashSet<string> _filters = new(StringComparer.OrdinalIgnoreCase);
    
    public EtwProcessMonitor()
    {
        // ETW сессия с kernel provider
        _session = new TraceEventSession("ByeTcpProcessMonitor");
        
        // Подписка на Process Start/Stop события
        _session.Source.Kernel.ProcessStart += OnProcessStart;
        _session.Source.Kernel.ProcessStop += OnProcessStop;
    }
    
    private void OnProcessStart(ProcessStartTraceData data)
    {
        if (_filters.Contains(data.ProcessName))
        {
            ProcessEventReceived?.Invoke(this, new ProcessEvent
            {
                Type = ProcessEventType.Start,
                ProcessName = data.ProcessName,
                ProcessId = data.ProcessID,
                FullPath = data.FileName,
                Timestamp = data.TimeStamp,
                ParentProcessId = data.ParentProcessID
            });
        }
    }
    
    // Преимущества:
    // • Zero-overhead (kernel-level events)
    // • Мгновенное уведомление (no polling)
    // • Надежность (kernel source)
}
```

#### 3.2.2 Adaptive Network Monitor

**Проблема:** Частый ICMP polling (каждые 5 сек) создает нагрузку.

**Решение:** Адаптивный интервал на основе активности профиля.

```csharp
interface INetworkMonitor
{
    event EventHandler<NetworkMetrics>? MetricsUpdated;
    Task StartAsync(CancellationToken ct);
    Task StopAsync();
    void SetAdaptiveMode(AdaptiveMode mode);
    NetworkMetrics GetCurrentMetrics();
}

enum AdaptiveMode
{
    Idle,           // 60 сек интервал (фоновый режим)
    Normal,         // 30 сек интервал (стандарт)
    Active,         // 10 сек интервал (изменение профиля)
    Critical        // 5 сек интервал (проблемы с сетью)
}

public sealed class AdaptiveNetworkMonitor : INetworkMonitor
{
    private readonly AdaptiveMode _currentMode;
    private readonly TimeSpan[] _intervals = new[]
    {
        TimeSpan.FromSeconds(60), // Idle
        TimeSpan.FromSeconds(30), // Normal
        TimeSpan.FromSeconds(10), // Active
        TimeSpan.FromSeconds(5)   // Critical
    };
    
    // Автоматическое переключение режима на основе:
    // • Частоты смены профилей
    // • Packet loss > threshold
    // • RTT spike detection
}
```

---

### 3.3 Decision Layer

#### 3.3.1 Rule Engine (Pure Functions)

**Требования:**
- Детерминированность (одинаковый input → одинаковый output)
- Отсутствие побочных эффектов (no I/O, no state mutation)
- Идемпотентность (повторный вызов с теми же данными = тот же результат)

```csharp
interface IRuleEngine
{
    // Чистая функция: входные данные → результат
    RuleEvaluationResult Evaluate(
        EvaluationContext context,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, TcpProfile> profiles
    );
    
    // Валидация правил (статическая)
    ValidationResult ValidateRules(IReadOnlyList<Rule> rules);
}

// Immutable контекст оценки
record EvaluationContext
{
    public IReadOnlySet<ProcessInfo> RunningProcesses { get; init; }
    public NetworkMetrics NetworkMetrics { get; init; }
    public DateTime EvaluationTime { get; init; }
}

// Immutable результат
record RuleEvaluationResult
{
    public string? SelectedProfileId { get; init; }
    public string? MatchingRuleId { get; init; }
    public int Priority { get; init; }
    public string Reason { get; init; }
    public bool ShouldSwitch { get; init; }
}

// Реализация
public sealed class PureRuleEngine : IRuleEngine
{
    public RuleEvaluationResult Evaluate(
        EvaluationContext context,
        IReadOnlyList<Rule> rules,
        IReadOnlyDictionary<string, TcpProfile> profiles)
    {
        // Сортировка по приоритету (descending)
        var sortedRules = rules.OrderByDescending(r => r.Priority);
        
        // Поиск первого подходящего правила
        foreach (var rule in sortedRules)
        {
            if (Matches(rule, context))
            {
                return new RuleEvaluationResult
                {
                    SelectedProfileId = rule.ProfileId,
                    MatchingRuleId = rule.Id,
                    Priority = rule.Priority,
                    Reason = rule.Description ?? rule.Id,
                    ShouldSwitch = true
                };
            }
        }
        
        // Default profile если ничего не найдено
        return new RuleEvaluationResult
        {
            SelectedProfileId = "default",
            MatchingRuleId = null,
            Priority = 0,
            Reason = "No matching rules",
            ShouldSwitch = false
        };
    }
    
    private static bool Matches(Rule rule, EvaluationContext context)
    {
        // Чистая логика сопоставления без побочных эффектов
        // ...
    }
}
```

#### 3.3.2 State Manager

**Ответственность:**
- Tracking активного профиля
- Tracking предыдущего профиля (для rollback)
- Windows Defaults baseline
- Rate limiting (debounce)
- История изменений

```csharp
interface IStateManager
{
    // State access
    TcpProfile? GetCurrentProfile();
    TcpProfile? GetPreviousProfile();
    TcpProfile GetDefaultProfile();
    
    // State transitions
    bool CanSwitchProfile(string newProfileId); // Rate limit check
    void RecordProfileChange(string profileId, string reason);
    Task RollbackToPreviousAsync();
    Task ResetToDefaultsAsync();
    
    // History
    IReadOnlyList<ProfileChangeRecord> GetHistory(int count);
    
    // State persistence
    Task SaveStateAsync();
    Task LoadStateAsync();
}

record StateManagerState
{
    public string? CurrentProfileId { get; init; }
    public string? PreviousProfileId { get; init; }
    public DateTime LastChangeTime { get; init; }
    public int ConsecutiveChanges { get; init; }
    public List<ProfileChangeRecord> History { get; init; }
}

record ProfileChangeRecord
{
    public DateTime Timestamp { get; init; }
    public string? FromProfileId { get; init; }
    public string ToProfileId { get; init; }
    public string Reason { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
}

// Rate Limiting реализация
public sealed class StateManager : IStateManager
{
    private readonly TimeSpan _minChangeInterval = TimeSpan.FromSeconds(5);
    private readonly int _maxChangesPerMinute = 10;
    
    public bool CanSwitchProfile(string newProfileId)
    {
        var state = _currentState;
        
        // Check 1: Min interval since last change
        if (DateTime.Now - state.LastChangeTime < _minChangeInterval)
        {
            return false; // Debounce
        }
        
        // Check 2: Max changes per minute (sliding window)
        var recentChanges = state.History
            .Where(r => r.Timestamp > DateTime.Now.AddMinutes(-1))
            .Count();
        
        if (recentChanges >= _maxChangesPerMinute)
        {
            return false; // Rate limit exceeded
        }
        
        // Check 3: Same profile (idempotency)
        if (state.CurrentProfileId == newProfileId)
        {
            return false; // Already applied
        }
        
        return true;
    }
}
```

---

### 3.4 Execution Layer

#### 3.4.1 Settings Executor (Transactional)

**Требования:**
- Атомарность применения профиля (all-or-nothing)
- Rollback при частичной ошибке
- Кэширование состояния (избегать redundant applies)

```csharp
interface ISettingsExecutor
{
    // Transactional apply
    Task<ExecutionResult> ApplyProfileAsync(
        TcpProfile profile,
        CancellationToken ct
    );
    
    // Rollback
    Task<ExecutionResult> RollbackAsync(
        RollbackTarget target,
        CancellationToken ct
    );
    
    // State query
    Task<TcpProfileState> GetCurrentStateAsync();
    Task<Dictionary<string, string>> GetCachedStateAsync();
}

record ExecutionResult
{
    public bool Success { get; init; }
    public List<SettingChange> AppliedChanges { get; init; }
    public List<SettingChange> RolledBackChanges { get; init; }
    public string? ErrorMessage { get; init; }
    public Exception? Exception { get; init; }
}

record SettingChange
{
    public SettingType Type { get; init; }
    public string Key { get; init; }
    public string? PreviousValue { get; init; }
    public string NewValue { get; init; }
}

// Transactional реализация
public sealed class TransactionalSettingsExecutor : ISettingsExecutor
{
    private readonly ISettingsProvider[] _providers;
    private readonly IStateCache _cache;
    
    public async Task<ExecutionResult> ApplyProfileAsync(
        TcpProfile profile,
        CancellationToken ct)
    {
        var currentState = await _cache.GetStateAsync(ct);
        
        // Check idempotency: skip if already applied
        if (IsAlreadyApplied(currentState, profile))
        {
            _logger.LogDebug("Profile already applied, skipping");
            return ExecutionResult.Success();
        }
        
        // Generate changes
        var changes = GenerateChanges(currentState, profile);
        
        // Prepare rollback stack
        var rollbackStack = new Stack<SettingChange>();
        
        try
        {
            // Apply each provider transactionally
            foreach (var provider in _providers)
            {
                var providerChanges = changes.Where(c => 
                    c.Type == provider.SettingType);
                
                var result = await provider.ApplyAsync(
                    providerChanges, 
                    ct);
                
                if (!result.Success)
                {
                    throw new SettingsApplyException(
                        $"Provider {provider.Name} failed: {result.Error}");
                }
                
                // Add to rollback stack
                foreach (var change in result.AppliedChanges)
                {
                    rollbackStack.Push(change);
                }
            }
            
            // Update cache
            await _cache.UpdateStateAsync(profile, ct);
            
            return ExecutionResult.Success(changes.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Profile apply failed, rolling back");
            
            // Rollback in reverse order
            await RollbackChangesAsync(rollbackStack, ct);
            
            return ExecutionResult.Failure(ex, rollbackStack.ToList());
        }
    }
    
    private async Task RollbackChangesAsync(
        Stack<SettingChange> rollbackStack, 
        CancellationToken ct)
    {
        foreach (var change in rollbackStack)
        {
            try
            {
                // Restore previous value
                await ApplyChangeAsync(change with 
                { 
                    NewValue = change.PreviousValue 
                }, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Rollback failed for {Key}", change.Key);
            }
        }
    }
}
```

#### 3.4.2 Settings Providers

**Иерархия провайдеров:**

```csharp
interface ISettingsProvider
{
    string Name { get; }
    SettingType SettingType { get; }
    Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes, 
        CancellationToken ct);
}

// Registry Provider (Direct WinAPI)
public sealed class RegistrySettingsProvider : ISettingsProvider
{
    public string Name => "Registry";
    public SettingType SettingType => SettingType.Registry;
    
    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes, 
        CancellationToken ct)
    {
        var appliedChanges = new List<SettingChange>();
        
        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();
            
            // Direct registry API (no reg.exe process)
            var previousValue = await ReadRegistryValueAsync(change.Key, ct);
            
            await WriteRegistryValueAsync(
                change.Key, 
                change.NewValue, 
                ct);
            
            appliedChanges.Add(change with 
            { 
                PreviousValue = previousValue 
            });
        }
        
        return ProviderResult.Success(appliedChanges);
    }
}

// NetSh Provider (Process + SDK)
public sealed class NetShSettingsProvider : ISettingsProvider
{
    public string Name => "NetSh";
    public SettingType SettingType => SettingType.NetSh;
    
    // Используем PowerShell SDK вместо процесса
    private readonly PowerShell _powerShell;
    
    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes, 
        CancellationToken ct)
    {
        foreach (var change in changes)
        {
            ct.ThrowIfCancellationRequested();
            
            // PowerShell SDK (не процесс!)
            var command = $"Set-NetTcpSetting -{change.Key} {change.NewValue}";
            _powerShell.AddScript(command);
            
            var result = await _powerShell.InvokeAsync(ct);
            
            if (_powerShell.Streams.Error.Count > 0)
            {
                return ProviderResult.Failure(
                    _powerShell.Streams.Error[0].ToString());
            }
        }
        
        return ProviderResult.Success();
    }
}

// WFP Provider (Optional, with fallback)
public sealed class WfpSettingsProvider : ISettingsProvider
{
    public string Name => "WFP";
    public SettingType SettingType => SettingType.Wfp;
    
    private readonly IWfpDriver? _driver;
    private readonly bool _isAvailable;
    
    public WfpSettingsProvider()
    {
        // Probe driver availability
        _driver = TryLoadWfpDriver();
        _isAvailable = _driver != null;
    }
    
    public async Task<ProviderResult> ApplyAsync(
        IEnumerable<SettingChange> changes, 
        CancellationToken ct)
    {
        if (!_isAvailable)
        {
            // Fallback: log warning, skip WFP settings
            _logger.LogWarning("WFP driver not available, skipping");
            return ProviderResult.Success(); // Non-fatal
        }
        
        // Apply via kernel driver
        return await _driver.ApplyAsync(changes, ct);
    }
}
```

---

### 3.5 Infrastructure Layer

#### 3.5.1 Versioned Config Manager

**Проблема v1.0:** Нет версионирования схемы, нет валидации.

**Решение v2.0:**

```json
// profiles.json с версионированием
{
  "$schema": "https://bye-tcp.internet/schemas/v2/profiles.schema.json",
  "version": "2.0",
  "schemaVersion": "2026-03",
  "profiles": [
    {
      "id": "gaming_low_latency",
      "version": "1.0",
      "name": "Gaming (Low Latency)",
      "settings": { ... }
    }
  ]
}
```

```csharp
interface IConfigManager
{
    Task<ConfigResult<T>> LoadConfigAsync<T>(
        string path, 
        JsonSchema schema,
        CancellationToken ct) where T : class;
    
    ValidationResult Validate<T>(T config, JsonSchema schema);
    T MergeConfigs<T>(T defaultConfig, T userConfig);
}

public sealed class VersionedConfigManager : IConfigManager
{
    public async Task<ConfigResult<T>> LoadConfigAsync<T>(
        string path, 
        JsonSchema schema,
        CancellationToken ct)
    {
        // 1. Load raw JSON
        var json = await File.ReadAllTextAsync(path, ct);
        
        // 2. Parse with version detection
        var doc = JsonDocument.Parse(json);
        var version = doc.RootElement.GetProperty("version").GetString();
        
        // 3. Version compatibility check
        if (!IsVersionCompatible(version))
        {
            return ConfigResult<T>.Error(
                $"Incompatible version: {version}");
        }
        
        // 4. Schema validation
        var validationResult = ValidateJson(json, schema);
        if (!validationResult.Valid)
        {
            return ConfigResult<T>.Error(
                validationResult.Errors);
        }
        
        // 5. Deserialize
        var config = JsonSerializer.Deserialize<T>(json);
        
        return ConfigResult<T>.Success(config);
    }
}
```

#### 3.5.2 JSON Schema для валидации

```json
// schemas/profiles.schema.json
{
  "$schema": "http://json-schema.org/draft-07/schema#",
  "$id": "https://bye-tcp.internet/schemas/v2/profiles.schema.json",
  "type": "object",
  "required": ["version", "schemaVersion", "profiles"],
  "properties": {
    "version": {
      "type": "string",
      "pattern": "^\\d+\\.\\d+$"
    },
    "schemaVersion": {
      "type": "string",
      "pattern": "^\\d{4}-\\d{2}$"
    },
    "profiles": {
      "type": "array",
      "items": {
        "type": "object",
        "required": ["id", "name"],
        "properties": {
          "id": {
            "type": "string",
            "pattern": "^[a-z][a-z0-9_]*$"
          },
          "name": { "type": "string", "minLength": 1 },
          "tcpAckFrequency": { "type": "integer", "minimum": 1, "maximum": 8 },
          "tcpNoDelay": { "type": "integer", "enum": [0, 1] }
        }
      }
    }
  }
}
```

---

## 4. Контракты Между Слоями

### 4.1 Event Contracts

```csharp
// Monitoring → Decision
record MonitoringEvent
{
    public MonitoringEventType Type { get; init; }
    public DateTime Timestamp { get; init; }
    public ProcessEvent? ProcessEvent { get; init; }
    public NetworkMetrics? NetworkMetrics { get; init; }
}

// Decision → Execution
record ProfileChangeCommand
{
    public string ProfileId { get; init; }
    public string Reason { get; init; }
    public string CorrelationId { get; init; }
    public CancellationToken CancellationToken { get; init; }
}

// Execution → Decision
record ProfileChangeResult
{
    public string CorrelationId { get; init; }
    public bool Success { get; init; }
    public string? ErrorMessage { get; init; }
    public TimeSpan Duration { get; init; }
}
```

### 4.2 Dependency Injection

```csharp
public static class DependencyInjection
{
    public static IServiceCollection AddByeTcpCore(
        this IServiceCollection services)
    {
        // Orchestration Layer
        services.AddSingleton<IOrchestrator, Orchestrator>();
        services.AddSingleton<IWatchdog, Watchdog>();
        
        // Monitoring Layer
        services.AddSingleton<IProcessMonitor, EtwProcessMonitor>();
        services.AddSingleton<INetworkMonitor, AdaptiveNetworkMonitor>();
        services.AddSingleton<IDiagnosticsEngine, DiagnosticsEngine>();
        
        // Decision Layer
        services.AddSingleton<IRuleEngine, PureRuleEngine>();
        services.AddSingleton<IStateManager, StateManager>();
        
        // Execution Layer
        services.AddSingleton<ISettingsExecutor, 
            TransactionalSettingsExecutor>();
        services.AddSingleton<ISettingsProvider, 
            RegistrySettingsProvider>();
        services.AddSingleton<ISettingsProvider, 
            NetShSettingsProvider>();
        services.AddSingleton<ISettingsProvider, 
            WfpSettingsProvider>();
        
        // Infrastructure Layer
        services.AddSingleton<IConfigManager, VersionedConfigManager>();
        services.AddSingleton<IStateCache, InMemoryStateCache>();
        
        return services;
    }
}
```

---

## 5. Обработка Ошибок и Watchdog

### 5.1 Global Exception Handler

```csharp
public sealed class GlobalExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;
    private readonly IWatchdog _watchdog;
    
    public void HandleUnhandledException(
        object sender, 
        UnhandledExceptionEventArgs e)
    {
        var ex = e.ExceptionObject as Exception;
        
        _logger.LogCritical(ex, 
            "Unhandled exception in {Sender}", sender);
        
        // Attempt recovery
        _ = _watchdog.RecoverAsync(ex);
    }
}

// Task scheduler exception
TaskScheduler.UnobservedTaskException += (s, e) =>
{
    _logger.LogError(e.Exception, 
        "Unobserved task exception");
    e.SetObserved(); // Prevent crash
};
```

### 5.2 Watchdog Implementation

```csharp
public sealed class Watchdog : IWatchdog
{
    private readonly IHealthCheck[] _healthChecks;
    private readonly TimeSpan _checkInterval;
    
    public event EventHandler<HealthStatus>? HealthStatusChanged;
    
    public async Task StartAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                await Task.Delay(_checkInterval, ct);
                
                var healthReport = await CheckHealthAsync(ct);
                
                if (!healthReport.IsHealthy)
                {
                    HealthStatusChanged?.Invoke(this, 
                        HealthStatus.Unhealthy);
                    
                    await AttemptRecoveryAsync(healthReport, ct);
                }
            }
            catch (OperationCanceledException)
            {
                break;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Watchdog check failed");
            }
        }
    }
    
    private async Task AttemptRecoveryAsync(
        HealthReport report, 
        CancellationToken ct)
    {
        foreach (var issue in report.Issues)
        {
            try
            {
                await RecoverFromIssueAsync(issue, ct);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, 
                    "Recovery from {Issue} failed", issue.Type);
            }
        }
    }
}
```

---

## 6. Безопасность

### 6.1 UAC и Administrator Check

```csharp
public static class SecurityHelper
{
    public static bool IsRunningAsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        
        return principal.IsInRole(
            WindowsBuiltInRole.Administrator);
    }
    
    public static void RequireAdministrator()
    {
        if (!IsRunningAsAdministrator())
        {
            throw new SecurityException(
                "Administrator privileges required");
        }
    }
}
```

### 6.2 Config File Protection

```csharp
public sealed class ConfigFileSecurity
{
    public static void SecureConfigFile(string path)
    {
        var fileInfo = new FileInfo(path);
        var security = fileInfo.GetAccessControl();
        
        // Remove inherited permissions
        security.SetAccessRuleProtection(
            true, // Disable inheritance
            false // Don't preserve inherited rules
        );
        
        // Grant access to Administrators and SYSTEM only
        var adminRule = new FileSystemAccessRule(
            "Administrators",
            FileSystemRights.Read | FileSystemRights.Write,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow
        );
        
        var systemRule = new FileSystemAccessRule(
            "SYSTEM",
            FileSystemRights.Read,
            InheritanceFlags.None,
            PropagationFlags.None,
            AccessControlType.Allow
        );
        
        security.AddAccessRule(adminRule);
        security.AddAccessRule(systemRule);
        
        fileInfo.SetAccessControl(security);
    }
}
```

---

## 7. Тестируемость

### 7.1 Unit Tests для Rule Engine

```csharp
[TestClass]
public class PureRuleEngineTests
{
    [TestMethod]
    public void Evaluate_NoMatchingRules_ReturnsDefault()
    {
        // Arrange
        var engine = new PureRuleEngine();
        var context = new EvaluationContext
        {
            RunningProcesses = new HashSet<ProcessInfo>(),
            NetworkMetrics = new NetworkMetrics()
        };
        var rules = new List<Rule>();
        var profiles = new Dictionary<string, TcpProfile>
        {
            ["default"] = new TcpProfile { Id = "default" }
        };
        
        // Act
        var result = engine.Evaluate(context, rules, profiles);
        
        // Assert
        Assert.AreEqual("default", result.SelectedProfileId);
        Assert.IsFalse(result.ShouldSwitch);
    }
    
    [TestMethod]
    public void Evaluate_MatchingRule_ReturnsCorrectProfile()
    {
        // Arrange
        var engine = new PureRuleEngine();
        var context = new EvaluationContext
        {
            RunningProcesses = new HashSet<ProcessInfo>
            {
                new ProcessInfo { Name = "cs2.exe" }
            }
        };
        var rules = new List<Rule>
        {
            new Rule
            {
                Id = "gaming_cs2",
                Priority = 100,
                Conditions = new RuleConditions
                {
                    Process = new ProcessCondition
                    {
                        Name = "cs2.exe",
                        State = ProcessState.Running
                    }
                },
                ProfileId = "gaming_low_latency"
            }
        };
        
        // Act
        var result = engine.Evaluate(context, rules, new());
        
        // Assert
        Assert.AreEqual("gaming_low_latency", result.SelectedProfileId);
        Assert.AreEqual("gaming_cs2", result.MatchingRuleId);
    }
}
```

### 7.2 Integration Tests для Settings Executor

```csharp
[TestClass]
public class SettingsExecutorIntegrationTests
{
    [TestMethod]
    [TestCategory("Integration")]
    public async Task ApplyProfile_RollbackOnFailure()
    {
        // Arrange
        var executor = new TransactionalSettingsExecutor(
            new[] { new MockFailingProvider() },
            new InMemoryStateCache()
        );
        var profile = TestProfiles.GamingLowLatency;
        
        // Act
        var result = await executor.ApplyProfileAsync(
            profile, 
            CancellationToken.None);
        
        // Assert
        Assert.IsFalse(result.Success);
        Assert.IsTrue(result.RolledBackChanges.Count > 0);
    }
}
```

---

## 8. Итоговая Структура Проекта

```
bye-tcp-internet/
├── src/
│   ├── ByeTcp.Orchestration/      # NEW: Orchestration Layer
│   │   ├── Orchestrator.cs
│   │   ├── Watchdog.cs
│   │   ├── GlobalExceptionHandler.cs
│   │   └── ByeTcp.Orchestration.csproj
│   │
│   ├── ByeTcp.Monitoring/         # NEW: Monitoring Layer
│   │   ├── EtwProcessMonitor.cs
│   │   ├── AdaptiveNetworkMonitor.cs
│   │   ├── DiagnosticsEngine.cs
│   │   └── ByeTcp.Monitoring.csproj
│   │
│   ├── ByeTcp.Decision/           # NEW: Decision Layer
│   │   ├── PureRuleEngine.cs
│   │   ├── StateManager.cs
│   │   ├── RateLimiter.cs
│   │   └── ByeTcp.Decision.csproj
│   │
│   ├── ByeTcp.Execution/          # NEW: Execution Layer
│   │   ├── TransactionalSettingsExecutor.cs
│   │   ├── Providers/
│   │   │   ├── RegistrySettingsProvider.cs
│   │   │   ├── NetShSettingsProvider.cs
│   │   │   └── WfpSettingsProvider.cs
│   │   ├── StateCache.cs
│   │   └── ByeTcp.Execution.csproj
│   │
│   ├── ByeTcp.Infrastructure/     # NEW: Infrastructure Layer
│   │   ├── VersionedConfigManager.cs
│   │   ├── SecurityHelper.cs
│   │   └── ByeTcp.Infrastructure.csproj
│   │
│   └── ByeTcp.Service/            # Simplified: Windows Service Host
│       ├── Program.cs
│       ├── ByeTcpService.cs
│       └── ByeTcp.Service.csproj
│
├── config/
│   ├── profiles.json              # v2.0 schema
│   ├── rules.json                 # v2.0 schema
│   └── settings.json
│
├── schemas/                       # NEW: JSON Schemas
│   ├── profiles.schema.json
│   └── rules.schema.json
│
├── tests/                         # NEW: Test Projects
│   ├── ByeTcp.Decision.Tests/
│   ├── ByeTcp.Execution.Tests/
│   └── ByeTcp.Integration.Tests/
│
├── scripts/
│   ├── install.ps1                # Updated for v2.0
│   ├── build.ps1
│   └── test.ps1
│
└── docs/
    ├── ARCHITECTURE_v2.md         # This document
    └── MIGRATION_GUIDE.md         # v1.0 → v2.0
```

---

## 9. Список Изменений с Обоснованием

| Изменение | Обоснование |
|-----------|-------------|
| **WMI → ETW** | Zero-overhead, event-driven, kernel-level reliability |
| **Polling → Adaptive** | Reduced CPU usage, smarter diagnostics |
| **Monolithic Service → Layered** | SRP, SoC, testability |
| **NetSh Process → PowerShell SDK** | No process spawning, better error handling |
| **No Cache → State Cache** | Prevent redundant applies, idempotency |
| **No Transaction → Transactional Executor** | Atomic operations, rollback on failure |
| **No Rate Limit → Debounce** | Prevent thrashing on frequent process changes |
| **No Versioning → Versioned Config** | Schema evolution, backward compatibility |
| **No Validation → JSON Schema** | Early error detection, config integrity |
| **No Watchdog → Health Monitoring** | Self-recovery, improved reliability |

---

## 10. Миграция с v1.0 на v2.0

См. `docs/MIGRATION_GUIDE.md` для подробных инструкций по миграции.
