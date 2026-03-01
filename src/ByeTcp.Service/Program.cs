using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.DependencyInjection;
using Serilog;
using Serilog.Events;
using ByeTcp.Contracts;
using ByeTcp.Monitoring;
using ByeTcp.Decision;
using ByeTcp.Execution;
using ByeTcp.Execution.Providers;
using ByeTcp.Infrastructure;
using ByeTcp.Orchestration;

namespace ByeTcp.Service;

/// <summary>
/// Bye-TCP Internet v2.0 — Windows Service Entry Point
/// </summary>
public class Program
{
    public static void Main(string[] args)
    {
        // Проверка прав администратора
        if (!SecurityHelper.IsRunningAsAdministrator())
        {
            Console.WriteLine("⚠️  WARNING: Not running as Administrator!");
            Console.WriteLine("   Some features (ETW, Registry, NetSh) require elevated privileges.");
            Console.WriteLine();
        }

        // Настройка Serilog
        var logPath = Path.Combine(AppContext.BaseDirectory, "logs", "bye-tcp-.log");
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Debug()
            .MinimumLevel.Override("Microsoft", LogEventLevel.Information)
            .MinimumLevel.Override("System", LogEventLevel.Warning)
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: logPath,
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: 7,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{ThreadId}] {Message:lj}{NewLine}{Exception}",
                fileSizeLimitBytes: 10_000_000
            )
            .CreateLogger();

        try
        {
            Log.Information("╔═══════════════════════════════════════════════════════════╗");
            Log.Information("║        Bye-TCP Internet Service v2.0                      ║");
            Log.Information("║  Layered Architecture - ETW Monitoring                    ║");
            Log.Information("╚═══════════════════════════════════════════════════════════╝");
            Log.Information("Starting service...");

            CreateHostBuilder(args).Build().Run();
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Service terminated unexpectedly");
        }
        finally
        {
            Log.Information("Service stopped");
            Log.CloseAndFlush();
        }
    }

    public static IHostBuilder CreateHostBuilder(string[] args) =>
        Host.CreateDefaultBuilder(args)
            .UseWindowsService(options =>
            {
                options.ServiceName = "ByeTcp";
            })
            .UseSerilog()
            .ConfigureServices((hostContext, services) =>
            {
                // Infrastructure Layer
                var configPath = Path.Combine(AppContext.BaseDirectory, "config");
                var statePath = Path.Combine(AppContext.BaseDirectory, "state", "state.json");
                var cachePath = Path.Combine(AppContext.BaseDirectory, "cache", "state.cache.json");
                var schemasPath = Path.Combine(AppContext.BaseDirectory, "schemas");

                services.AddSingleton<IConfigManager>(sp => 
                    new VersionedConfigManager(
                        sp.GetRequiredService<ILogger<VersionedConfigManager>>(),
                        schemasPath));

                services.AddSingleton<IStateCache>(sp =>
                    new InMemoryStateCache(
                        sp.GetRequiredService<ILogger<InMemoryStateCache>>(),
                        cachePath));

                services.AddSingleton<IRateLimiter, SlidingWindowRateLimiter>();

                // Monitoring Layer
                services.AddSingleton<IProcessMonitor, WmiProcessMonitor>();
                services.AddSingleton<INetworkMonitor, AdaptiveNetworkMonitor>();

                // Decision Layer
                services.AddSingleton<IRuleEngine, PureRuleEngine>();
                services.AddSingleton<IStateManager>(sp =>
                    new StateManager(
                        sp.GetRequiredService<ILogger<StateManager>>(),
                        sp.GetRequiredService<IRateLimiter>(),
                        statePath));

                // Execution Layer
                services.AddSingleton<ISettingsProvider, RegistrySettingsProvider>();
                services.AddSingleton<ISettingsProvider, NetShSettingsProvider>();
                services.AddSingleton<ISettingsProvider, WfpSettingsProvider>();
                
                services.AddSingleton<ISettingsExecutor, TransactionalSettingsExecutor>();

                // Orchestration Layer
                services.AddSingleton<IWatchdog, Watchdog>();
                services.AddSingleton<IOrchestrator>(sp =>
                    new Orchestrator(
                        sp.GetRequiredService<ILogger<Orchestrator>>(),
                        sp.GetRequiredService<IProcessMonitor>(),
                        sp.GetRequiredService<INetworkMonitor>(),
                        null, // DiagnosticsEngine - опционально
                        sp.GetRequiredService<IRuleEngine>(),
                        sp.GetRequiredService<IStateManager>(),
                        sp.GetRequiredService<ISettingsExecutor>(),
                        sp.GetRequiredService<IConfigManager>(),
                        sp.GetRequiredService<IWatchdog>(),
                        configPath,
                        statePath));

                // Host the orchestrator
                services.AddHostedService<ByeTcpHostedService>();
            });
}

/// <summary>
/// Hosted Service wrapper for Orchestrator
/// </summary>
public sealed class ByeTcpHostedService : IHostedService
{
    private readonly ILogger<ByeTcpHostedService> _logger;
    private readonly IOrchestrator _orchestrator;

    public ByeTcpHostedService(
        ILogger<ByeTcpHostedService> logger,
        IOrchestrator orchestrator)
    {
        _logger = logger;
        _orchestrator = orchestrator;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("🚀 Starting Bye-TCP Hosted Service");
        await _orchestrator.InitializeAsync(cancellationToken);
        _ = _orchestrator.RunAsync(cancellationToken);
    }

    public async Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("⏹️ Stopping Bye-TCP Hosted Service");
        await _orchestrator.ShutdownAsync(cancellationToken);
    }
}
