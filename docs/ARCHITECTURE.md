# 📐 Bye-TCP Internet — Архитектурное Описание

## 1. Обзор Системы

**Bye-TCP Internet** — это фоновая служба Windows для динамической оптимизации TCP/IP стека в реальном времени на основе анализа сетевых условий и активных приложений.

### 1.1 Ключевые Возможности

- **Runtime Adaptation** — адаптация параметров TCP/IP без перезагрузки
- **App-Aware Profiling** — автоматическое применение профилей под конкретные приложения
- **Real-time Monitoring** — мониторинг RTT, jitter, packet loss
- **Zero-UI Operation** — работа в фоне без графического интерфейса
- **Safe Rollback** — резервирование и восстановление настроек

---

## 2. Архитектура Системы

```
┌─────────────────────────────────────────────────────────────────────────────┐
│                           WINDOWS SERVICE LAYER                              │
│                         (ByeTcp.Service.exe)                                 │
├─────────────────────────────────────────────────────────────────────────────┤
│                                                                              │
│  ┌──────────────────┐    ┌──────────────────┐    ┌──────────────────────┐  │
│  │  Service Host    │    │  Configuration   │    │   Logging Service    │  │
│  │  (WinService)    │◄──►│  Manager         │    │   (NLog/Serilog)     │  │
│  └────────┬─────────┘    └──────────────────┘    └──────────────────────┘  │
│           │                                                                  │
│           ▼                                                                  │
│  ┌──────────────────────────────────────────────────────────────────────┐   │
│  │                    CORE ENGINE (ByeTcp.Core)                          │   │
│  ├──────────────────────────────────────────────────────────────────────┤   │
│  │                                                                       │   │
│  │  ┌─────────────────┐  ┌─────────────────┐  ┌─────────────────────┐   │   │
│  │  │ Process Monitor │  │ Network Monitor │  │  Diagnostics Engine │   │   │
│  │  │ (WMI/ETW)       │  │ (Metrics)       │  │  (Ping/Traceroute)  │   │   │
│  │  └────────┬────────┘  └────────┬────────┘  └──────────┬──────────┘   │   │
│  │           │                    │                       │              │   │
│  │           └────────────────────┼───────────────────────┘              │   │
│  │                                ▼                                      │   │
│  │  ┌─────────────────────────────────────────────────────────────────┐  │   │
│  │  │                    Rule Engine                                   │  │   │
│  │  │              (Profile Matcher & Decision Maker)                  │  │   │
│  │  └────────────────────────────┬────────────────────────────────────┘  │   │
│  │                               │                                       │   │
│  └───────────────────────────────┼───────────────────────────────────────┘   │
│                                  │                                           │
└──────────────────────────────────┼───────────────────────────────────────────┘
                                   │
                    ┌──────────────┼──────────────┐
                    │              │              │
                    ▼              ▼              ▼
    ┌───────────────────┐ ┌───────────────────┐ ┌─────────────────────┐
    │  Registry Module  │ │   NetSh Module    │ │   WFP Module        │
    │  (RegEdit API)    │ │  (Process Spawn)  │ │  (Native DLL)       │
    └─────────┬─────────┘ └─────────┬─────────┘ └──────────┬──────────┘
              │                     │                       │
              └─────────────────────┼───────────────────────┘
                                    │
                                    ▼
    ┌───────────────────────────────────────────────────────────────────┐
    │                      WINDOWS TCP/IP STACK                          │
    │  ┌─────────────┐  ┌─────────────┐  ┌─────────────┐  ┌───────────┐ │
    │  │   Registry  │  │   NetSh     │  │  WFP Hooks  │  │  NDIS     │ │
    │  │  Parameters │  │   Commands  │  │  (Filter)   │  │  Driver   │ │
    │  └─────────────┘  └─────────────┘  └─────────────┘  └───────────┘ │
    └───────────────────────────────────────────────────────────────────┘
```

---

## 3. Компоненты Системы

### 3.1 ByeTcp.Service (Windows Service Host)

**Технология:** C# / .NET 8 / Worker Service

**Ответственность:**
- Хостинг службы Windows
- Управление жизненным циклом компонентов
- Обработка событий запуска/остановки
- Graceful shutdown

