# 🖥️ Bye-TCP Internet — WinUI 3 Desktop Application

## 📋 Обзор

Полноценное десктопное приложение для управления Bye-TCP Internet на базе WinUI 3 (Windows App SDK) с архитектурой MVVM.

---

## 🏗️ Архитектура

```
┌─────────────────────────────────────────────────────────────────┐
│                         UI Layer (WinUI 3)                       │
│  ┌─────────────┐  ┌─────────────┐  ┌─────────────────────────┐  │
│  │  Views      │  │ ViewModels  │  │  Converters/Controls    │  │
│  │  (XAML)     │◄─┤ (MVVM)      │  │  (Custom UI)            │  │
│  └─────────────┘  └─────────────┘  └─────────────────────────┘  │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                      Client Layer (IPC)                          │
│  ┌─────────────────────────────────────────────────────────┐    │
│  │  NamedPipeServiceClient  │  Models (DTOs)               │    │
│  │  - Async/Await           │  - IpcMessages               │    │
│  │  - Cancellation Tokens   │  - TcpProfileDto             │    │
│  │  - Timeouts              │  - NetworkMetrics            │    │
│  └─────────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────────┘
                              │
                              ▼
┌─────────────────────────────────────────────────────────────────┐
│                    Bye-TCP Service (v2.0)                        │
│  Orchestration → Monitoring → Decision → Execution              │
└─────────────────────────────────────────────────────────────────┘
```

---

## 📁 Структура проекта

```
bye-tcp-internet/
├── src/
│   ├── ByeTcp.UI/                    # WinUI 3 приложение
│   │   ├── Views/                    # XAML страницы
│   │   │   ├── DashboardPage.xaml
│   │   │   ├── ProfilesPage.xaml
│   │   │   ├── RulesPage.xaml
│   │   │   ├── DiagnosticsPage.xaml
│   │   │   ├── LogsPage.xaml
│   │   │   └── SettingsPage.xaml
│   │   ├── ViewModels/               # ViewModels (CommunityToolkit.Mvvm)
│   │   │   └── MainViewModels.cs
│   │   ├── Services/                 # UI сервисы
│   │   ├── Converters/               # Value converters
│   │   ├── Controls/                 # Custom controls
│   │   ├── App.xaml / App.xaml.cs    # DI настройка
│   │   └── MainWindow.xaml           # Главное окно
│   │
│   ├── ByeTcp.Client/                # IPC клиент
│   │   ├── IPC/
│   │   │   └── NamedPipeServiceClient.cs
│   │   └── Models/
│   │       └── IpcMessages.cs        # DTO для IPC
│   │
│   └── ByeTcp.UI.Strings/            # Локализация
│       └── Strings/
│           ├── en-US/Resources.resw
│           └── ru-RU/Resources.resw
│
└── tests/
    └── ByeTcp.UI.Tests/              # Unit тесты для ViewModels
```

---

## 🔧 Ключевые компоненты

### 1. IPC Клиент (Named Pipes)

**Файл:** `src/ByeTcp.Client/IPC/NamedPipeServiceClient.cs`

```csharp
// Подключение к сервису
var client = new NamedPipeServiceClient(logger, "ByeTcpServicePipe");
await client.ConnectAsync(TimeSpan.FromSeconds(5));

// Получение статуса
var status = await client.GetServiceStatusAsync(ct);

// Применение профиля (с Dry Run)
var result = await client.ApplyProfileAsync("gaming", dryRun: false, timeoutSeconds: 30, ct);

// Подписка на метрики (real-time)
await foreach (var metrics in client.SubscribeToMetricsAsync(TimeSpan.FromSeconds(1), ct))
{
    Console.WriteLine($"RTT: {metrics.RttMs}ms");
}
```

### 2. ViewModels (CommunityToolkit.Mvvm)

**Файл:** `src/ByeTcp.UI/ViewModels/MainViewModels.cs`

```csharp
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    
    [ObservableProperty]
    private double rttMs;
    
    [ObservableProperty]
    private bool isServiceRunning;
    
    [RelayCommand]
    private async Task RefreshAsync()
    {
        var status = await _client.GetServiceStatusAsync(ct);
        IsServiceRunning = status.IsRunning;
        
        var metrics = await _client.GetNetworkMetricsAsync(ct);
        RttMs = metrics.RttMs;
    }
}
```

### 3. Apply/Preview/Rollback операции

```csharp
// Preview (Dry Run)
var preview = await client.ApplyProfileAsync("gaming", dryRun: true);
// preview.AppliedChanges содержит список изменений

// Apply
var result = await client.ApplyProfileAsync("gaming", dryRun: false, timeoutSeconds: 30);
if (!result.Success)
{
    // Ошибка — сервис автоматически выполнил rollback
}

// Rollback
var rollback = await client.RollbackAsync();
```

---

## 🎨 UI Компоненты

### Dashboard

- **Service Status Card** — статус службы, текущий профиль, uptime
- **Metrics Cards** — RTT, Jitter, Packet Loss с прогресс-барами
- **Live Charts** — графики RTT/Jitter истории (LiveCharts2)
- **Quick Actions** — кнопки быстрого доступа

### Profiles

- **Profile Cards** — карточки профилей с индикатором активности
- **Profile Editor** — форма редактирования с preview изменений
- **Apply/Preview/Dry Run** — кнопки применения

### Diagnostics

