# ✅ IPC Интеграция — Завершена

## 📦 Что создано

### IPC Сервер (Service Side)

**Файл:** [`src/ByeTcp.Service/IPC/IpcServerService.cs`](d:\bye-tcp-internet\src\ByeTcp.Service\IPC\IpcServerService.cs)

**Класс:** `IpcServerService : BackgroundService`

---

## 🔧 Реализованные методы

| Запрос (UI → Service) | Ответ (Service → UI) | Описание |
|-----------------------|---------------------|----------|
| `ServiceStatusRequest` | `ServiceStatusResponse` | Статус службы, профиль, uptime |
| `NetworkMetricsRequest` | `NetworkMetricsResponse` | RTT, Jitter, Packet Loss |
| `GetProfilesRequest` | `GetProfilesResponse` | Список профилей |
| `ApplyProfileRequest` | `ApplyProfileResponse` | Применение профиля (с Dry Run) |
| `GetRulesRequest` | `GetRulesResponse` | Список правил |
| `GetLogsRequest` | `GetLogsResponse` | Логи службы |
| `PingRequest` | `PingResponse` | Ping тест |
| `CreateBackupRequest` | `CreateBackupResponse` | Создание backup |
| `RollbackRequest` | `RollbackResponse` | Откат к предыдущему |
| `ResetToDefaultsRequest` | `ResetToDefaultsResponse` | Сброс к default |

---

## 🏗️ Архитектура

```
┌─────────────────────────────────────────────────────────┐
│                  UI Application (WinUI 3)                │
│  ┌─────────────────────────────────────────────────┐    │
│  │  NamedPipeServiceClient                         │    │
│  │  - ConnectAsync()                               │    │
│  │  - SendRequestAsync<T>()                        │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────┬───────────────────────────────────┘
                      │ Named Pipe
                      │ "ByeTcpServicePipe"
                      ▼
┌─────────────────────────────────────────────────────────┐
│                  Windows Service                         │
│  ┌─────────────────────────────────────────────────┐    │
│  │  IpcServerService : BackgroundService           │    │
│  │  - HandleClientAsync()                          │    │
│  │  - ProcessRequestAsync()                        │    │
│  │  - Request Handlers                             │    │
│  └─────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────┘
```

---

## 📝 Примеры использования

### 1. Подключение из UI

```csharp
var client = new NamedPipeServiceClient(logger, "ByeTcpServicePipe");

// Подключение с таймаутом
if (!await client.ConnectAsync(TimeSpan.FromSeconds(5), ct))
{
    throw new Exception("Failed to connect to service");
}
```

### 2. Получение статуса службы

```csharp
var status = await client.GetServiceStatusAsync(ct);

Console.WriteLine($"Running: {status.IsRunning}");
Console.WriteLine($"Profile: {status.CurrentProfileName}");
Console.WriteLine($"Uptime: {status.Uptime}");
```

### 3. Применение профиля (с Preview)

```csharp
// Preview (Dry Run)
var preview = await client.ApplyProfileAsync("gaming", dryRun: true);
Console.WriteLine($"Changes: {preview.AppliedChanges.Count}");

// Apply
var result = await client.ApplyProfileAsync("gaming", dryRun: false, timeoutSeconds: 30);

if (!result.Success)
{
    Console.WriteLine($"Error: {result.ErrorMessage}");
}
```

### 4. Ping тест

```csharp
var pingResult = await client.RunPingAsync("8.8.8.8", count: 4, timeoutMs: 3000);

Console.WriteLine($"Min: {pingResult.MinMs}ms");
Console.WriteLine($"Max: {pingResult.MaxMs}ms");
Console.WriteLine($"Avg: {pingResult.AvgMs}ms");
Console.WriteLine($"Loss: {pingResult.PacketLoss} packets");
```

---

## 🔒 Безопасность

### 1. Named Pipe Security

По умолчанию Named Pipe доступен только для процессов текущего пользователя.
Для ограничения доступа можно добавить ACL:

```csharp
var security = new PipeSecurity();
security.AddAccessRule(new PipeAccessRule(
    "Administrators",
    PipeAccessRights.FullControl,
    AccessControlType.Allow
));

var pipe = new NamedPipeServerStream(
    "ByeTcpServicePipe",
    PipeDirection.InOut,
    1,
    PipeTransmissionMode.Byte,
    PipeOptions.Asynchronous,
    65536,
    65536,
    security);
```

### 2. Проверка прав

Сервис проверяет права администратора при запуске:

```csharp
if (!SecurityHelper.IsRunningAsAdministrator())
{
    Console.WriteLine("WARNING: Not running as Administrator!");
}
```

---

## 🛠️ Регистрация в Service

