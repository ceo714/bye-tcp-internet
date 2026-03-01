using System.IO.Pipes;
using System.Text;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace ByeTcp.Client.IPC;

/// <summary>
/// Клиент для IPC связи с сервисом через Named Pipes
/// 
/// Особенности:
/// - Асинхронная модель (async/await)
/// - Cancellation tokens для всех операций
/// - Таймауты на операции
/// - Автоматическое переподключение
/// - Структурированное логирование
/// </summary>
public interface IByeTcpServiceClient : IDisposable
{
    // Service control
    Task<ServiceStatusResponse> GetServiceStatusAsync(CancellationToken ct = default);
    
    // Metrics
    Task<NetworkMetricsResponse> GetNetworkMetricsAsync(CancellationToken ct = default);
    IAsyncEnumerable<NetworkMetricsResponse> SubscribeToMetricsAsync(TimeSpan interval, CancellationToken ct = default);
    
    // Profiles
    Task<GetProfilesResponse> GetProfilesAsync(CancellationToken ct = default);
    Task<ApplyProfileResponse> ApplyProfileAsync(string profileId, bool dryRun = false, int timeoutSeconds = 30, CancellationToken ct = default);
    
    // Rules
    Task<GetRulesResponse> GetRulesAsync(CancellationToken ct = default);
    
    // Logs
    Task<GetLogsResponse> GetLogsAsync(int count = 100, string? levelFilter = null, CancellationToken ct = default);
    
    // Diagnostics
    Task<PingResponse> RunPingAsync(string target = "8.8.8.8", int count = 4, int timeoutMs = 3000, CancellationToken ct = default);
    
    // Backup/Restore
    Task<CreateBackupResponse> CreateBackupAsync(CancellationToken ct = default);
    Task<RollbackResponse> RollbackAsync(CancellationToken ct = default);
    Task<ResetToDefaultsResponse> ResetToDefaultsAsync(CancellationToken ct = default);
    
    // Connection
    Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default);
    void Disconnect();
    bool IsConnected { get; }
}

/// <summary>
/// Реализация клиента через Named Pipes
/// </summary>
public sealed class NamedPipeServiceClient : IByeTcpServiceClient
{
    private readonly ILogger<NamedPipeServiceClient> _logger;
    private readonly string _pipeName;
    private readonly JsonSerializerSettings _jsonSettings;
    
    private NamedPipeClientStream? _pipe;
    private bool _disposed;
    private readonly SemaphoreSlim _connectionLock = new(1, 1);

    public bool IsConnected => _pipe?.IsConnected ?? false;

    public NamedPipeServiceClient(
        ILogger<NamedPipeServiceClient> logger,
        string pipeName = "ByeTcpServicePipe")
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _pipeName = pipeName;
        
