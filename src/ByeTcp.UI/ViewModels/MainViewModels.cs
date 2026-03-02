using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using ByeTcp.Client.IPC;
using ByeTcp.Client.Models;

namespace ByeTcp.UI.ViewModels;

/// <summary>
/// Базовый ViewModel для всех страниц
/// </summary>
public partial class ViewModelBase : ObservableObject
{
    [ObservableProperty]
    private string title = string.Empty;
    
    [ObservableProperty]
    private bool isLoading;
    
    [ObservableProperty]
    private string? errorMessage;
}

/// <summary>
/// ViewModel для Dashboard
/// </summary>
public partial class DashboardViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    private readonly CancellationTokenSource _refreshCts = new();
    
    [ObservableProperty]
    private bool isServiceRunning;
    
    [ObservableProperty]
    private string? currentProfileName;
    
    [ObservableProperty]
    private TimeSpan uptime;
    
    [ObservableProperty]
    private double rttMs;
    
    [ObservableProperty]
    private double jitterMs;
    
    [ObservableProperty]
    private double packetLossPercent;
    
    [ObservableProperty]
    private NetworkQuality networkQuality;
    
    [ObservableProperty]
    private ObservableCollection<MetricDataPoint> rttHistory = new();
    
    [ObservableProperty]
    private ObservableCollection<MetricDataPoint> jitterHistory = new();
    
    public DashboardViewModel(IByeTcpServiceClient client)
    {
        _client = client;
        Title = "Dashboard";
        
        // Запуск автообновления метрик
        _ = StartMetricsRefreshAsync();
    }
    
    /// <summary>
    /// Запуск периодического обновления метрик
    /// </summary>
    private async Task StartMetricsRefreshAsync()
    {
        try
        {
            while (!_refreshCts.Token.IsCancellationRequested)
            {
                await RefreshAsync();
                await Task.Delay(TimeSpan.FromSeconds(1), _refreshCts.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Нормальная отмена
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error refreshing metrics: {ex.Message}";
        }
    }
    
    /// <summary>
    /// Обновление данных
    /// </summary>
    [RelayCommand]
    private async Task RefreshAsync()
    {
        if (IsLoading) return;
        
        try
        {
            IsLoading = true;
            ErrorMessage = null;
            
            // Получение статуса службы
            var status = await _client.GetServiceStatusAsync(_refreshCts.Token);
            IsServiceRunning = status.IsRunning;
            CurrentProfileName = status.CurrentProfileName;
            Uptime = status.Uptime;
            
            // Получение метрик
            if (IsServiceRunning)
            {
                var metrics = await _client.GetNetworkMetricsAsync(_refreshCts.Token);
                RttMs = metrics.RttMs;
                JitterMs = metrics.JitterMs;
                PacketLossPercent = metrics.PacketLossPercent;
                NetworkQuality = metrics.Quality;
                
                // Добавление в историю для графика
                var now = DateTime.Now;
                RttHistory.Add(new MetricDataPoint { Timestamp = now, Value = metrics.RttMs, MetricType = "RTT" });
                JitterHistory.Add(new MetricDataPoint { Timestamp = now, Value = metrics.JitterMs, MetricType = "Jitter" });
                
                // Ограничение истории (последние 60 точек)
                if (RttHistory.Count > 60) RttHistory.RemoveAt(0);
                if (JitterHistory.Count > 60) JitterHistory.RemoveAt(0);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    public override void Dispose()
    {
        _refreshCts.Cancel();
        _refreshCts.Dispose();
        base.Dispose();
    }
}

/// <summary>
/// ViewModel для Profiles
/// </summary>
public partial class ProfilesViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    
    [ObservableProperty]
    private ObservableCollection<TcpProfileDto> profiles = new();
    
    [ObservableProperty]
    private TcpProfileDto? selectedProfile;
    
    [ObservableProperty]
    private string activeProfileId = string.Empty;
    
    [ObservableProperty]
    private bool isEditMode;
    
    [ObservableProperty]
    private string editProfileName = string.Empty;
    
    [ObservableProperty]
    private string editProfileDescription = string.Empty;
    
    [ObservableProperty]
    private int? editTcpAckFrequency;
    
    [ObservableProperty]
    private int? editTcpNoDelay;
    
    [ObservableProperty]
    private string? editCongestionProvider;
    
    public ProfilesViewModel(IByeTcpServiceClient client)
    {
        _client = client;
        Title = "Profiles";
        _ = LoadProfilesAsync();
    }
    
    /// <summary>
    /// Загрузка профилей
    /// </summary>
    [RelayCommand]
    private async Task LoadProfilesAsync()
    {
        try
        {
            IsLoading = true;
            var response = await _client.GetProfilesAsync();
            
            Profiles.Clear();
            foreach (var profile in response.Profiles)
            {
                Profiles.Add(profile);
            }
            
            ActiveProfileId = response.ActiveProfileId;
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading profiles: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Применение выбранного профиля
    /// </summary>
    [RelayCommand]
    private async Task ApplyProfileAsync()
    {
        if (SelectedProfile == null) return;
        
        // TODO: Показать диалог подтверждения
        try
        {
            IsLoading = true;
            var response = await _client.ApplyProfileAsync(SelectedProfile.Id, dryRun: false, timeoutSeconds: 30);
            
            if (response.Success)
            {
                ActiveProfileId = SelectedProfile.Id;
                // TODO: Показать уведомление об успехе
            }
            else
            {
                ErrorMessage = response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error applying profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Предварительный просмотр (Dry Run)
    /// </summary>
    [RelayCommand]
    private async Task PreviewProfileAsync()
    {
        if (SelectedProfile == null) return;
        
        try
        {
            IsLoading = true;
            var response = await _client.ApplyProfileAsync(SelectedProfile.Id, dryRun: true, timeoutSeconds: 30);
            
            // TODO: Показать диалог с preview изменений
            // response.AppliedChanges содержит список изменений
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error previewing profile: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Начало редактирования
    /// </summary>
    [RelayCommand]
    private void StartEdit()
    {
        if (SelectedProfile == null) return;
        
        IsEditMode = true;
        EditProfileName = SelectedProfile.Name;
        EditProfileDescription = SelectedProfile.Description;
        EditTcpAckFrequency = SelectedProfile.TcpAckFrequency;
        EditTcpNoDelay = SelectedProfile.TcpNoDelay;
        EditCongestionProvider = SelectedProfile.CongestionProvider;
    }
    
    /// <summary>
    /// Сохранение редактирования
    /// </summary>
    [RelayCommand]
    private async Task SaveEditAsync()
    {
        // TODO: Отправить изменения на сервис
        IsEditMode = false;
        await LoadProfilesAsync();
    }
    
    /// <summary>
    /// Отмена редактирования
    /// </summary>
    [RelayCommand]
    private void CancelEdit()
    {
        IsEditMode = false;
    }
}

/// <summary>
/// ViewModel для Diagnostics
/// </summary>
public partial class DiagnosticsViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    private CancellationTokenSource? _pingCts;
    
    [ObservableProperty]
    private string targetHost = "8.8.8.8";
    
    [ObservableProperty]
    private int pingCount = 4;
    
    [ObservableProperty]
    private int pingTimeout = 3000;
    
    [ObservableProperty]
    private bool isPinging;
    
    [ObservableProperty]
    private ObservableCollection<PingResultDto> pingResults = new();
    
    [ObservableProperty]
    private int minMs;
    
    [ObservableProperty]
    private int maxMs;
    
    [ObservableProperty]
    private int avgMs;
    
    [ObservableProperty]
    private int packetLossCount;
    
    public DiagnosticsViewModel(IByeTcpServiceClient client)
    {
        _client = client;
        Title = "Diagnostics";
    }
    
    /// <summary>
    /// Запуск Ping теста
    /// </summary>
    [RelayCommand]
    private async Task RunPingAsync()
    {
        if (IsPinging) return;
        
        try
        {
            IsPinging = true;
            IsLoading = true;
            ErrorMessage = null;
            PingResults.Clear();
            
            _pingCts = new CancellationTokenSource();
            
            var response = await _client.RunPingAsync(
                TargetHost, 
                PingCount, 
                PingTimeout, 
                _pingCts.Token);
            
            foreach (var result in response.Results)
            {
                PingResults.Add(result);
            }
            
            MinMs = response.MinMs;
            MaxMs = response.MaxMs;
            AvgMs = response.AvgMs;
            PacketLossCount = response.PacketLoss;
        }
        catch (OperationCanceledException)
        {
            // Отменено пользователем
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Ping error: {ex.Message}";
        }
        finally
        {
            IsPinging = false;
            IsLoading = false;
            _pingCts?.Dispose();
            _pingCts = null;
        }
    }
    
    /// <summary>
    /// Остановка Ping теста
    /// </summary>
    [RelayCommand]
    private void StopPing()
    {
        _pingCts?.Cancel();
    }
}

/// <summary>
/// ViewModel для Logs
/// </summary>
public partial class LogsViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    private CancellationTokenSource? _liveCts;
    
    [ObservableProperty]
    private ObservableCollection<LogEntryDto> logEntries = new();
    
    [ObservableProperty]
    private string selectedLevel = "All";
    
    [ObservableProperty]
    private bool isLiveMode;
    
    public LogsViewModel(IByeTcpServiceClient client)
    {
        _client = client;
        Title = "Logs";
        _ = LoadLogsAsync();
    }
    
    /// <summary>
    /// Загрузка логов
    /// </summary>
    [RelayCommand]
    private async Task LoadLogsAsync()
    {
        try
        {
            IsLoading = true;
            var response = await _client.GetLogsAsync(count: 100, levelFilter: SelectedLevel == "All" ? null : SelectedLevel);
            
            LogEntries.Clear();
            foreach (var entry in response.Logs)
            {
                LogEntries.Add(entry);
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error loading logs: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Переключение Live режима
    /// </summary>
    [RelayCommand]
    private async Task ToggleLiveModeAsync()
    {
        if (IsLiveMode)
        {
            _liveCts?.Cancel();
            IsLiveMode = false;
        }
        else
        {
            IsLiveMode = true;
            _liveCts = new CancellationTokenSource();
            
            try
            {
                while (IsLiveMode && !_liveCts.Token.IsCancellationRequested)
                {
                    await LoadLogsAsync();
                    await Task.Delay(TimeSpan.FromSeconds(2), _liveCts.Token);
                }
            }
            catch (OperationCanceledException)
            {
                // Нормальная отмена
            }
        }
    }
    
    /// <summary>
    /// Экспорт логов
    /// </summary>
    [RelayCommand]
    private async Task ExportLogsAsync()
    {
        // TODO: Реализовать экспорт в CSV/JSON
        await Task.CompletedTask;
    }
}

/// <summary>
/// ViewModel для Settings
/// </summary>
public partial class SettingsViewModel : ViewModelBase
{
    private readonly IByeTcpServiceClient _client;
    
    [ObservableProperty]
    private string selectedTheme = "Dark";
    
    [ObservableProperty]
    private string selectedLanguage = "ru-RU";
    
    public SettingsViewModel(IByeTcpServiceClient client)
    {
        _client = client;
        Title = "Settings";
    }
    
    /// <summary>
    /// Создание резервной копии
    /// </summary>
    [RelayCommand]
    private async Task CreateBackupAsync()
    {
        try
        {
            IsLoading = true;
            var response = await _client.CreateBackupAsync();
            
            if (response.Success)
            {
                // TODO: Показать уведомление
            }
            else
            {
                ErrorMessage = response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error creating backup: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Откат к предыдущему состоянию
    /// </summary>
    [RelayCommand]
    private async Task RollbackAsync()
    {
        try
        {
            IsLoading = true;
            var response = await _client.RollbackAsync();
            
            if (!response.Success)
            {
                ErrorMessage = response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error rolling back: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
    
    /// <summary>
    /// Сброс к настройкам по умолчанию
    /// </summary>
    [RelayCommand]
    private async Task ResetToDefaultsAsync()
    {
        try
        {
            IsLoading = true;
            var response = await _client.ResetToDefaultsAsync();
            
            if (!response.Success)
            {
                ErrorMessage = response.ErrorMessage;
            }
        }
        catch (Exception ex)
        {
            ErrorMessage = $"Error resetting: {ex.Message}";
        }
        finally
        {
            IsLoading = false;
        }
    }
}