```
Startup Sequence:
1. Initialize Logging
2. Load Configuration
3. Create Backup of Current Settings
4. Start Process Monitor
5. Start Network Monitor
6. Start Diagnostics Engine
7. Apply Default Profile
```

### 3.2 Process Monitor (Мониторинг Процентов)

**Технология:** C# + WMI / System.Management / ETW

**Методы обнаружения:**
1. **WMI Event Subscription** — `__InstanceCreationEvent` / `__InstanceDeletionEvent`
2. **ETW Process Provider** — Microsoft-Windows-Kernel-Process
3. **Polling Fallback** — периодический опрос (резервный вариант)

**Оптимизация производительности:**
```csharp
// WMI Query с фильтрацией по имени процесса
SELECT * FROM __InstanceCreationEvent 
WHERE TargetInstance ISA "Win32_Process" 
AND (TargetInstance.Name = "cs2.exe" OR TargetInstance.Name = "qbittorrent.exe")
```

### 3.3 Network Monitor (Сетевые Метрики)

**Измеряемые параметры:**

| Метрика | Метод измерения | Частота |
|---------|-----------------|---------|
| RTT | ICMP Echo Request | 5 сек |
| Jitter | Стандартное отклонение RTT | 5 сек |
| Packet Loss | % потерянных ICMP пакетов | 30 сек |
| TCP Retransmissions | PerfCounter TCPv4 | 10 сек |
| Bandwidth Utilization | Performance Counters | 5 сек |

**Источники данных:**
- `System.Net.NetworkInformation.Ping` — ICMP измерения
- `PerformanceCounter` — TCPv4, Network Interface
- WFP Callouts — глубокий анализ трафика (опционально)

### 3.4 Diagnostics Engine (Диагностический Движок)

**Функции:**
- Периодические ping-тесты к целевым хостам
- Traceroute при обнаружении проблем
- Анализ DNS разрешения
- Проверка доступности шлюза

**Целевые хосты по умолчанию:**
```json
{
  "primary": "8.8.8.8",
  "secondary": "1.1.1.1",
  "gateway": "auto"
}
```

### 3.5 Rule Engine (Движок Правил)

**Архитектура:**

```
┌─────────────────────────────────────────────────────────┐
│                    Rule Engine                           │
├─────────────────────────────────────────────────────────┤
│                                                          │
│  ┌─────────────┐    ┌─────────────┐    ┌─────────────┐  │
│  │  Process    │    │  Network    │    │   Time      │  │
│  │  Rules      │    │  Rules      │    │   Rules     │  │
│  └──────┬──────┘    └──────┬──────┘    └──────┬──────┘  │
│         │                  │                  │         │
│         └──────────────────┼──────────────────┘         │
│                            ▼                            │
│              ┌─────────────────────────┐                │
│              │   Priority Resolver     │                │
│              │   (Conflict Detection)  │                │
│              └────────────┬────────────┘                │
│                           │                             │
│                           ▼                             │
│              ┌─────────────────────────┐                │
│              │   Profile Selector      │                │
│              └────────────┬────────────┘                │
│                           │                             │
└───────────────────────────┼─────────────────────────────┘
                            │
                            ▼
                   ┌─────────────────┐
                   │  Active Profile │
                   └─────────────────┘
```

**Формат правил (JSON):**
```json
{
  "rules": [
    {
      "id": "gaming_cs2",
      "priority": 100,
      "conditions": {
        "process": { "name": "cs2.exe", "state": "running" }
      },
      "profile": "gaming_low_latency",
      "actions": [
        { "type": "set_registry", "path": "TcpAckFrequency", "value": 1 },
        { "type": "set_registry", "path": "TCPNoDelay", "value": 1 }
      ]
    }
  ]
}
```

### 3.6 Settings Application Module (Модуль Применения Настроек)

**Уровни применения:**

```
Level 1: Registry (HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters)
         └─ Требует: Administrator privileges
         └─ Применяется: Мгновенно или после перезагрузки (зависит от параметра)
         
Level 2: NetSh Interface TCP
         └─ Команды: netsh int tcp set global ...
         └─ Применяется: Мгновенно
         
Level 3: WFP Callouts (Kernel Mode)
         └─ Требует: Signed Driver
         └─ Применяется: Мгновенно, прозрачно для приложений
```