        _jsonSettings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.None,
            NullValueHandling = NullValueHandling.Ignore,
            DateFormatHandling = DateFormatHandling.IsoDateFormat
        };
        
        _logger.LogInformation("IPC Client initialized. Pipe: {PipeName}", pipeName);
    }

    /// <summary>
    /// Подключение к сервису
    /// </summary>
    public async Task<bool> ConnectAsync(TimeSpan timeout, CancellationToken ct = default)
    {
        await _connectionLock.WaitAsync(ct);
        
        try
        {
            if (IsConnected)
            {
                _logger.LogDebug("Уже подключено");
                return true;
            }
            
            _pipe = new NamedPipeClientStream(
                ".", 
                _pipeName, 
                PipeDirection.InOut, 
                PipeOptions.Asynchronous);
            
            try
            {
                await _pipe.ConnectAsync((int)timeout.TotalMilliseconds, ct);
                _logger.LogInformation("✅ Подключено к сервису");
                return true;
            }
            catch (TimeoutException)
            {
                _logger.LogWarning("⚠️ Таймаут подключения к сервису ({Timeout}ms)", timeout.TotalMilliseconds);
                _pipe.Dispose();
                _pipe = null;
                return false;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Ошибка подключения к сервису");
            _pipe?.Dispose();
            _pipe = null;
            return false;
        }
        finally
        {
            _connectionLock.Release();
        }
    }

    /// <summary>
    /// Отключение от сервиса
    /// </summary>
    public void Disconnect()
    {
        if (_pipe != null)
        {
            try
            {
                _pipe.Disconnect();
                _pipe.Dispose();
            }
            catch (Exception ex)
            {
                _logger.LogDebug(ex, "Ошибка при отключении");
            }
            finally
            {
                _pipe = null;
            }
            
            _logger.LogInformation("Отключено от сервиса");
        }
    }

    /// <summary>
    /// Отправка запроса и получение ответа
    /// </summary>
    private async Task<TResponse> SendRequestAsync<TResponse>(
        IpcMessage request,
        CancellationToken ct = default) where TResponse : IpcMessage
    {
        if (!IsConnected)
        {
            throw new InvalidOperationException("Not connected to service");
        }
        
        if (_pipe == null)
        {
            throw new InvalidOperationException("Pipe is null");
        }
        
        var json = JsonConvert.SerializeObject(request, _jsonSettings);
        var messageBytes = Encoding.UTF8.GetBytes(json);
        
        // Отправка запроса
        await _pipe.WriteAsync(messageBytes, 0, messageBytes.Length, ct);
        await _pipe.FlushAsync(ct);
        
        _logger.LogDebug("➡️ Отправлено: {MessageType} ({CorrelationId})", 
            request.MessageType, request.CorrelationId);
        
        // Чтение ответа
        var buffer = new byte[65536];
        var result = await _pipe.ReadAsync(buffer, 0, buffer.Length, ct);
        
        if (result == 0)
        {
            throw new IOException("Pipe closed unexpectedly");
        }
        
        var responseJson = Encoding.UTF8.GetString(buffer, 0, result);
        var response = JsonConvert.DeserializeObject<TResponse>(responseJson, _jsonSettings);
        
        if (response == null)
        {
            throw new InvalidOperationException("Failed to deserialize response");
        }
        
        _logger.LogDebug("⬅️ Получено: {MessageType} ({CorrelationId})", 
            response.MessageType, response.CorrelationId);
        
        return response;
    }

    // === Service Control ===
    
    public async Task<ServiceStatusResponse> GetServiceStatusAsync(CancellationToken ct = default)
    {
        var request = new ServiceStatusRequest();
        return await SendRequestAsync<ServiceStatusResponse>(request, ct);
    }

    // === Metrics ===
    
    public async Task<NetworkMetricsResponse> GetNetworkMetricsAsync(CancellationToken ct = default)
    {
        var request = new NetworkMetricsRequest();
        return await SendRequestAsync<NetworkMetricsResponse>(request, ct);
    }

    public async IAsyncEnumerable<NetworkMetricsResponse> SubscribeToMetricsAsync(
        TimeSpan interval,
        [System.Runtime.CompilerServices.EnumeratorCancellation] CancellationToken ct = default)
    {
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var metrics = await GetNetworkMetricsAsync(ct);
                yield return metrics;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Ошибка получения метрик");
            }
            
            await Task.Delay(interval, ct);
        }
    }

    // === Profiles ===
    
    public async Task<GetProfilesResponse> GetProfilesAsync(CancellationToken ct = default)
    {
        var request = new GetProfilesRequest();
        return await SendRequestAsync<GetProfilesResponse>(request, ct);
    }

    public async Task<ApplyProfileResponse> ApplyProfileAsync(
        string profileId, 
        bool dryRun = false, 
        int timeoutSeconds = 30, 
        CancellationToken ct = default)
    {
        var request = new ApplyProfileRequest
        {
            ProfileId = profileId,
            DryRun = dryRun,
            TimeoutSeconds = timeoutSeconds
        };
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(timeoutSeconds));
        
        return await SendRequestAsync<ApplyProfileResponse>(request, timeoutCts.Token);
    }

    // === Rules ===
    
    public async Task<GetRulesResponse> GetRulesAsync(CancellationToken ct = default)
    {
        var request = new GetRulesRequest();
        return await SendRequestAsync<GetRulesResponse>(request, ct);
    }

    // === Logs ===
    
    public async Task<GetLogsResponse> GetLogsAsync(
        int count = 100, 
        string? levelFilter = null, 
        CancellationToken ct = default)
    {
        var request = new GetLogsRequest
        {
            Count = count,
            LevelFilter = levelFilter
        };
        return await SendRequestAsync<GetLogsResponse>(request, ct);
    }

    // === Diagnostics ===
    
    public async Task<PingResponse> RunPingAsync(
        string target = "8.8.8.8", 
        int count = 4, 
        int timeoutMs = 3000, 
        CancellationToken ct = default)
    {
        var request = new PingRequest
        {
            Target = target,
            Count = count,
            TimeoutMs = timeoutMs
        };
        return await SendRequestAsync<PingResponse>(request, ct);
    }

    // === Backup/Restore ===
    
    public async Task<CreateBackupResponse> CreateBackupAsync(CancellationToken ct = default)
    {
        var request = new CreateBackupRequest();
        return await SendRequestAsync<CreateBackupResponse>(request, ct);
    }

    public async Task<RollbackResponse> RollbackAsync(CancellationToken ct = default)
    {
        var request = new RollbackRequest();
        return await SendRequestAsync<RollbackResponse>(request, ct);
    }

    public async Task<ResetToDefaultsResponse> ResetToDefaultsAsync(CancellationToken ct = default)
    {
        var request = new ResetToDefaultsRequest();
        return await SendRequestAsync<ResetToDefaultsResponse>(request, ct);
    }

    /// <summary>
    /// Освобождение ресурсов
    /// </summary>
    public void Dispose()
    {
        if (_disposed) return;
        
        _disposed = true;
        Disconnect();
        _connectionLock.Dispose();
        GC.SuppressFinalize(this);
    }
}