**Файл:** [`src/ByeTcp.Service/Program.cs`](d:\bye-tcp-internet\src\ByeTcp.Service\Program.cs)

```csharp
public static IHostBuilder CreateHostBuilder(string[] args) =>
    Host.CreateDefaultBuilder(args)
        .UseWindowsService()
        .UseSerilog()
        .ConfigureServices((hostContext, services) =>
        {
            // ... другие сервисы
            
            // IPC Server для связи с UI
            services.AddHostedService<IpcServerService>();
            
            // Orchestrator
            services.AddHostedService<ByeTcpHostedService>();
        });
```

---

## 📊 Обработка ошибок

### 1. Timeout на операции

```csharp
using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
timeoutCts.CancelAfter(TimeSpan.FromSeconds(request.TimeoutSeconds));

var result = await settingsExecutor.ApplyProfileAsync(profile, timeoutCts.Token);
```

### 2. Error response

```csharp
catch (Exception ex)
{
    return new ApplyProfileResponse
    {
        CorrelationId = request.CorrelationId,
        Success = false,
        ErrorMessage = ex.Message
    };
}
```

### 3. Логирование

```csharp
_logger.LogInformation("✅ Клиент подключён");
_logger.LogDebug("⬅️ Получено: {Json}", requestJson);
_logger.LogError(ex, "Ошибка обработки запроса {MessageType}", request.MessageType);
```

---

## 🧪 Тестирование

### 1. Проверка подключения

```powershell
# Запуск службы
sc start ByeTcp

# Проверка pipe
Get-ChildItem \\.\pipe\ | Where-Object Name -like "*ByeTcp*"
```

### 2. UI Integration Test

```csharp
[TestClass]
public class IpcIntegrationTests
{
    [TestMethod]
    public async Task ConnectAndGetStatus()
    {
        var client = new NamedPipeServiceClient(logger);
        
        var connected = await client.ConnectAsync(TimeSpan.FromSeconds(5));
        Assert.IsTrue(connected);
        
        var status = await client.GetServiceStatusAsync();
        Assert.IsTrue(status.IsRunning);
    }
}
```

---

## 📈 Производительность

### 1. Асинхронная модель

Все операции используют async/await:

```csharp
await pipe.ReadAsync(buffer, 0, buffer.Length, ct);
await pipe.WriteAsync(responseBytes, 0, responseBytes.Length, ct);
```

### 2. Буферизация

```csharp
var buffer = new byte[65536]; // 64KB buffer
```

### 3. Переподключение

При обрыве соединения сервер автоматически перезапускается:

```csharp
while (!stoppingToken.IsCancellationRequested)
{
    try
    {
        await RunServerAsync(stoppingToken);
    }
    catch (Exception ex)
    {
        _logger.LogError(ex, "Ошибка IPC сервера. Перезапуск через 5 сек...");
        await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
    }
}
```

---

## ✅ Чеклист интеграции

| Компонент | Статус | Файл |
|-----------|--------|------|
| IPC Server Service | ✅ | `IpcServerService.cs` |
| Request Handlers | ✅ | 10 handlers |
| Response Generation | ✅ | Все типы ответов |
| Error Handling | ✅ | Try-catch, error responses |
| Logging | ✅ | Serilog |
| Cancellation Tokens | ✅ | Все операции |
| Timeouts | ✅ | На клиенте и сервере |
| DI Registration | ✅ | `Program.cs` |
| IPC Client | ✅ | `NamedPipeServiceClient.cs` |
| UI Integration | ✅ | ViewModels используют client |

---

## 🚀 Сборка и запуск

```powershell
cd d:\bye-tcp-internet

# Сборка службы
"C:\Program Files\dotnet\dotnet.exe" build src\ByeTcp.Service\ByeTcp.Service.csproj -c Release

# Сборка UI
"C:\Program Files\dotnet\dotnet.exe" build src\ByeTcp.UI\ByeTcp.UI.csproj -c Release

# Установка службы (Administrator)
.\scripts\install.ps1

# Запуск UI
Start-Process "src\ByeTcp.UI\bin\Release\net8.0-windows10.0.19041.0\win-x64\ByeTcp.UI.exe"
```

---

## 📚 Документация

- [`IMPLEMENTATION_COMPLETE.md`](d:\bye-tcp-internet\IMPLEMENTATION_COMPLETE.md) — UI документация
- [`docs/CONVERTERS_GUIDE.md`](d:\bye-tcp-internet\docs\CONVERTERS_GUIDE.md) — Конвертеры
- [`docs/WINUI3_APP_GUIDE.md`](d:\bye-tcp-internet\docs\WINUI3_APP_GUIDE.md) — WinUI 3 guide

---

## ✅ Готово!

IPC интеграция завершена. UI приложение может общаться со службой через Named Pipes.
