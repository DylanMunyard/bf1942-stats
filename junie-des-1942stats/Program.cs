using junie_des_1942stats.PlayerStats;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats;
using junie_des_1942stats.StatsCollectors;
using junie_des_1942stats.ClickHouse;
using Prometheus;
using Serilog;
using Microsoft.Extensions.Logging;
using OpenTelemetry;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Logs;
using Serilog.Sinks.Loki;

// Configure Serilog
var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://192.168.1.230:3100";
var tempoUrl = Environment.GetEnvironmentVariable("TEMPO_URL") ?? "http://192.168.1.230:4317";

Log.Logger = new LoggerConfiguration()
    .Enrich.WithProperty("app", "junie-des-1942stats")
    .Enrich.WithProperty("environment", Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development")
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .WriteTo.LokiHttp(() => new LokiSinkConfiguration { LokiUrl = lokiUrl })
    .CreateLogger();

try
{
    Log.Information("Starting up junie-des-1942stats application");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog to the application
    builder.Host.UseSerilog();

    // Add OpenTelemetry tracing
    builder.Services.AddOpenTelemetry()
        .WithTracing(tracerProviderBuilder =>
            tracerProviderBuilder
                .AddSource("junie-des-1942stats")
                .SetResourceBuilder(ResourceBuilder.CreateDefault()
                    .AddService("junie-des-1942stats", serviceVersion: "1.0.0")
                    .AddAttributes(new Dictionary<string, object>
                    {
                        ["deployment.environment"] = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development"
                    }))
                .AddOtlpExporter(opts => 
                {
                    opts.Endpoint = new Uri(tempoUrl);
                    opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
                })
                .AddAspNetCoreInstrumentation()
                .AddHttpClientInstrumentation()
                .AddSqlClientInstrumentation());

    // Add OpenTelemetry logging
    builder.Logging.AddOpenTelemetry(options =>
    {
        options.AddOtlpExporter(opts => 
        {
            opts.Endpoint = new Uri(tempoUrl);
            opts.Protocol = OpenTelemetry.Exporter.OtlpExportProtocol.Grpc;
        });
    });

    // Add services to the container
    builder.Services.AddControllers();

    // Configure SQLite database path - check for environment variable first
    string dbPath;
    var envDbPath = Environment.GetEnvironmentVariable("DB_PATH");

    if (!string.IsNullOrEmpty(envDbPath))
    {
        // Use the environment variable path if it exists
        dbPath = envDbPath;
        Log.Information("Using database path from environment variable: {DbPath}", dbPath);
    }
    else
    {
        // Default to current directory
        dbPath = Path.Combine(Directory.GetCurrentDirectory(), "playertracker.db");
        Log.Information("Using default database path: {DbPath}", dbPath);
    }

    // Configure SQLite
    builder.Services.AddDbContext<PlayerTrackerDbContext>(options =>
        options.UseSqlite($"Data Source={dbPath}"));

    // Register the player tracking service
    builder.Services.AddScoped<PlayerTrackingService>();
    builder.Services.AddScoped<PlayerStatsService>();

    // Register the ServerStatsService
    builder.Services.AddScoped<ServerStatsService>();

    // Register the stat collector background services
    builder.Services.AddHostedService<StatsCollectionBackgroundService>();
    builder.Services.AddHostedService<RankingCalculationService>();

    // Add HTTP server for Prometheus to scrape
    builder.Services.AddMetricServer(options =>
    {
        options.Port = 9091;
    });

    // Add Prometheus service with 5 second timeout
    builder.Services.AddHttpClient<PrometheusService>(client => 
    {
        client.Timeout = TimeSpan.FromSeconds(2);
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddSingleton<PrometheusService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(5);
        var prometheusUrl = Environment.GetEnvironmentVariable("PROMETHEUS_URL") ?? "http://prometheus.home.net/api/v1";
        return new PrometheusService(httpClient, prometheusUrl);
    });

    // Add ClickHouse service with 2 second timeout
    builder.Services.AddHttpClient<PlayerMetricsService>(client => 
    {
        client.Timeout = TimeSpan.FromSeconds(2);
        // Add ClickHouse authentication header
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddSingleton<PlayerMetricsService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        httpClient.Timeout = TimeSpan.FromSeconds(2);
        var clickHouseUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        return new PlayerMetricsService(httpClient, clickHouseUrl);
    });

    // Register RealTimeAnalyticsService
    builder.Services.AddSingleton<RealTimeAnalyticsService>();

    var host = builder.Build();

    // Enable routing and controllers
    host.UseRouting();
    host.MapControllers();

    // Ensure databases are created and migrated
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
        var playerMetricsService = scope.ServiceProvider.GetRequiredService<PlayerMetricsService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        try
        {
            // Apply EF Core migrations for SQLite
            dbContext.Database.Migrate();
            logger.LogInformation("SQLite database migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying SQLite migrations");
        }

        try
        {
            // Ensure ClickHouse schema is created
            await playerMetricsService.EnsureSchemaAsync();
            logger.LogInformation("ClickHouse schema created successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while creating ClickHouse schema");
        }
    }

    Log.Information("Application started successfully");
    await host.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}