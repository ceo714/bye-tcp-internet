````markdown
# Bye-TCP Internet

Адаптивный оптимизатор TCP/IP стека для Windows 10/11.

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://github.com/ceo714/bye-tcp-internet)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## Описание

Bye-TCP Internet — фоновая служба Windows для динамической оптимизации TCP/IP стека в реальном времени.

В отличие от статических утилит, система адаптирует параметры сети автоматически на основе:

- активных приложений (игры, торренты, стриминг);
- текущих сетевых условий (RTT, jitter, packet loss);
- пользовательских правил и приоритетов.

Проект ориентирован на управляемую, предсказуемую и безопасную оптимизацию без ручной перенастройки после каждого сценария использования.

---

## Ключевые возможности

| Возможность | Описание |
|-------------|----------|
| Runtime Adaptation | Применение настроек без перезагрузки системы |
| App-Aware Profiling | Автоматическое переключение профилей по процессам |
| Real-time Monitoring | Мониторинг RTT, jitter, packet loss |
| Zero-UI Operation | Работа в фоне как Windows Service |
| Safe Rollback | Резервирование и восстановление настроек |
| WFP Integration | Поддержка низкоуровневого контроля через Windows Filtering Platform |

---

## Требования

- Windows 10/11 (x64)
- .NET 8 Runtime или self-contained сборка
- Права администратора
- Доступ к WMI (по умолчанию включен)

---

## Установка

```powershell
git clone https://github.com/ceo714/bye-tcp-internet.git
cd bye-tcp-internet

dotnet build -c Release

# запуск от имени Administrator
.\scripts\install.ps1
````

### Проверка статуса

```powershell
.\scripts\install.ps1 -Status
```

### Управление службой

```powershell
.\scripts\install.ps1 -Start
.\scripts\install.ps1 -Stop
.\scripts\install.ps1 -Uninstall
```

---

## Архитектура

```
ByeTcp.Service (Windows Service Host)
    ├── Process Monitor (WMI)
    ├── Network Monitor (ICMP / metrics)
    ├── Rule Engine
    ├── Profile Manager
    ├── Settings Applier (Registry / NetSh)
    └── Diagnostics Engine

                    ↓

Windows TCP/IP Stack
(Registry + NetSh + WFP)
```

Подробное описание архитектуры см. в `docs/ARCHITECTURE.md`.

---

## Конфигурация

### Профили

Файл: `config/profiles.json`

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

### Правила

Файл: `config/rules.json`

```json
{
  "id": "gaming_cs2",
  "priority": 100,
  "conditions": {
    "process": { "name": "cs2.exe", "state": "running" }
  },
  "profile": "gaming_low_latency"
}
```

---

## Встроенные профили

| Профиль                 | Назначение                          |
| ----------------------- | ----------------------------------- |
| default                 | Базовые настройки Windows           |
| gaming_low_latency      | Минимальная задержка                |
| gaming_extreme          | Агрессивная оптимизация             |
| torrent_high_throughput | Максимальная пропускная способность |
| streaming               | Баланс задержки и стабильности      |
| web_browsing            | Повседневная работа                 |

---

## Логи и мониторинг

По умолчанию логи сохраняются в:

```
C:\Program Files\ByeTcp\logs\
```

Просмотр в реальном времени:

```powershell
Get-Content "C:\Program Files\ByeTcp\logs\bye-tcp.log" -Tail 50 -Wait
```

Логирование реализовано через Serilog с поддержкой уровней Debug / Information / Warning / Error.

---

## Разработка

### Структура проекта

```
src/
 ├── ByeTcp.Service      # Windows Service Host
 ├── ByeTcp.Core         # Бизнес-логика
 ├── ByeTcp.Contracts    # Интерфейсы
 ├── ByeTcp.Infrastructure
 └── ByeTcp.Native       # WFP / C++ модуль
```

### Сборка

```bash
dotnet build ByeTcp.sln -c Release
```

Native-модуль:

```bash
msbuild src\ByeTcp.Native\ByeTcp.Native.vcxproj -p:Configuration=Release -p:Platform=x64
```

### Запуск в режиме отладки

```bash
dotnet run --project src/ByeTcp.Service
```

---

## Безопасность и ограничения

* Требуются права Administrator.
* Часть параметров может требовать перезагрузки.
* Возможны конфликты с антивирусным ПО.
* Kernel-mode компоненты требуют подписанного драйвера.

Сброс TCP-стека:

```powershell
netsh int tcp reset
```

---

## Changelog

### v0.1.0 (2026-03-01)

* Базовая реализация Windows Service
* WMI мониторинг процессов
* ICMP мониторинг сети
* Rule Engine с приоритетами
* Применение настроек через Registry и NetSh
* Резервирование и восстановление
* Базовая интеграция WFP (без подписанного драйвера)

---

## Лицензия

MIT License — см. файл LICENSE.

---

## Контакты

Автор: [https://github.com/ceo714](https://github.com/ceo714)

Issues и предложения:
[https://github.com/ceo714/bye-tcp-internet/issues](https://github.com/ceo714/bye-tcp-internet/issues)

```
```