**Критические параметры TCP/IP:**

| Параметр | Registry Path | NetSh Command | Перезагрузка |
|----------|---------------|---------------|--------------|
| TcpAckFrequency | `HKLM\SYSTEM\CCS\Services\Tcpip\Parameters\Interfaces\{GUID}\TcpAckFrequency` | ❌ | ❌ |
| TCPNoDelay | `HKLM\SYSTEM\CCS\Services\Tcpip\Parameters\Interfaces\{GUID}\TCPNoDelay` | ❌ | ❌ |
| TcpDelAckTicks | `HKLM\SYSTEM\CCS\Services\Tcpip\Parameters\Interfaces\{GUID}\TcpDelAckTicks` | ❌ | ❌ |
| ReceiveWindow | ❌ | `netsh int tcp set global autotuninglevel=...` | ❌ |
| CongestionProvider | ❌ | `netsh int tcp set global congestionprovider=...` | ❌ |
| ECN | ❌ | `netsh int tcp set global ecncapability=...` | ❌ |

### 3.7 WFP Module (Опционально)

**Технология:** C++ / Windows Filtering Platform

**Возможности:**
- Перехват TCP соединений на уровне kernel
- Принудительная установка TCP опций
- Мониторинг трафика в реальном времени
- QoS маркировка пакетов

**Требования:**
- Подписанный драйвер (WHQL или тестовая подпись)
- Отключенный Secure Boot для разработки
- Administrator privileges

---

## 4. Технологический Стек

### 4.1 Выбор Технологий

| Компонент | Технология | Обоснование |
|-----------|------------|-------------|
| Service Host | C# / .NET 8 | Быстрая разработка, богатая экосистема, async/await |
| Process Monitor | C# + WMI | Нативная поддержка Windows, event-driven модель |
| Network Monitor | C# + Ping/PerfCounters | Достаточно для базовых метрик |
| Registry Module | C# + Microsoft.Win32 | Прямой доступ к реестру |
| NetSh Module | C# + Process.Start | Универсальный интерфейс |
| WFP Driver | C++ / WDK | Низкоуровневый доступ, производительность |
| Configuration | JSON | Читаемость, гибкость, сериализация |
| Logging | Serilog | Структурированное логирование, sinks |

### 4.2 Аргументация Выбора C# для Service Layer

```
Преимущества:
✓ Быстрая разработка бизнес-логики
✓ Встроенная поддержка WMI (System.Management)
✓ Async/await для неблокирующего мониторинга
✓ Rich exception handling
✓ Easy deployment (ClickOnce, MSI, sc.exe)

Недостатки:
✗ Требует .NET Runtime (решается self-contained deployment)
✗ Небольшой overhead памяти (~20-50MB)
✗ Не подходит для kernel-mode компонентов
```

### 4.3 Аргументация Выбора C++ для WFP

```
Преимущества:
✓ Прямой доступ к Windows API
✓ Минимальный footprint памяти
✓ Единственный вариант для kernel-mode
✓ Производительность без GC overhead

Недостатки:
✗ Ручное управление памятью
✗ Сложнее отладка
✗ Требует WDK и подписи драйвера
```

---

## 5. Профили Оптимизации

### 5.1 Gaming Profile (Low Latency)

**Цель:** Минимизация задержек для онлайн-игр

```
TcpAckFrequency = 1          # Отправлять ACK на каждый пакет
TCPNoDelay = 1               # Отключить алгоритм Нагла
TcpDelAckTicks = 0           # Отключить отложенные ACK
ReceiveWindow = Auto         # Авто-настройка
CongestionProvider = CTCP    # Compound TCP для стабильности
ECN = Disabled               # Избежать задержек от ECN
```

### 5.2 Torrent Profile (High Throughput)

**Цель:** Максимизация пропускной способности

```
TcpAckFrequency = 2          # Баланс latency/throughput
TCPNoDelay = 0               # Включить алгоритм Нагла
TcpDelAckTicks = 2           # Отложенные ACK для эффективности
ReceiveWindow = 16MB+        # Увеличенное окно
CongestionProvider = CUBIC   # Агрессивный congestion control
ECN = Enabled                # Избежать потерь от переполнения
```

