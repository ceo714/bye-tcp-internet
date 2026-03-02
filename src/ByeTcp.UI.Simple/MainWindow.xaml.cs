using System.Diagnostics;
using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace ByeTcp.UI.Simple;

public sealed partial class MainWindow : Window
{
    private string selectedProfile = "gaming";
    private readonly string basePath;

    public MainWindow()
    {
        this.InitializeComponent();
        
        // Определяем базовый путь
        basePath = Path.GetDirectoryName(Process.GetCurrentProcess().MainModule?.FileName ?? "") ?? "";
        
        Log("Приложение запущено");
        Log($"Путь: {basePath}");
        
        // Загружаем текущие настройки
        _ = LoadCurrentSettingsAsync();
    }

    private void Log(string message)
    {
        var timestamp = DateTime.Now.ToString("HH:mm:ss");
        LogText.Text = $"[{timestamp}] {message}\n" + LogText.Text;
    }

    private async Task LoadCurrentSettingsAsync()
    {
        try
        {
            Log("Загрузка текущих настроек...");
            
            var psi = new ProcessStartInfo
            {
                FileName = "netsh.exe",
                Arguments = "int tcp show global",
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                var output = await process.StandardOutput.ReadToEndAsync();
                process.WaitForExit();

                // Парсим вывод
                foreach (var line in output.Split('\n'))
                {
                    if (line.Contains("Receive Window Auto-Tuning Level"))
                    {
                        var value = line.Split(':').Last().Trim();
                        AutoTuningText.Text = $"Auto-Tuning: {value}";
                    }
                    else if (line.Contains("Add-On Congestion Control Provider"))
                    {
                        var value = line.Split(':').Last().Trim();
                        CongestionText.Text = $"Congestion Provider: {value}";
                    }
                    else if (line.Contains("ECN Capability"))
                    {
                        var value = line.Split(':').Last().Trim();
                        EcnText.Text = $"ECN: {value}";
                    }
                    else if (line.Contains("RFC 1323 Timestamps"))
                    {
                        var value = line.Split(':').Last().Trim();
                        TimestampsText.Text = $"Timestamps: {value}";
                    }
                }

                Log("Настройки загружены");
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка: {ex.Message}");
        }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button button && button.Tag is string profile)
        {
            selectedProfile = profile;
            Log($"Выбран профиль: {profile}");
            
            // Визуальное выделение
            GamingButton.Background = null;
            TorrentButton.Background = null;
            StreamingButton.Background = null;
            WebButton.Background = null;
            DefaultButton.Background = null;
            
            button.Background = this.Resources.TryGetValue("AccentFillColorDefaultBrush", out var accent)
                ? accent as SolidColorBrush
                : null;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e)
    {
        await LoadCurrentSettingsAsync();
    }

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log($"Применение профиля {selectedProfile}...");
            
            var scriptPath = Path.Combine(basePath, "..", "..", "..", "..", "apply.ps1");
            
            if (!File.Exists(scriptPath))
            {
                // Пробуем применить через netsh напрямую
                await ApplyProfileDirectlyAsync(selectedProfile);
                return;
            }

            var psi = new ProcessStartInfo
            {
                FileName = "powershell.exe",
                Arguments = $"-ExecutionPolicy Bypass -File \"{scriptPath}\" -Profile {selectedProfile}",
                UseShellExecute = true,
                Verb = "runas" // Запрос UAC
            };

            Process.Start(psi);
            Log($"Запущено применение профиля: {selectedProfile}");
        }
        catch (Exception ex)
        {
            Log($"Ошибка: {ex.Message}");
        }
    }

    private async Task ApplyProfileDirectlyAsync(string profile)
    {
        try
        {
            var settings = profile switch
            {
                "gaming" => new { AutoTuning = "normal", Congestion = "ctcp", ECN = "disabled" },
                "torrent" => new { AutoTuning = "experimental", Congestion = "cubic", ECN = "enabled" },
                "streaming" => new { AutoTuning = "normal", Congestion = "ctcp", ECN = "enabled" },
                "web" => new { AutoTuning = "normal", Congestion = "default", ECN = "enabled" },
                _ => new { AutoTuning = "normal", Congestion = "default", ECN = "default" }
            };

            Log($"Выполнение netsh int tcp set global autotuninglevel={settings.AutoTuning}");
            await RunNetShCommandAsync($"int tcp set global autotuninglevel={settings.AutoTuning}");
            
            Log($"Выполнение netsh int tcp set global congestionprovider={settings.Congestion}");
            await RunNetShCommandAsync($"int tcp set global congestionprovider={settings.Congestion}");
            
            Log($"Выполнение netsh int tcp set global ecncapability={settings.ECN}");
            await RunNetShCommandAsync($"int tcp set global ecncapability={settings.ECN}");
            
            Log("Профиль применен!");
            await LoadCurrentSettingsAsync();
        }
        catch (Exception ex)
        {
            Log($"Ошибка применения: {ex.Message}");
        }
    }

    private async Task RunNetShCommandAsync(string command)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "netsh.exe",
            Arguments = command,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
            Verb = "runas"
        };

        using var process = Process.Start(psi);
        if (process != null)
        {
            await process.WaitForExitAsync();
        }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("Создание резервной копии...");
            
            var timestamp = DateTime.Now.ToString("yyyyMMdd-HHmmss");
            var backupPath = Path.Combine(basePath, $"backup-{timestamp}.reg");
            
            var psi = new ProcessStartInfo
            {
                FileName = "reg.exe",
                Arguments = $"export \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\" \"{backupPath}\" /y",
                UseShellExecute = true,
                Verb = "runas"
            };

            using var process = Process.Start(psi);
            if (process != null)
            {
                await process.WaitForExitAsync();
                
                if (File.Exists(backupPath))
                {
                    Log($"Резервная копия создана: {backupPath}");
                }
                else
                {
                    Log("Ошибка создания резервной копии");
                }
            }
        }
        catch (Exception ex)
        {
            Log($"Ошибка: {ex.Message}");
        }
    }
}