- **Ping Test** — запуск ping с настраиваемыми параметрами
- **Results Table** — таблица результатов
- **Statistics** — Min/Max/Avg, Packet Loss %

### Logs

- **Virtualized List** — виртуализированная таблица логов
- **Filters** — по уровню, компоненту, времени
- **Live Mode** — автообновление каждые 2 сек
- **Export** — экспорт в CSV/JSON

---

## 🌐 Локализация

**Файлы ресурсов:**
- `Strings/en-US/Resources.resw` — English
- `Strings/ru-RU/Resources.resw` — Russian

**Использование:**
```xaml
<TextBlock Text="{x:Bind x:Static strings:Resources.Dashboard_Title}" />
```

---

## 🛠️ Сборка и запуск

### Требования

- Windows 10/11 (x64)
- .NET 8 SDK
- Visual Studio 2022 с workload "Windows App SDK"

### Сборка

```powershell
cd d:\bye-tcp-internet

# Восстановление пакетов
"C:\Program Files\dotnet\dotnet.exe" restore ByeTcp.sln

# Сборка UI
"C:\Program Files\dotnet\dotnet.exe" build src\ByeTcp.UI\ByeTcp.UI.csproj -c Release
```

### Запуск

```powershell
# Запуск приложения
Start-Process "d:\bye-tcp-internet\src\ByeTcp.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\ByeTcp.UI.exe"
```

### Установка службы (требуется для работы UI)

```powershell
# От Administrator
.\scripts\install.ps1
```

---

## 📊 Графики (LiveCharts2)

```csharp
// В ViewModel
public ISeries[] RttSeries { get; } = new ISeries[]
{
    new LineSeries<double>
    {
        Values = new ObservableCollection<double>(),
        Name = "RTT",
        Fill = null
    }
};

// Обновление данных
private void UpdateRtt(double value)
{
    ((LineSeries<double>)RttSeries[0]).Values.Add(value);
    if (RttSeries[0].Values.Count > 60)
        RttSeries[0].Values.RemoveAt(0);
}
```

---

## 🔒 Безопасность

### UAC Prompt

```csharp
// Проверка прав администратора
if (!SecurityHelper.IsRunningAsAdministrator())
{
    // Показать предупреждение
    // Предложить запустить от админа
}
```

### Transactional Apply

```csharp
// Сервис применяет настройки транзакционно
// При ошибке — автоматический rollback
var result = await client.ApplyProfileAsync("gaming");
if (!result.Success)
{
    // Все изменения отменены
}
```

---

## 🧪 Тестирование

### Unit Tests для ViewModels

```csharp
[TestClass]
public class DashboardViewModelTests
{
    [TestMethod]
    public async Task Refresh_UpdatesMetrics()
    {
        // Arrange
        var mockClient = new Mock<IByeTcpServiceClient>();
        mockClient.Setup(c => c.GetServiceStatusAsync(It.IsAny<Ct>()))
            .ReturnsAsync(new ServiceStatusResponse { IsRunning = true });
        
        var vm = new DashboardViewModel(mockClient.Object);
        
        // Act
        await vm.RefreshCommand.ExecuteAsync(null);
        
        // Assert
        Assert.IsTrue(vm.IsServiceRunning);
    }
}
```

---

## 📝 TODO для завершения

1. **Создать недостающие страницы:**
   - `ProfilesPage.xaml`
   - `RulesPage.xaml`
   - `DiagnosticsPage.xaml`
   - `LogsPage.xaml`
   - `SettingsPage.xaml`

2. **Добавить converters:**
   - `BoolToColorConverter`
   - `LossToColorConverter`
   - `NullToBoolConverter`

3. **Добавить CardControl:**
   ```xaml
   <Style x:Key="CardControlStyle" TargetType="Border">
       <Setter Property="Background" Value="{ThemeResource CardBackgroundFillColorDefaultBrush}" />
       <Setter Property="BorderBrush" Value="{ThemeResource CardStrokeColorDefaultBrush}" />
       <Setter Property="BorderThickness" Value="1" />
       <Setter Property="CornerRadius" Value="8" />
       <Setter Property="Padding" Value="16" />
   </Style>
   ```

4. **Реализовать Service-side IPC server** в ByeTcp.Service

5. **Добавить интеграционные тесты**

---

## 📚 Ресурсы

- [Windows App SDK](https://docs.microsoft.com/windows/apps/windows-app-sdk/)
- [CommunityToolkit.Mvvm](https://docs.microsoft.com/dotnet/communitytoolkit/mvvm/)
- [LiveCharts2 WinUI](https://livecharts.dev/docs/winui/2.0.0-rc2)
- [Named Pipes](https://docs.microsoft.com/dotnet/api/system.io.pipes.namedpipeclientstream)

---

## ✅ Чеклист готовности

- [x] IPC Client (Named Pipes)
- [x] Models/DTOs
- [x] ViewModels (Dashboard, Profiles, Diagnostics, Logs, Settings)
- [x] MainWindow с NavigationView
- [x] DashboardPage (XAML)
- [x] App.xaml с DI
- [x] Локализация RU/EN
- [ ] ProfilesPage (XAML)
- [ ] RulesPage (XAML)
- [ ] DiagnosticsPage (XAML)
- [ ] LogsPage (XAML)
- [ ] SettingsPage (XAML)
- [ ] Value Converters
- [ ] Custom Controls (CardControl)
- [ ] Service-side IPC Server
- [ ] Integration Tests
