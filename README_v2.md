# 📡 Bye-TCP Internet v2.0

**Адаптивный оптимизатор TCP/IP стека для Windows 10/11**  
*Версия 2.0 — Рефакторинг с разделением ответственности*

[![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)](.)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-lightgrey.svg)](.)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](.)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 📖 Описание

Bye-TCP Internet v2.0 — это **переработанная архитектура** адаптивного оптимизатора TCP/IP стека Windows с:

- **Событийной моделью** (ETW вместо WMI polling)
- **Транзакционным применением** настроек (rollback при ошибках)
- **Rate limiting** (предотвращение thrashing)
- **Health monitoring** (watchdog с self-recovery)
- **Валидацией конфигурации** (JSON Schema)

---

## 🏗️ Архитектура v2.0

```
┌─────────────────────────────────────────────────────────────────────────┐
│                         ORCHESTRATION LAYER                              │
│  ┌───────────────────────────────────────────────────────────────────┐  │
│  │  Orchestrator + Watchdog + Health Monitoring                      │  │
│  └───────────────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────────────┘
                                    │
        ┌───────────────────────────┼───────────────────────────┐
        ▼                           ▼                           ▼
┌───────────────────┐   ┌───────────────────┐   ┌───────────────────┐
│  MONITORING       │   │  DECISION         │   │  EXECUTION        │
│  Layer            │   │  Layer            │   │  Layer            │
│                   │   │                   │   │                   │
│ • ETW Process     │   │ • Pure Rule       │   │ • Transactional   │
│   Monitor         │   │   Engine          │   │   Executor        │
│ • Adaptive        │   │ • State Manager   │   │ • Registry        │
│   Network Monitor │   │ • Rate Limiter    │   │   Provider        │
│ • Diagnostics     │   │                   │   │ • NetSh Provider  │
│                   │   │                   │   │ • WFP Provider    │
└───────────────────┘   └───────────────────┘   └───────────────────┘
                                    │
                                    ▼
┌─────────────────────────────────────────────────────────────────────────┐
│                      INFRASTRUCTURE LAYER                                │
│  ┌─────────────────┐   ┌─────────────────┐   ┌─────────────────────┐   │
│  │ Versioned       │   │ Security        │   │ State Cache         │   │
│  │ Config Manager  │   │ Helper          │   │ (Idempotency)       │   │
│  └─────────────────┘   └─────────────────┘   └─────────────────────┘   │
└─────────────────────────────────────────────────────────────────────────┘
```

### Слои и Проекты

| Слой | Проект | Ответственность |
|------|--------|-----------------|
| **Contracts** | `ByeTcp.Contracts` | Общие модели и интерфейсы |
| **Monitoring** | `ByeTcp.Monitoring` | ETW Process Monitor, Adaptive Network Monitor |
| **Decision** | `ByeTcp.Decision` | Pure Rule Engine, State Manager |
| **Execution** | `ByeTcp.Execution` | Transactional Executor, Settings Providers |
| **Infrastructure** | `ByeTcp.Infrastructure` | Config Manager, Security |
| **Orchestration** | `ByeTcp.Orchestration` | Orchestrator, Watchdog |
| **Service** | `ByeTcp.Service` | Windows Service Host |

---

## 🚀 Быстрый старт

### Требования

- Windows 10/11 (x64)
- .NET 8 SDK
- Права администратора (для ETW и изменения TCP/IP)

### Сборка

```powershell
# Клонирование
git clone https://github.com/your-org/bye-tcp-internet.git
cd bye-tcp-internet

# Сборка всех проектов
dotnet build ByeTcp.sln -c Release

# Публикация
.\scripts\build.ps1 -Publish
```

### Установка

```powershell
# От Administrator
.\scripts\install.ps1

# Проверка статуса
.\scripts\install.ps1 -Status

# Валидация
.\scripts\test.ps1 -Verbose
```

---

## 📐 Ключевые Улучшения v2.0

### 1. ETW Process Monitor (вместо WMI)

```csharp
// v1.0: WMI с polling 1 секунда
var query = "SELECT * FROM __InstanceCreationEvent WITHIN 1 WHERE ...";

// v2.0: ETW kernel events (zero overhead)
_session.Source.Kernel.ProcessStart += OnProcessStart;
_session.Source.Kernel.ProcessStartEnable();
```

**Преимущества:**
- ⚡ Мгновенное обнаружение (<1ms)
- 📉 Zero CPU overhead
- 🔒 Не зависит от WMI службы

### 2. Transactional Settings Executor

```csharp
// v1.0: Последовательное применение без rollback
foreach (var setting in settings) Apply(setting);

// v2.0: Транзакционное применение с rollback
try
{
    foreach (var provider in providers)
    {
        await provider.ApplyAsync(changes);
    }
}
catch
{
    await RollbackAsync(rollbackStack); // All-or-nothing
}
```

**Преимущества:**
- ✅ Atomic operations
- ♻️ Rollback при ошибке
- 📊 Consistent state

### 3. Rate Limiting

```csharp
// Sliding window: max 10 изменений в минуту
if (!rateLimiter.AllowAction("changes", window: 1min, max: 10))
{
    _logger.LogWarning("Rate limit exceeded");
    return;
}
```

**Преимущества:**
- 🛑 Предотвращение thrashing
- 📈 Стабильность системы

### 4. JSON Schema Validation

```json
{
  "$schema": "https://bye-tcp.internet/schemas/v2/profiles.schema.json",
  "version": "2.0",
  "schemaVersion": "2026-03",
  "profiles": [...]
}
```

