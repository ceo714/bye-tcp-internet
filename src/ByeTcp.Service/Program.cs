using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
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
    public static async Task<int> Main(string[] args)
    {
        // 1) Загрузка конфигурации до создания host (нужно чтобы logger имел настройки из json)
        var configuration = new ConfigurationBuilder()
            .SetBasePath(AppContext.BaseDirectory)
            .AddJsonFile("appsettings.json", optional: false, reloadOnChange: true)
            .AddEnvironmentVariables()
            .Build();

        // 2) Инициализация Serilog на основе конфигурации
        var logsPathRaw = configuration["Service:LogsPath"] ?? "%PROGRAMDATA%\\ByeTcp\\logs";
        var logsPath = Environment.ExpandEnvironmentVariables(logsPathRaw);
        Directory.CreateDirectory(logsPath);

        var logFileNamePattern = configuration["Service:LogFileNamePattern"] ?? "bye-tcp-.log";
        var retainedFileCountLimit = int.Parse(configuration["Service:RetainedFileCountLimit"] ?? "7");

        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(ParseLogLevel(configuration["Serilog:MinimumLevel:Default"], LogEventLevel.Debug))
            .MinimumLevel.Override("Microsoft", ParseLogLevel(configuration["Serilog:MinimumLevel:Override:Microsoft"], LogEventLevel.Information))
            .MinimumLevel.Override("System", ParseLogLevel(configuration["Serilog:MinimumLevel:Override:System"], LogEventLevel.Warning))
            .Enrich.FromLogContext()
            .Enrich.WithThreadId()
            .Enrich.WithMachineName()
            .Enrich.WithEnvironmentUserName()
            .WriteTo.Console(
                outputTemplate: "{Timestamp:HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] (component={Component}) (event_id={EventId}) {Message:lj}{NewLine}{Exception}"
            )
            .WriteTo.File(
                path: Path.Combine(logsPath, logFileNamePattern),
                rollingInterval: RollingInterval.Day,
                retainedFileCountLimit: retainedFileCountLimit,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] [{SourceContext}] (component={Component}) (event_id={EventId}) {Message:lj}{NewLine}{Exception}"
            )
            .CreateLogger();

        // 3) Глобальные обработчики необработанных исключений
        AppDomain.CurrentDomain.UnhandledException += (s, e) =>
        {
            try
            {
                Log.Fatal(e.ExceptionObject as Exception, "Unhandled exception (AppDomain)");
            }
            catch { }
        };

        TaskScheduler.UnobservedTaskException += (s, e) =>
        {
            try
            {
                Log.Error(e.Exception, "Unobserved task exception");
                e.SetObserved();
            }
            catch { }
        };

        try
        {
            Log.Information("╔═══════════════════════════════════════════════════════════╗");
            Log.Information("║        Bye-TCP Internet Service v2.0                      ║");
            Log.Information("║  Layered Architecture - ETW Monitoring                    ║");
            Log.Information("╚═══════════════════════════════════════════════════════════╝");
            Log.Information("Starting host for Bye-TCP Service...");

            // 4) Создаём Host Builder и настраиваем сервисы
            var hostBuilder = Host.CreateDefaultBuilder(args)
                .UseSerilog() // Используем уже инициализированный Serilog
                .UseWindowsService(options =>
                {
                    options.ServiceName = configuration["Service:ServiceName"] ?? "ByeTcp";
                })
                .ConfigureAppConfiguration((ctx, cfg) =>
                {
                    // Повторно добавляем appsettings.json чтобы host тоже видел конфигурацию и поддерживал reloadOnChange
                    cfg.AddConfiguration(configuration);
                })
                .ConfigureServices((context, services) =>
                {
                    // Привязка опций сервисов
                    services.Configure<ServiceOptions>(configuration.GetSection("Service"));

                    // Infrastructure Layer
                    var configPath = configuration["Service:ConfigPath"] ?? "config";
                    var statePath = configuration["Service:StatePath"] ?? "state";
                    var cachePath = configuration["Service:CachePath"] ?? "cache";
                    var schemasPath = configuration["Service:SchemasPath"] ?? "schemas";

                    var fullConfigPath = Path.Combine(AppContext.BaseDirectory, configPath);
                    var fullStatePath = Path.Combine(AppContext.BaseDirectory, statePath);
                    var fullCachePath = Path.Combine(AppContext.BaseDirectory, cachePath);
                    var fullSchemasPath = Path.Combine(AppContext.BaseDirectory, schemasPath);

                    services.AddSingleton<IConfigManager>(sp =>
                        new VersionedConfigManager(
                            sp.GetRequiredService<ILogger<VersionedConfigManager>>(),
                            fullSchemasPath));

                    services.AddSingleton<IStateCache>(sp =>
                        new InMemoryStateCache(
                            sp.GetRequiredService<ILogger<InMemoryStateCache>>(),
                            fullCachePath));

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
                            fullStatePath));

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
                            fullConfigPath,
                            fullStatePath));

                    // Host the orchestrator
                    services.AddHostedService<ByeTcpHostedService>();
                });

            using var cts = new CancellationTokenSource();
            Console.CancelKeyPress += (s, e) =>
            {
                e.Cancel = true;
                Log.Information("Console Cancel pressed - initiating shutdown");
                cts.Cancel();
            };

            var host = hostBuilder.Build();

            // 5) Запускаем host асинхронно и передаём cancellation token для graceful shutdown
            await host.RunAsync(cts.Token).ConfigureAwait(false);

            Log.Information("Host stopped gracefully");
            return 0;
        }
        catch (Exception ex)
        {
            Log.Fatal(ex, "Host terminated unexpectedly");
            return 1;
        }
        finally
        {
            Log.Information("Service stopped");
            Log.CloseAndFlush();
        }
    }

    private static LogEventLevel ParseLogLevel(string? value, LogEventLevel @default)
    {
        if (string.IsNullOrWhiteSpace(value)) return @default;
        return value.Trim().ToLower() switch
        {
            "verbose" => LogEventLevel.Verbose,
            "debug" => LogEventLevel.Debug,
            "information" or "info" => LogEventLevel.Information,
            "warning" or "warn" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => @default
        };
    }
}