### 5.3 Default Profile (Balanced)

**Цель:** Сбалансированные настройки Windows по умолчанию

```
TcpAckFrequency = Default
TCPNoDelay = Default
TcpDelAckTicks = Default
ReceiveWindow = normal
CongestionProvider = Default
ECN = Default
```

---

## 6. Риски и Ограничения

### 6.1 Driver Signing (WFP)

```
Проблема: Windows 10/11 требует подписанный драйвер
Решения:
  1. WHQL сертификация (дорого, долго)
  2. Тестовая подпись + Test Mode (для разработки)
  3. Отключить WFP модуль, использовать только Registry/NetSh
```

### 6.2 Безопасность Windows

```
Проблема: UAC, Defender, Secure Boot
Митигация:
  - Запуск от имени Administrator
  - Добавление в исключения Defender
  - Подпись исполняемых файлов (code signing certificate)
```

### 6.3 Задержки Применения NetSh

```
Проблема: Некоторые параметры требуют переподключения
Митигация:
  - Предупреждение пользователя в логах
  - Graceful restart сетевых соединений при необходимости
```

### 6.4 Конфликты Настроек

```
Проблема: Несколько приложений с разными профилями
Решение:
  - Приоритизация правил (gaming > torrent > default)
  - Detection конфликтов в Rule Engine
  - Логирование всех переключений
```

### 6.5 Перезагрузка Системы

```
Проблема: Некоторые registry параметры требуют reboot
Митигация:
  - Использование альтернатив через NetSh где возможно
  - Явное указание в документации
  - Отложенное применение до следующей перезагрузки
```

---

## 7. Последовательность Взаимодействия

### 7.1 Обнаружение Запуска Игры

```
1. WMI Event: __InstanceCreationEvent for cs2.exe
        │
        ▼
2. Process Monitor → Rule Engine: "cs2.exe started"
        │
        ▼
3. Rule Engine匹配: gaming_cs2 rule (priority 100)
        │
        ▼
4. Profile Selector: gaming_low_latency
        │
        ▼
5. Settings Module:
   ├─ Registry: Set TcpAckFrequency=1
   ├─ Registry: Set TCPNoDelay=1
   └─ NetSh: Set congestionprovider=ctcp
        │
        ▼
6. Logger: "Applied gaming profile for cs2.exe"
        │
        ▼
7. Diagnostics: Start enhanced RTT monitoring
```

### 7.2 Завершение Игры

```
1. WMI Event: __InstanceDeletionEvent for cs2.exe
        │
        ▼
2. Process Monitor → Rule Engine: "cs2.exe exited"
        │
        ▼
3. Rule Engine: Check for other active rules
        │
        ▼
4. Profile Selector: default (no other rules match)
        │
        ▼
5. Settings Module: Restore default parameters
        │
        ▼
6. Logger: "Restored default profile"
```

---

## 8. Структура Конфигурации

```
config/
├── settings.json           # Основные настройки службы
├── profiles.json           # Определения профилей
├── rules.json              # Правила переключения
├── diagnostics.json        # Настройки диагностики
└── targets.json            # Целевые хосты для мониторинга

logs/
├── bye-tcp.log             # Основной лог
├── bye-tcp-errors.log      # Ошибки
└── history/                # Архив логов

backups/
├── backup-YYYYMMDD-HHMMSS.reg
├── backup-YYYYMMDD-HHMMSS.bat
└── factory-defaults.reg
```

---

## 9. Развертывание

### 9.1 Требования

- Windows 10/11 (x64)
- .NET 8 Runtime (или self-contained build)
- Administrator privileges
- Отключенный Secure Boot (для WFP драйвера)

### 9.2 Установка

```powershell
# 1. Копирование файлов
Copy-Item -Recurse .\bin\Release\ C:\Program Files\ByeTcp\

# 2. Регистрация службы
sc.exe create ByeTcp binPath= "C:\Program Files\ByeTcp\ByeTcp.Service.exe" start= auto

# 3. Настройка прав
icacls "C:\Program Files\ByeTcp" /grant Administrators:F

# 4. Запуск
sc.exe start ByeTcp
```

### 9.3 Удаление

