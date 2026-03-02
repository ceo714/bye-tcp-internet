using System.Diagnostics;
using System.IO;
using System.Windows;
using System.Windows.Controls;

namespace ByeTcp.UI.Wpf;

public partial class MainWindow : Window
{
    private string selectedProfile = "gaming";
    private readonly string basePath;

    public MainWindow()
    {
        InitializeComponent();
        basePath = AppDomain.CurrentDomain.BaseDirectory;
        Log("Приложение запущено");
        _ = LoadCurrentSettingsAsync();
    }

    private void Log(string msg) => 
        LogTxt.Text = $"[{DateTime.Now:HH:mm:ss}] {msg}\n" + LogTxt.Text;

    private async Task LoadCurrentSettingsAsync()
    {
        try
        {
            Log("Загрузка настроек...");
            var psi = new ProcessStartInfo("netsh.exe", "int tcp show global")
            {
                RedirectStandardOutput = true,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            using var proc = Process.Start(psi);
            if (proc != null)
            {
                var output = await proc.StandardOutput.ReadToEndAsync();
                proc.WaitForExit();

                foreach (var line in output.Split('\n'))
                {
                    if (line.Contains("Auto-Tuning")) 
                        AutoTuningTxt.Text = "Auto-Tuning: " + line.Split(':').Last().Trim();
                    else if (line.Contains("Congestion")) 
                        CongestionTxt.Text = "Congestion: " + line.Split(':').Last().Trim();
                    else if (line.Contains("ECN")) 
                        EcnTxt.Text = "ECN: " + line.Split(':').Last().Trim();
                    else if (line.Contains("Timestamps")) 
                        TimestampsTxt.Text = "Timestamps: " + line.Split(':').Last().Trim();
                }
                Log("Настройки загружены");
            }
        }
        catch (Exception ex) { Log($"Ошибка: {ex.Message}"); }
    }

    private void Profile_Click(object sender, RoutedEventArgs e)
    {
        if (sender is Button btn && btn.Tag is string profile)
        {
            selectedProfile = profile;
            Log($"Выбран: {profile}");
            GamingBtn.Background = TorrentBtn.Background = StreamingBtn.Background = WebBtn.Background = DefaultBtn.Background = null;
            btn.Background = System.Windows.Media.Brushes.LightBlue;
        }
    }

    private async void Refresh_Click(object sender, RoutedEventArgs e) => await LoadCurrentSettingsAsync();

    private async void Apply_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log($"Применение {selectedProfile}...");
            var (at, cp, ecn) = selectedProfile switch
            {
                "gaming" => ("normal", "ctcp", "disabled"),
                "torrent" => ("experimental", "cubic", "enabled"),
                "streaming" => ("normal", "ctcp", "enabled"),
                "web" => ("normal", "default", "enabled"),
                _ => ("normal", "default", "default")
            };

            await RunNetShAsync($"int tcp set global autotuninglevel={at}");
            await RunNetShAsync($"int tcp set global congestionprovider={cp}");
            await RunNetShAsync($"int tcp set global ecncapability={ecn}");
            
            Log("Применено!");
            await LoadCurrentSettingsAsync();
        }
        catch (Exception ex) { Log($"Ошибка: {ex.Message}"); }
    }

    private async void Backup_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Log("Создание backup...");
            var path = System.IO.Path.Combine(basePath, $"backup-{DateTime.Now:yyyyMMdd-HHmmss}.reg");
            var psi = new ProcessStartInfo("reg.exe", $"export \"HKLM\\SYSTEM\\CurrentControlSet\\Services\\Tcpip\\Parameters\" \"{path}\" /y")
            {
                UseShellExecute = true,
                Verb = "runas"
            };
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                await proc.WaitForExitAsync();
                Log(File.Exists(path) ? $"Backup: {path}" : "Ошибка backup");
            }
        }
        catch (Exception ex) { Log($"Ошибка: {ex.Message}"); }
    }

    private async Task RunNetShAsync(string cmd)
    {
        var psi = new ProcessStartInfo("netsh.exe", cmd) { UseShellExecute = true, Verb = "runas" };
        using var proc = Process.Start(psi);
        if (proc != null) await proc.WaitForExitAsync();
    }
}