/// <summary>
/// Hosted Service wrapper for Orchestrator
/// </summary>
public sealed class ByeTcpHostedService : IHostedService
{
    private readonly ILogger<ByeTcpHostedService> _logger;
    private readonly IOrchestrator _orchestrator;
    private readonly ServiceOptions _options;

    public ByeTcpHostedService(
        ILogger<ByeTcpHostedService> logger,
        IOrchestrator orchestrator,
        Microsoft.Extensions.Options.IOptions<ServiceOptions> options)
    {
        _logger = logger;
        _orchestrator = orchestrator;
        _options = options.Value;
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
        
        using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeoutCts.CancelAfter(TimeSpan.FromSeconds(_options.GracefulShutdownTimeoutSeconds));
        
        try
        {
            await _orchestrator.ShutdownAsync(timeoutCts.Token);
            _logger.LogInformation("✅ Bye-TCP Hosted Service stopped");
        }
        catch (OperationCanceledException)
        {
            _logger.LogWarning("⚠️ Shutdown timed out");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during shutdown");
        }
    }
}

/// <summary>
/// Service options model
/// </summary>
public class ServiceOptions
{
    public string LogsPath { get; set; } = "logs";
    public string ConfigPath { get; set; } = "config";
    public string StatePath { get; set; } = "state";
    public string CachePath { get; set; } = "cache";
    public string SchemasPath { get; set; } = "schemas";
    public string ServiceName { get; set; } = "ByeTcp";
    public int GracefulShutdownTimeoutSeconds { get; set; } = 30;
    public int RetryAttempts { get; set; } = 3;
    public int RetryDelaySeconds { get; set; } = 2;
}
