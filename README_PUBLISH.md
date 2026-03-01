# 📡 Bye-TCP Internet v2.0
## Адаптивный оптимизатор TCP/IP для Windows 10/11

[![Version](https://img.shields.io/badge/version-2.0.0-blue.svg)](.)
[![Platform](https://img.shields.io/badge/platform-Windows%2010%2F11%20x64-lightgrey.svg)](.)
[![.NET](https://img.shields.io/badge/.NET-8.0-purple.svg)](.)
[![License](https://img.shields.io/badge/license-MIT-green.svg)](LICENSE)

---

## 📖 Описание

**Bye-TCP Internet** — это система автоматической оптимизации сетевого стека Windows в реальном времени. Приложение мониторит активные процессы и сетевые условия, автоматически применяя оптимальные настройки TCP/IP.

### Ключевые возможности

| Возможность | Описание |
|-------------|----------|
| 🎮 **App-Aware Profiling** | Авто-применение профилей для игр (CS2, Valorant), торрентов (qBittorrent), стриминга (Discord) |
| 📊 **Real-time Monitoring** | Мониторинг RTT, jitter, packet loss в реальном времени |
| ⚡ **Zero-UI Service** | Фоновая служба без графического интерфейса (опционально GUI приложение) |
| 💾 **Safe Rollback** | Автоматическое резервирование и восстановление настроек |
| 🔒 **Secure** | Проверка прав администратора, защита конфигурации |

---

## 📋 Требования

### Обязательные

| Компонент | Версия | Примечание |
|-----------|--------|------------|
| **ОС** | Windows 10/11 x64 | Build 19041 или выше |
| **.NET SDK** | 8.0 или выше | Для сборки из исходников |
| **Права** | Administrator | Для применения настроек TCP/IP |

### Опциональные

| Компонент | Назначение |
|-----------|------------|
| **Visual Studio 2022** | Разработка и отладка |
| **Windows App SDK** | Для сборки GUI приложения |

---

## 🚀 Быстрый старт

### Вариант 1: Готовые бинарники (рекомендуется)

```powershell
# Перейти в папку публикации
cd publish\UI

# Запустить GUI приложение
.\ByeTcp.UI.Wpf.exe

# Или службу (требует Administrator)
cd ..\service
.\ByeTcp.Service.exe
```

### Вариант 2: Сборка из исходников

```powershell
# 1. Установите .NET 8 SDK
# https://dotnet.microsoft.com/download/dotnet/8.0

# 2. Клонировать репозиторий
git clone https://github.com/YOUR_USERNAME/bye-tcp-internet.git
cd bye-tcp-internet

# 3. Очистить проект (опционально)
.\cleanup.ps1

# 4. Восстановить пакеты
dotnet restore

# 5. Собрать
dotnet build -c Release

# 6. Опубликовать GUI приложение
dotnet publish src\ByeTcp.UI.Wpf\ByeTcp.UI.Wpf.csproj `
    -c Release -r win-x64 --self-contained true -o publish\UI

# 7. Опубликовать службу
dotnet publish src\ByeTcp.Service\ByeTcp.Service.csproj `
    -c Release -r win-x64 --self-contained true -o publish\service
```

---

## 📁 Структура проекта

```
bye-tcp-internet/
├── 📂 src/                    # Исходный код
│   ├── ByeTcp.UI.Wpf/         # GUI приложение (WPF)
│   ├── ByeTcp.Service/        # Windows служба
│   ├── ByeTcp.Orchestration/  # Оркестратор
│   ├── ByeTcp.Monitoring/     # Мониторинг (WMI, сеть)
│   ├── ByeTcp.Decision/       # Rule Engine
│   ├── ByeTcp.Execution/      # Применение настроек
│   └── ByeTcp.Infrastructure/ # Конфигурация, безопасность
│
├── 📂 publish/                # Скомпилированные бинарники
│   ├── UI/                    # GUI приложение
│   │   └── ByeTcp.UI.Wpf.exe  # ← ЗАПУСТИТЬ ДЛЯ GUI
│   └── service/               # Служба
│       └── ByeTcp.Service.exe # ← ЗАПУСТИТЬ ДЛЯ СЛУЖБЫ
│
├── 📂 config/                 # Конфигурация
│   ├── profiles.json          # Профили оптимизации
│   └── rules.json             # Правила переключения
│
├── 📂 schemas/                # JSON Schema для валидации
├── 📂 scripts/                # PowerShell скрипты
├── 📂 docs/                   # Документация
├── cleanup.ps1                # Скрипт очистки
└── README.md                  # Этот файл
```

---

## 💻 Использование

### GUI Приложение

1. **Запуск:**
   ```
   publish\UI\ByeTcp.UI.Wpf.exe
   ```

2. **Выбор профиля:**
   - 🎮 **Gaming** — минимальная задержка для игр
   - 📥 **Torrent** — максимальная пропускная способность
   - 📺 **Streaming** — баланс для видеостриминга
   - 🌐 **Web** — для веб-браузинга
   - ⚙️ **Default** — настройки Windows по умолчанию

3. **Применение:**
   - Нажмите **"✅ Применить"**
   - Подтвердите UAC запрос (требуются права администратора)

4. **Резервная копия:**
   - Нажмите **"💾 Backup"** для создания резервной копии настроек

### PowerShell скрипты

```powershell
# Применить профиль
.\apply.ps1 -Profile gaming

# Мониторинг сети и процессов
.\demo.ps1 -Monitor

# Показать справку
.\apply.ps1 -Help
```

### Установка как служба Windows

```powershell
# От имени Administrator
sc.exe create ByeTcp binPath= "C:\Path\To\ByeTcp.Service.exe" start= auto
sc.exe description ByeTcp "Адаптивный оптимизатор TCP/IP"
sc.exe start ByeTcp

# Проверка статуса
sc.exe query ByeTcp

# Удаление
sc.exe stop ByeTcp
sc.exe delete ByeTcp
```

---

## ⚙️ Профили оптимизации

### Gaming (Low Latency)

```json
{
  "TcpAckFrequency": 1,
  "TcpNoDelay": 1,
  "TcpDelAckTicks": 0,
  "ReceiveWindowAutoTuningLevel": "normal",
  "CongestionProvider": "ctcp",
  "EcnCapability": "disabled"
}
```

**Когда использовать:** CS2, Valorant, Apex Legends, Overwatch, Fortnite

### Torrent (High Throughput)

```json
{
  "TcpAckFrequency": 2,
  "TcpNoDelay": 0,
  "TcpDelAckTicks": 2,
  "ReceiveWindowAutoTuningLevel": "experimental",
  "CongestionProvider": "cubic",
  "EcnCapability": "enabled"
}
```

**Когда использовать:** qBittorrent, uTorrent, BitTorrent

### Streaming

```json
{
  "TcpAckFrequency": 1,
  "TcpNoDelay": 1,
  "TcpDelAckTicks": 1,
  "ReceiveWindowAutoTuningLevel": "normal",
  "CongestionProvider": "ctcp",
  "EcnCapability": "enabled"
}
```

**Когда использовать:** Discord, Zoom, Microsoft Teams, OBS

---

## 🔧 Установка зависимостей

### .NET 8 Runtime (для готовых бинарников)

Если при запуске появляется ошибка ".NET 8 не найден":

1. Скачайте с официального сайта:
   https://dotnet.microsoft.com/download/dotnet/8.0

2. Установите **.NET 8 Desktop Runtime** (для GUI) или
   **.NET 8 ASP.NET Core Runtime** (для службы)

3. Перезапустите приложение

### Windows App SDK (для сборки GUI)

```powershell
# Установить через Visual Studio Installer:
# - workload "Разработка классических приложений Windows"
# - компонент "Windows App SDK"

# Или вручную:
winget install Microsoft.WindowsAppSDK
```

### Visual Studio 2022 (для разработки)

```
1. Скачайте: https://visualstudio.microsoft.com/downloads/
2. Установите workloads:
   - .NET Desktop Development
   - Windows App SDK (опционально для WinUI)
```

---

## ⚠️ Важные замечания

### Безопасность

- ⚠️ **Требуется запуск от Administrator** для применения настроек TCP/IP
- ⚠️ **Некоторые параметры требуют перезагрузки** для применения
- ⚠️ **Создайте резервную копию** перед применением профилей

### Совместимость

| Компонент | Поддержка |
|-----------|-----------|
| Windows 10 2004+ | ✅ Полная |
| Windows 11 | ✅ Полная |
| Windows Server 2019+ | ✅ Полная |
| Windows 7/8.1 | ❌ Не поддерживается |

### Известные ограничения

1. **WFP драйвер** требует подписи (отключен по умолчанию)
2. **ETW мониторинг** требует прав администратора
3. **Некоторые NetSh команды** могут требовать перезагрузки

---

## 🐛 Решение проблем

### Ошибка "Не найден .NET 8"

```powershell
# Проверка установленной версии
dotnet --version

# Если не установлено — скачайте с:
# https://dotnet.microsoft.com/download/dotnet/8.0
```

### Ошибка "Требуется Administrator"

```powershell
# Запуск от имени администратора
Start-Process .\ByeTcp.UI.Wpf.exe -Verb RunAs
```

### Ошибка применения настроек

1. Проверьте права администратора
2. Создайте резервную копию: `.\apply.ps1 -Backup`
3. Попробуйте применить по одному параметру

### Служба не запускается

```powershell
# Проверка журнала событий
Get-EventLog -LogName Application -Source "ByeTcp" -Newest 20

# Пересоздание службы
sc.exe delete ByeTcp
sc.exe create ByeTcp binPath= "C:\Path\To\ByeTcp.Service.exe" start= auto
```

---

## 📚 Документация

| Документ | Описание |
|----------|----------|
| [`docs/ARCHITECTURE_v2.md`](docs/ARCHITECTURE_v2.md) | Архитектура v2.0 |
| [`docs/REFACTORING_SUMMARY.md`](docs/REFACTORING_SUMMARY.md) | Перечень изменений |
| [`docs/IPC_INTEGRATION.md`](docs/IPC_INTEGRATION.md) | IPC интеграция |
| [`PROJECT_COMPLETE.md`](PROJECT_COMPLETE.md) | Статус проекта |

---

## 🤝 Вклад в проект

```powershell
# 1. Fork репозитория
# 2. Создайте ветку
git checkout -b feature/my-feature

# 3. Внесите изменения
# 4. Запустите тесты
dotnet test

# 5. Создайте Pull Request
```

---

## 📄 Лицензия

MIT License — см. файл [LICENSE](LICENSE)

---

## 📞 Контакты

- 📧 Email: your-email@example.com
- 💬 Issues: https://github.com/YOUR_USERNAME/bye-tcp-internet/issues
- 📖 Wiki: https://github.com/YOUR_USERNAME/bye-tcp-internet/wiki

---

## 🙏 Благодарности

- [Microsoft Docs](https://docs.microsoft.com/windows/) — документация Windows API
- [LiveCharts2](https://livecharts.dev/) — графики для UI
- [Serilog](https://serilog.net/) — структурированное логирование

---

**© 2026 Bye-TCP Internet Project**