```powershell
sc.exe stop ByeTcp
sc.exe delete ByeTcp
Remove-Item -Recurse "C:\Program Files\ByeTcp"
```

---

## 10. Диаграммы UML

### 10.1 Class Diagram (Core Components)

```
┌─────────────────────────────────────────────────────────────────┐
│                         ByeTcp.Core                              │
├─────────────────────────────────────────────────────────────────┤
│                                                                  │
│  ┌────────────────────┐         ┌────────────────────┐          │
│  │ IProcessMonitor    │◄───────│ WmiProcessMonitor  │          │
│  ├────────────────────┤         ├────────────────────┤          │
│  │ +Start()           │         │ +Start()           │          │
│  │ +Stop()            │         │ +Stop()            │          │
│  │ +GetRunning()      │         │ +GetRunning()      │          │
│  │ +ProcessStarted    │         │ -managementScope   │          │
│  │ +ProcessExited     │         │ -eventQuery        │          │
│  └─────────┬──────────┘         └────────────────────┘          │
│            │                                                     │
│            │ implements                                          │
│            ▼                                                     │
│  ┌────────────────────┐         ┌────────────────────┐          │
│  │ INetworkMonitor    │◄───────│ IcmpNetworkMonitor │          │
│  ├────────────────────┤         ├────────────────────┤          │
│  │ +Start()           │         │ +Start()           │          │
│  │ +Stop()            │         │ +GetMetrics()      │          │
│  │ +GetMetrics()      │         │ -pingTargets       │          │
│  │ +MetricsUpdated    │         │ -timer             │          │
│  └─────────┬──────────┘         └────────────────────┘          │
│            │                                                     │
│            ▼                                                     │
│  ┌────────────────────────────────────────────────────┐         │
│  │                  IRuleEngine                        │         │
│  ├────────────────────────────────────────────────────┤         │
│  │ +Evaluate(ProcessInfo, NetworkMetrics): Profile    │         │
│  │ +RegisterRule(Rule)                                │         │
│  │ +UnregisterRule(ruleId)                            │         │
│  │ +ProfileChanged                                    │         │
│  └────────────────────────────────────────────────────┘         │
│                                                                  │
└─────────────────────────────────────────────────────────────────┘
```

---

## 11. Производительность

### 11.1 Целевые Показатели

| Метрика | Target | Max |
|---------|--------|-----|
| CPU Usage | < 1% | < 5% |
| Memory | < 50 MB | < 100 MB |
| WMI Query Latency | < 100 ms | < 500 ms |
| Profile Switch Time | < 1 sec | < 3 sec |
| Ping Interval | 5 sec | configurable |

### 11.2 Оптимизации

```csharp
// 1. Event-driven вместо polling (где возможно)
// 2. Debounce для частых событий процессов
// 3. Batch применение registry изменений
// 4. Async/await для всех I/O операций
// 5. Object pooling для Ping объектов
```

---

## 12. Безопасность

### 12.1 Защита Конфигурации

- Шифрование чувствительных данных (AES-256)
- Валидация целостности файлов (SHA-256 hash)
- Restrict access к папке установки

### 12.2 Аудит и Логирование

- Логирование всех изменений настроек
- Логирование всех переключений профилей
- Rotation логов (ежедневно/еженедельно)

---

## 13. Расширяемость

### 13.1 Plugin Architecture (Future)

```
┌─────────────────────────────────────────┐
│           Plugin Interface               │
├─────────────────────────────────────────┤
│ IPlugin {                                │
│   string Name { get; }                   │
│   void Initialize(IHost host);           │
│   void ProcessMetrics(Metrics m);        │
│   void ApplyProfile(Profile p);          │
│ }                                        │
└─────────────────────────────────────────┘
```

### 13.2 Custom Rules (User-defined)

```json
{
  "custom_rules": [
    {
      "name": "MyApp Optimization",
      "process": "myapp.exe",
      "settings": {
        "TcpAckFrequency": 1,
        "CustomParam": "value"
      }
    }
  ]
}
```

---

## 14. Changelog Planned

- **v0.1** — Proof of Concept (Registry + WMI)
- **v0.2** — Full Service + NetSh integration
- **v0.3** — Diagnostics Engine + Logging
- **v0.4** — WFP Driver (experimental)
- **v1.0** — Production Release
