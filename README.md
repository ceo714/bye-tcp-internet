# 📡 Bye-TCP Internet

**Адаптивный оптимизатор TCP/IP стека для Windows 10/11**

[![Version](https://img.shields.io/badge/version-0.1.0-blue.svg)](https://github.com/your-org/bye-tcp-internet)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11-lightgrey.svg)](https://www.microsoft.com/windows)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 📖 Описание

Bye-TCP Internet — это фоновая служба Windows для **динамической оптимизации TCP/IP стека** в реальном времени. В отличие от статических утилит вроде TCP Optimizer, наша система автоматически адаптирует параметры сети на основе:

- 🎮 **Активных приложений** (игры, торренты, стриминг)
- 📊 **Сетевых условий** (RTT, jitter, packet loss)
- ⏱️ **Времени суток** (опционально)

### Ключевые возможности

| Возможность | Описание |
|-------------|----------|
| **Runtime Adaptation** | Адаптация параметров без перезагрузки системы |
| **App-Aware Profiling** | Автоматическое применение профилей под приложения |
| **Real-time Monitoring** | Мониторинг RTT, jitter, packet loss в реальном времени |
| **Zero-UI Operation** | Работа в фоне без графического интерфейса |
| **Safe Rollback** | Резервирование и восстановление настроек |
| **WFP Integration** | Низкоуровневый контроль через Windows Filtering Platform |

---

## 🚀 Быстрый старт

### Требования

- Windows 10/11 (x64)
- .NET 8 Runtime (или self-contained сборка)
- Права администратора
- WMI доступ (включен по умолчанию)

### Установка

```powershell
# Клонирование репозитория
git clone https://github.com/your-org/bye-tcp-internet.git
cd bye-tcp-internet

# Сборка проекта
dotnet build -c Release

# Установка службы (от Administrator)
.\scripts\install.ps1
```

### Проверка статуса

```powershell
.\scripts\install.ps1 -Status
```

### Управление службой

```powershell
# Запуск
.\scripts\install.ps1 -Start

# Остановка
.\scripts\install.ps1 -Stop

# Удаление
.\scripts\install.ps1 -Uninstall
```

---

## 📐 Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│                    ByeTcp.Service.exe                            │
│  ┌───────────────────────────────────────────────────────────┐  │
│  │  Process Monitor (WMI)  →  Rule Engine  →  Settings Applier│  │
│  │  Network Monitor (ICMP) →               →  Registry/NetSh  │  │
│  │  Diagnostics Engine     →               →  WFP (optional)  │  │
│  └───────────────────────────────────────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
              ┌───────────────────────────────┐
              │    Windows TCP/IP Stack       │
              │  Registry + NetSh + WFP       │
              └───────────────────────────────┘
```

Подробное архитектурное описание см. в [docs/ARCHITECTURE.md](docs/ARCHITECTURE.md).

---

## ⚙️ Конфигурация

### Профили оптимизации

Профили определяются в `config/profiles.json`:

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

### Правила переключения

Правила определяются в `config/rules.json`:

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

### Встроенные профили

| Профиль | Описание | Когда применяется |
|---------|----------|------------------|
| `default` | Настройки Windows по умолчанию | Нет активных правил |
| `gaming_low_latency` | Минимальная задержка | CS2, Valorant, Apex |
| `gaming_extreme` | Экстремальная оптимизация | Соревновательные игры |
| `torrent_high_throughput` | Максимальная пропускная способность | qBittorrent, uTorrent |
| `streaming` | Баланс задержки и трафика | Discord, Zoom, OBS |
| `web_browsing` | Веб-браузинг | Chrome, Firefox, Edge |

---

## 📊 Мониторинг и логи

### Просмотр логов

```powershell
# Последние 50 строк в реальном времени
Get-Content "C:\Program Files\ByeTcp\logs\bye-tcp.log" -Tail 50 -Wait
```

### Формат логов

```
14:32:15.123 [INF] [ThreadId: 12] ▶️ Процесс запущен: cs2.exe (PID: 8456)
14:32:15.145 [INF] [ThreadId: 12] 🔄 Переключение профиля: Windows Default → Gaming (Low Latency)
14:32:15.167 [DBG] [ThreadId: 12] 📝 Registry: TcpAckFrequency = 1
14:32:15.189 [DBG] [ThreadId: 12] 🌐 NetSh: int tcp set global congestionprovider=ctcp
```

---

## 🔧 Расширение

### Добавление нового правила

1. Откройте `config/rules.json`
2. Добавьте новое правило:

```json
{
  "id": "my_custom_app",
  "priority": 80,
  "conditions": {
    "process": { "name": "myapp.exe", "state": "running" }
  },
  "profile": "gaming_low_latency"
}
```

### Добавление нового профиля

1. Откройте `config/profiles.json`
2. Добавьте новый профиль:

```json
{
  "id": "my_custom_profile",
  "name": "My Custom Profile",
  "description": "Описание профиля",
  "tcpAckFrequency": 1,
  "tcpNoDelay": 1,
  "congestionProvider": "cubic"
}
```

---

## 🛠️ Разработка

### Структура проекта

```
bye-tcp-internet/
├── src/
│   ├── ByeTcp.Service/      # Windows Service host
│   ├── ByeTcp.Core/         # Ядро системы
│   └── ByeTcp.Native/       # C++ WFP модуль
├── config/                  # Конфигурационные файлы
├── scripts/                 # Скрипты установки
├── docs/                    # Документация
└── logs/                    # Логи (создается при установке)
```

### Сборка

```bash
# Сборка всех проектов
dotnet build ByeTcp.sln -c Release

# Сборка native модуля (требуется Visual Studio с C++)
msbuild src\ByeTcp.Native\ByeTcp.Native.vcxproj -p:Configuration=Release -p:Platform=x64
```

### Тестирование

```bash
# Запуск в режиме отладки (не как служба)
dotnet run --project src/ByeTcp.Service
```

---

## ⚠️ Риски и ограничения

### Driver Signing (WFP)

Windows 10/11 требует подписанный драйвер для kernel-mode компонентов. Для разработки:

```powershell
# Включение тестового режима (требует перезагрузки)
bcdedit /set testsigning on
```

### Безопасность

- Служба требует прав **Administrator**
- Некоторые параметры требуют **перезагрузки** для применения
- Возможны конфликты с антивирусами (добавьте в исключения)

### Восстановление

При проблемах восстановите настройки из резервной копии:

```powershell
# Путь к резервной копии
$backup = "C:\Program Files\ByeTcp\backups\factory-defaults-*.reg"
reg import $backup
```

Или сбросьте через NetSh:

```powershell
netsh int tcp reset
```

---

## 📝 Changelog

### v0.1.0 (2026-03-01)

- ✅ Базовая реализация Windows Service
- ✅ WMI мониторинг процессов
- ✅ ICMP мониторинг сети (RTT, jitter)
- ✅ Rule Engine с приоритетами
- ✅ Применение настроек через Registry/NetSh
- ✅ Резервирование и восстановление
- ✅ 8 встроенных профилей оптимизации
- ✅ 20+ правил для популярных приложений
- ⚠️ WFP модуль (базовая реализация, без драйвера)

---

## 🤝 Вклад

Приветствуются PR с:

- Новыми правилами для приложений
- Улучшениями производительности
- Исправлениями багов
- Документацией

---

## 📄 Лицензия

MIT License — см. [LICENSE](LICENSE)

---

## 📞 Контакты

- GitHub Issues: [Сообщить о проблеме](https://github.com/your-org/bye-tcp-internet/issues)
- Email: your-email@example.com

---

## 🙏 Благодарности

- [TCP Optimizer](https://www.speedguide.net/downloads.php) — вдохновение для проекта
- [Microsoft Docs](https://docs.microsoft.com/en-us/windows/win32/api/) — документация Windows API
- [Serilog](https://serilog.net/) — структурированное логирование
