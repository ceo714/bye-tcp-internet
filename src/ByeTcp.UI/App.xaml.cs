using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Microsoft.UI.Xaml;
using Serilog;
using ByeTcp.Client.IPC;
using ByeTcp.UI.ViewModels;
using ByeTcp.UI.Views;

namespace ByeTcp.UI;

/// <summary>
/// Application entry point with Dependency Injection
/// </summary>
public partial class App : Application
{
    private readonly IHost _host;
    public IServiceProvider Services => _host.Services;

    public App()
    {
        // Configure Serilog
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .WriteTo.Debug()
            .WriteTo.File(
                Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                    "ByeTcp", "UI", "Logs", "app-.log"),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7)
            .CreateLogger();

        // Configure DI
        _host = Microsoft.Extensions.Hosting.Host
            .CreateDefaultBuilder()
            .UseContentRoot(AppContext.BaseDirectory)
            .ConfigureServices((context, services) =>
            {
                // IPC Client
                services.AddSingleton<IByeTcpServiceClient, NamedPipeServiceClient>();
                
                // ViewModels
                services.AddTransient<MainWindow>();
                services.AddTransient<DashboardViewModel>();
                services.AddTransient<ProfilesViewModel>();
                services.AddTransient<DiagnosticsViewModel>();
                services.AddTransient<LogsViewModel>();
                services.AddTransient<SettingsViewModel>();
                
                // Logging
                services.AddLogging(builder => builder.AddSerilog(dispose: true));
            })
            .Build();
        
        this.UnhandledException += OnUnhandledException;
    }

    private void OnUnhandledException(object sender, Microsoft.UI.Xaml.UnhandledExceptionEventArgs e)
    {
        Log.Fatal(e.Exception, "Unhandled exception in UI");
        e.Handled = true;
    }

    protected override void OnLaunched(Microsoft.UI.Xaml.LaunchActivatedEventArgs args)
    {
        var mainWindow = Services.GetRequiredService<MainWindow>();
        mainWindow.Activate();
    }

    public new static App Current => (App)Application.Current;
    
    public T GetService<T>() where T : class => Services.GetRequiredService<T>();
}