**Преимущества:**
- ✅ Ранняя валидация
- 🔒 Типобезопасность
- 📋 Документация схемы

---

## ⚙️ Конфигурация

### Профили (config/profiles.json)

```json
{
  "id": "gaming_low_latency",
  "name": "Gaming (Low Latency)",
  "tcpAckFrequency": 1,
  "tcpNoDelay": 1,
  "tcpDelAckTicks": 0,
  "congestionProvider": "ctcp"
}
```

### Правила (config/rules.json)

```json
{
  "id": "gaming_cs2",
  "priority": 100,
  "profile": "gaming_low_latency",
  "cooldown": "00:00:10",
  "conditions": {
    "process": { "name": "cs2.exe" }
  }
}
```

---

## 📊 Мониторинг и Логи

### Просмотр логов

```powershell
# Real-time tail
Get-Content "C:\Program Files\ByeTcp\logs\bye-tcp.log" -Tail 50 -Wait
```

### Формат логов (Serilog structured)

```
14:32:15.123 [INF] [ThreadId: 12] ▶️ Процесс запущен: cs2.exe (PID: 8456)
14:32:15.145 [INF] [ThreadId: 12] 🔄 Найдено правило: gaming_cs2 → gaming_low_latency
14:32:15.167 [DBG] [ThreadId: 12] 📝 Registry: TcpAckFrequency = 1
14:32:15.189 [DBG] [ThreadId: 12] 🌐 NetSh: congestionprovider=ctcp
14:32:15.212 [INF] [ThreadId: 12] ✅ Профиль применен за 89ms
```

---

## 🧪 Тестирование

### Unit Tests

```powershell
# Запуск тестов
dotnet test tests/ByeTcp.Decision.Tests -c Release
dotnet test tests/ByeTcp.Execution.Tests -c Release
```

### Integration Tests

```powershell
# Интеграционные тесты (требуют Administrator)
dotnet test tests/ByeTcp.Integration.Tests -c Release -- Admin
```

---

## 🔒 Безопасность

### UAC и Права

```powershell
# Проверка прав
.\scripts\test.ps1

# Требуется Administrator для:
# - ETW kernel session
# - Изменения реестра TCP/IP
# - NetSh команд
```

### ACL Конфигурации

```csharp
// Config файлы защищены ACL:
// - Administrators: Full Control
// - SYSTEM: Read
// - Service SID: Read/Write
```

---

## 🛠️ Расширение

### Добавление нового правила

1. Откройте `config/rules.json`
2. Добавьте правило:

```json
{
  "id": "my_custom_app",
  "priority": 80,
  "profile": "gaming_low_latency",
  "conditions": {
    "process": { "name": "myapp.exe" }
  }
}
```

### Добавление нового профиля

1. Откройте `config/profiles.json`
2. Добавьте профиль:

```json
{
  "id": "my_custom_profile",
  "name": "My Custom Profile",
  "tcpAckFrequency": 1,
  "tcpNoDelay": 1
}
```

---

## 📄 Документация

| Документ | Описание |
|----------|----------|
| [docs/ARCHITECTURE_v2.md](docs/ARCHITECTURE_v2.md) | Полное архитектурное описание |
| [docs/REFACTORING_SUMMARY.md](docs/REFACTORING_SUMMARY.md) | Перечень изменений v1.0 → v2.0 |
| [schemas/profiles.schema.json](schemas/profiles.schema.json) | JSON Schema для профилей |
| [schemas/rules.schema.json](schemas/rules.schema.json) | JSON Schema для правил |

---

## ⚠️ Риски и Ограничения

| Риск | Митигация |
|------|-----------|
| **Требуется Administrator** | Явная проверка при запуске, UAC prompt |
| **ETW kernel session** | Fallback на polling если недоступно |
| **WFP драйвер неподписан** | Fallback на user-mode (пропуск WFP настроек) |
| **Некоторые параметры требуют reboot** | Логирование, отложенное применение |

---

## 📝 Changelog

### v2.0.0 (2026-03-01)

**Архитектурные изменения:**
- ✅ Layered Architecture (Orchestration/Monitoring/Decision/Execution/Infrastructure)
- ✅ ETW Process Monitor (вместо WMI)
- ✅ Adaptive Network Monitor
- ✅ Pure Rule Engine (чистые функции)
- ✅ State Manager с rate limiting
- ✅ Transactional Settings Executor
- ✅ Versioned Config с JSON Schema
- ✅ Watchdog с health monitoring

**Улучшения:**
- ✅ Idempotency применения профилей
- ✅ Rollback при ошибках
- ✅ Debounce переключений (5 sec min)
- ✅ Sliding window rate limit (10/min)
- ✅ Structured logging (Serilog)
- ✅ Graceful shutdown (30 sec timeout)

**Безопасность:**
- ✅ Administrator check
- ✅ Config file ACL
- ✅ JSON Schema validation

### v1.0.0 (2026-02-15)

- Базовая реализация
- WMI Process Monitor
- NetSh/Registry settings
- Простые правила

---

## 🤝 Вклад

Приветствуются PR с:
- Новыми правилами для приложений
- Улучшениями производительности
- Исправлениями багов
- Документацией
- Unit/Integration тестами

---

## 📄 Лицензия

MIT License — см. [LICENSE](LICENSE)

---

## 🙏 Благодарности

- [Microsoft.Diagnostics.Tracing.TraceEvent](https://www.nuget.org/packages/Microsoft.Diagnostics.Tracing.TraceEvent) — ETW библиотека
- [NJsonSchema](https://github.com/RicoSuter/NJsonSchema) — JSON Schema валидация
- [Serilog](https://serilog.net/) — Structured logging
