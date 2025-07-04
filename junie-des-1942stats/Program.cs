using junie_des_1942stats.PlayerStats;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats;
using junie_des_1942stats.StatsCollectors;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Caching;
using Prometheus;
using Serilog;
using Microsoft.Extensions.Logging;

// Configure Serilog
var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://192.168.1.230:5341";
var serviceName = "junie-des-1942stats";
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    // Filter to suppress EF Core SQL logs only during bulk operations
    .Filter.ByExcluding(logEvent => 
    {
        // Suppress EF Core SQL logs when they're part of bulk operations
        if (logEvent.Properties.ContainsKey("bulk_operation") && 
            logEvent.Properties["bulk_operation"].ToString() == "True" &&
            logEvent.Properties.ContainsKey("SourceContext") &&
            (logEvent.Properties["SourceContext"].ToString().Contains("Microsoft.EntityFrameworkCore.Database.Command") ||
             logEvent.Properties["SourceContext"].ToString().Contains("Microsoft.EntityFrameworkCore.Infrastructure")))
        {
            return true; // Exclude this log
        }
        return false; // Include this log
    })
    // Keep controller logs at Information level
    .MinimumLevel.Override("junie_des_1942stats.PlayerStats.PlayersController", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("junie_des_1942stats.ServerStats.ServersController", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("junie_des_1942stats.RealTimeAnalyticsController", Serilog.Events.LogEventLevel.Information)
    .Enrich.WithProperty("service.name", serviceName)
    .Enrich.WithProperty("service.version", "1.0.0")
    .Enrich.WithProperty("deployment.environment", environment)
    .Enrich.WithProperty("host.name", Environment.MachineName)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentUserName()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.Seq(seqUrl)
    .CreateLogger();

try
{
    Log.Information("Starting up junie-des-1942stats application");

    var builder = WebApplication.CreateBuilder(args);

    // Add Serilog to the application
    builder.Host.UseSerilog();

    // Add services to the container
    builder.Services.AddControllers();
    
    // Add Swagger/OpenAPI
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo 
        { 
            Title = "BF1942 Stats API", 
            Version = "v1",
            Description = "API for Battlefield 1942 player and server statistics"
        });
        
        // Custom schema ID resolver to handle conflicting class names and generic types
        c.CustomSchemaIds(type => 
        {
            // Generate unique schema IDs using full namespace and type information
            if (type.IsGenericType)
            {
                // Handle generic types like PagedResult<T>
                var genericTypeName = type.Name.Split('`')[0];
                var genericArguments = type.GetGenericArguments()
                    .Select(arg => GetUniqueTypeName(arg))
                    .ToArray();
                var namespacePart = GetNamespacePart(type.Namespace);
                return $"{namespacePart}{genericTypeName}Of{string.Join("And", genericArguments)}";
            }
            
            // Handle regular types with potential namespace conflicts
            return GetUniqueTypeName(type);
        });
        
        // Helper function to get unique type name including namespace
        static string GetUniqueTypeName(Type type)
        {
            var namespacePart = GetNamespacePart(type.Namespace);
            return $"{namespacePart}{type.Name}";
        }
        
        // Helper function to get a short namespace identifier
        static string GetNamespacePart(string? typeNamespace)
        {
            if (string.IsNullOrEmpty(typeNamespace))
                return "";
                
            // Create short namespace identifiers
            if (typeNamespace.Contains("PlayerStats"))
                return "PlayerStats";
            if (typeNamespace.Contains("ServerStats"))
                return "ServerStats";
            if (typeNamespace.Contains("ClickHouse"))
                return "ClickHouse";
            if (typeNamespace.Contains("PlayerTracking"))
                return "PlayerTracking";
            if (typeNamespace.Contains("Prometheus"))
                return "Prometheus";
            if (typeNamespace.Contains("StatsCollectors"))
                return "StatsCollectors";
                
            // For other namespaces, use the last part
            var parts = typeNamespace.Split('.');
            return parts.Length > 0 ? parts[^1] : "";
        }
    });

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
        options.UseSqlite($"Data Source={dbPath}")
               .EnableSensitiveDataLogging(false)  // Disable sensitive data logging
               .LogTo(message => { }, LogLevel.Warning)); // Only log warnings and errors

    // Register the player tracking service
    builder.Services.AddScoped<PlayerTrackingService>();
    builder.Services.AddScoped<PlayerStatsService>();

    // Register the ServerStatsService
    builder.Services.AddScoped<ServerStatsService>();
    
    // Register the HistoricalRoundsService
    builder.Services.AddScoped<HistoricalRoundsService>();

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

    // Add ClickHouse HTTP clients with 2 second timeout
    builder.Services.AddHttpClient<PlayerMetricsWriteService>(client => 
    {
        client.Timeout = TimeSpan.FromSeconds(2);
        // Add ClickHouse authentication header
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerRoundsWriteService>(client => 
    {
        client.Timeout = TimeSpan.FromSeconds(2);
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerRoundsReadService>(client => 
    {
        client.Timeout = TimeSpan.FromSeconds(2);
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    // Register ClickHouse Write Services (use CLICKHOUSE_WRITE_URL)
    builder.Services.AddSingleton<PlayerMetricsWriteService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerMetricsWriteService));
        
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerMetricsWriteService ClickHouse Write URL: {clickHouseWriteUrl}");
        
        return new PlayerMetricsWriteService(httpClient, clickHouseWriteUrl);
    });

    builder.Services.AddSingleton<PlayerRoundsWriteService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerRoundsWriteService));
        
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRoundsWriteService ClickHouse Write URL: {clickHouseWriteUrl}");
        
        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var logger = sp.GetRequiredService<ILogger<PlayerRoundsWriteService>>();
        return new PlayerRoundsWriteService(httpClient, clickHouseWriteUrl, scopeFactory, logger);
    });

    // Register ClickHouse Read Services (use CLICKHOUSE_URL)
    builder.Services.AddSingleton<PlayerRoundsReadService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerRoundsReadService));
        
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRoundsReadService ClickHouse Read URL: {clickHouseReadUrl}");
        
        var logger = sp.GetRequiredService<ILogger<PlayerRoundsReadService>>();
        return new PlayerRoundsReadService(httpClient, clickHouseReadUrl, logger);
    });

    // Register RealTimeAnalyticsService (read-only)
    builder.Services.AddSingleton<RealTimeAnalyticsService>();

    // Register ServerStatisticsService (read-only)
    builder.Services.AddSingleton<ServerStatisticsService>();


    // Configure Redis caching with short timeouts
    var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "42redis.home.net:6379";
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = $"{redisConnectionString},connectTimeout=1000,syncTimeout=1000,connectRetry=1,abortConnect=false";
        options.InstanceName = serviceName;
    });

    // Register caching services
    builder.Services.AddScoped<ICacheService, CacheService>();
    builder.Services.AddScoped<ICacheKeyService, CacheKeyService>();

    // Register BFList API service with configured HTTP client and resilience
    builder.Services.AddHttpClient("BfListApi", client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("User-Agent", "bf1942-stats/1.0");
    })
    .AddStandardResilienceHandler(options =>
    {
        // Configure retry policy
        options.Retry.MaxRetryAttempts = 3;
        options.Retry.BackoffType = Polly.DelayBackoffType.Exponential;
        options.Retry.Delay = TimeSpan.FromSeconds(1);
        options.Retry.MaxDelay = TimeSpan.FromSeconds(10);
        
        // Configure timeout policy
        options.AttemptTimeout.Timeout = TimeSpan.FromSeconds(8);
        options.TotalRequestTimeout.Timeout = TimeSpan.FromSeconds(30);
        
        // Configure circuit breaker
        options.CircuitBreaker.SamplingDuration = TimeSpan.FromSeconds(30);
        options.CircuitBreaker.FailureRatio = 0.5; // 50% failure rate
        options.CircuitBreaker.MinimumThroughput = 5;
        options.CircuitBreaker.BreakDuration = TimeSpan.FromSeconds(15);
    });
    
    builder.Services.AddScoped<junie_des_1942stats.Services.IBfListApiService, junie_des_1942stats.Services.BfListApiService>();

    // Register PlayerComparisonService (read-only)
    builder.Services.AddScoped<PlayerComparisonService>(sp =>
    {
        // Use read URL for PlayerComparisonService
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerComparisonService ClickHouse Read URL: {clickHouseReadUrl}");
        
        var uri = new Uri(clickHouseReadUrl);
        var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
        var connection = new ClickHouse.Client.ADO.ClickHouseConnection(connectionString);
        var logger = sp.GetRequiredService<ILogger<PlayerComparisonService>>();
        var dbContext = sp.GetRequiredService<PlayerTrackerDbContext>();
        var cacheService = sp.GetRequiredService<ICacheService>();
        var cacheKeyService = sp.GetRequiredService<ICacheKeyService>();
        return new PlayerComparisonService(connection, logger, dbContext, cacheService, cacheKeyService);
    });

    var host = builder.Build();

    // Configure the HTTP request pipeline
    if (host.Environment.IsDevelopment())
    {
        host.UseSwagger();
        host.UseSwaggerUI(c =>
        {
            c.SwaggerEndpoint("/swagger/v1/swagger.json", "BF1942 Stats API v1");
            c.RoutePrefix = "swagger";
        });
    }

    // Enable routing and controllers
    host.UseRouting();
    host.MapControllers();

    // Ensure databases are created and migrated
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
        var playerMetricsService = scope.ServiceProvider.GetRequiredService<PlayerMetricsWriteService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();
        
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        var isWriteUrlSet = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") != null;
        
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

        // Only attempt ClickHouse schema creation if write URL is properly configured or in development
        if (isWriteUrlSet || host.Environment.IsDevelopment())
        {
            try
            {
                // Ensure ClickHouse schema is created (using write service)
                await playerMetricsService.EnsureSchemaAsync();
                logger.LogInformation("ClickHouse schema created successfully at: {WriteUrl}", clickHouseWriteUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create ClickHouse schema at: {WriteUrl}. This is normal in dev environments with read-only access", clickHouseWriteUrl);
            }

            try
            {
                // Ensure ClickHouse player_rounds schema is created (using write service)
                var playerRoundsService = scope.ServiceProvider.GetRequiredService<PlayerRoundsWriteService>();
                await playerRoundsService.EnsureSchemaAsync();
                logger.LogInformation("ClickHouse player_rounds schema created successfully at: {WriteUrl}", clickHouseWriteUrl);

                // Migrate player_rounds table to ReplacingMergeTree engine
                await playerRoundsService.MigrateToReplacingMergeTreeAsync();
                logger.LogInformation("ClickHouse player_rounds table migrated to ReplacingMergeTree engine at: {WriteUrl}", clickHouseWriteUrl);
            }
            catch (Exception ex)
            {
                logger.LogWarning(ex, "Could not create ClickHouse player_rounds schema at: {WriteUrl}. This is normal in dev environments with read-only access", clickHouseWriteUrl);
            }
        }
        else
        {
            logger.LogInformation("Skipping ClickHouse schema creation - no write URL configured and not in development environment");
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