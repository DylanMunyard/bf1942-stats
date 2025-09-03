using junie_des_1942stats.PlayerStats;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats;
using junie_des_1942stats.StatsCollectors;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Interfaces;
using junie_des_1942stats.ClickHouse.Base;
using junie_des_1942stats.Caching;
using junie_des_1942stats.Services;
using Prometheus;
using Serilog;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using junie_des_1942stats.Services.Auth;
using System.Diagnostics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using junie_des_1942stats.Telemetry;
using OpenTelemetry.Exporter;

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

    // Configure OpenTelemetry
    builder.Services.AddOpenTelemetry()
        .ConfigureResource(resource => resource
            .AddService(serviceName: serviceName, serviceVersion: "1.0.0")
            .AddAttributes(new Dictionary<string, object>
            {
                ["deployment.environment"] = environment,
                ["host.name"] = Environment.MachineName
            }))

        .WithTracing(tracing =>
            {
                tracing.AddAspNetCoreInstrumentation(options =>
                {
                    // Don't trace health checks and metrics endpoints
                    options.Filter = httpContext =>
                    {
                        var path = httpContext.Request.Path.Value?.ToLower() ?? "";
                        return !path.Contains("/health") && !path.Contains("/metrics") && !path.Contains("/swagger");
                    };
                    options.RecordException = true;
                });
                tracing.AddHttpClientInstrumentation();
                tracing.AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForStoredProcedure = true;
                    options.SetDbStatementForText = true;
                });
                tracing.AddSqlClientInstrumentation(options =>
                {
                    options.SetDbStatementForStoredProcedure = true;
                    options.SetDbStatementForText = true;
                    options.RecordException = true;
                });
                tracing.AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri($"{seqUrl}/ingest/otlp/v1/traces");
                    opt.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
                tracing.AddSource("junie-des-1942stats.*");
                tracing.AddSource(ActivitySources.PlayerStats.Name);
                tracing.AddSource(ActivitySources.Database.Name);
                tracing.AddSource(ActivitySources.BfListApi.Name);
                tracing.AddSource(ActivitySources.Cache.Name);
                tracing.AddSource(ActivitySources.ClickHouse.Name);
                tracing.AddSource(ActivitySources.StatsCollection.Name);
            }
        );

    // Add services to the container
    builder.Services.AddControllers().AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
    });

    // Register OAuth services
    builder.Services.AddScoped<junie_des_1942stats.Services.OAuth.IGoogleAuthService, junie_des_1942stats.Services.OAuth.GoogleAuthService>();

    // CORS
    var allowedOrigin = builder.Configuration["Cors:AllowedOrigins"];
    builder.Services.AddCors(options =>
    {
        options.AddPolicy("default", policy =>
        {
            if (!string.IsNullOrEmpty(allowedOrigin))
            {
                policy.WithOrigins(allowedOrigin)
                      .AllowAnyHeader()
                      .AllowAnyMethod()
                      .WithExposedHeaders("WWW-Authenticate")
                      .AllowCredentials();
            }
            else
            {
                policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod();
            }
        });
    });

    // JWT Auth
    var issuer = builder.Configuration["Jwt:Issuer"] ?? "";
    var audience = builder.Configuration["Jwt:Audience"] ?? "";
    string? ReadConfigStringOrFile(string valueKey, string pathKey)
    {
        var v = builder.Configuration[valueKey];
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var p = builder.Configuration[pathKey];
        if (!string.IsNullOrWhiteSpace(p) && File.Exists(p)) return File.ReadAllText(p);
        return null;
    }

    var privateKeyPem = ReadConfigStringOrFile("Jwt:PrivateKey", "Jwt:PrivateKeyPath");

    builder.Services.AddAuthentication(options =>
    {
        options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
        options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidIssuer = issuer,
            ValidateAudience = true,
            ValidAudience = audience,
            ValidateIssuerSigningKey = true,
            ValidateLifetime = true,
            ClockSkew = TimeSpan.FromSeconds(30),
            IssuerSigningKey = CreateRsaKey(privateKeyPem ?? throw new InvalidOperationException("JWT private key not configured. Set Jwt:PrivateKey (inline PEM) or Jwt:PrivateKeyPath (file path)."))
        };
    });
    builder.Services.AddAuthorization();

    // DI for auth services
    builder.Services.AddScoped<ITokenService, TokenService>();
    builder.Services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen(c =>
    {
        c.SwaggerDoc("v1", new Microsoft.OpenApi.Models.OpenApiInfo
        {
            Title = "BF1942 Stats API",
            Version = "v1",
            Description = "API for Battlefield 1942 player and server statistics"
        });

        // Add JWT Authentication to Swagger
        c.AddSecurityDefinition("Bearer", new Microsoft.OpenApi.Models.OpenApiSecurityScheme
        {
            Description = "JWT Authorization header using the Bearer scheme. Enter 'Bearer' [space] and then your token in the text input below.",
            Name = "Authorization",
            In = Microsoft.OpenApi.Models.ParameterLocation.Header,
            Type = Microsoft.OpenApi.Models.SecuritySchemeType.ApiKey,
            Scheme = "Bearer"
        });

        c.AddSecurityRequirement(new Microsoft.OpenApi.Models.OpenApiSecurityRequirement
        {
            {
                new Microsoft.OpenApi.Models.OpenApiSecurityScheme
                {
                    Reference = new Microsoft.OpenApi.Models.OpenApiReference
                    {
                        Type = Microsoft.OpenApi.Models.ReferenceType.SecurityScheme,
                        Id = "Bearer"
                    }
                },
                Array.Empty<string>()
            }
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

    // Register bot detection service
    builder.Services.AddSingleton<IBotDetectionService, BotDetectionService>();
    
    // Register the player tracking service
    builder.Services.AddScoped<PlayerTrackingService>();
    builder.Services.AddScoped<RoundBackfillService>();
    builder.Services.AddScoped<PlayerStatsService>();

    // Register the ServerStatsService and supporting services
    builder.Services.AddScoped<ServerStatsService>();
    builder.Services.AddScoped<RoundsService>();

    // Register the stat collector background services
    builder.Services.AddHostedService<StatsCollectionBackgroundService>();
    builder.Services.AddHostedService<ClickHouseSyncBackgroundService>();
    builder.Services.AddHostedService<RankingCalculationService>();

    // Add HTTP server for Prometheus to scrape
    builder.Services.AddMetricServer(options =>
    {
        options.Port = 9091;
    });


    // Add ClickHouse HTTP clients with longer timeout for write operations
    builder.Services.AddHttpClient<PlayerMetricsWriteService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
        // Add ClickHouse authentication header
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerRoundsWriteService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(60);
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerMetricsMigrationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for migration operations
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

    builder.Services.AddHttpClient<PlayerInsightsService>(client =>
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

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerMetricsWriteService ClickHouse Write URL: {clickHouseWriteUrl}");

        return new PlayerMetricsWriteService(httpClient, clickHouseWriteUrl);
    });

    builder.Services.AddSingleton<PlayerRoundsWriteService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerRoundsWriteService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRoundsWriteService ClickHouse Write URL: {clickHouseWriteUrl}");

        var scopeFactory = sp.GetRequiredService<IServiceScopeFactory>();
        var logger = sp.GetRequiredService<ILogger<PlayerRoundsWriteService>>();
        return new PlayerRoundsWriteService(httpClient, clickHouseWriteUrl, scopeFactory, logger);
    });

    builder.Services.AddSingleton<PlayerMetricsMigrationService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerMetricsMigrationService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerMetricsMigrationService ClickHouse Write URL: {clickHouseWriteUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerMetricsMigrationService>>();
        return new PlayerMetricsMigrationService(httpClient, clickHouseWriteUrl, logger);
    });

    // Register ClickHouse Read Services (use CLICKHOUSE_URL)
    builder.Services.AddSingleton<PlayerRoundsReadService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerRoundsReadService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRoundsReadService ClickHouse Read URL: {clickHouseReadUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerRoundsReadService>>();
        return new PlayerRoundsReadService(httpClient, clickHouseReadUrl, logger, sp);
    });

    builder.Services.AddSingleton<PlayerInsightsService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerInsightsService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerInsightsService ClickHouse Read URL: {clickHouseReadUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerInsightsService>>();
        return new PlayerInsightsService(httpClient, clickHouseReadUrl, logger);
    });

    // Register RealTimeAnalyticsService (read-only)
    builder.Services.AddSingleton<RealTimeAnalyticsService>();

    // Register IClickHouseReader service
    builder.Services.AddScoped<IClickHouseReader>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        return new ClickHouseReader(httpClient, clickHouseReadUrl);
    });

    // Register ServerStatisticsService (read-only)
    builder.Services.AddSingleton<ServerStatisticsService>();


    // Configure Redis caching with short timeouts
    var redisConnectionString = Environment.GetEnvironmentVariable("REDIS_CONNECTION_STRING") ?? "42redis.home.net:6380";
    builder.Services.AddStackExchangeRedisCache(options =>
    {
        options.Configuration = $"{redisConnectionString},connectTimeout=1000,syncTimeout=1000,connectRetry=1,abortConnect=false";
        options.InstanceName = serviceName;
    });

    // Configure Redis for event publishing with graceful failure handling
    builder.Services.AddSingleton<IConnectionMultiplexer>(sp =>
    {
        var logger = sp.GetRequiredService<ILogger<Program>>();
        try
        {
            var connectionString = $"{redisConnectionString},abortConnect=false";
            var connection = ConnectionMultiplexer.Connect(connectionString);
            logger.LogInformation("Redis connection established successfully");
            return connection;
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Redis connection failed, continuing without Redis event publishing");
            // Return a null multiplexer that will be handled gracefully
            return null!;
        }
    });

    builder.Services.AddSingleton<IDatabase>(sp =>
    {
        var multiplexer = sp.GetRequiredService<IConnectionMultiplexer>();
        return multiplexer?.GetDatabase()!;
    });

    builder.Services.AddSingleton<IPlayerEventPublisher, PlayerEventPublisher>();

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

    // Register Gamification Services
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.BadgeDefinitionsService>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.AchievementLabelingService>();

    // Register HttpClient for ClickHouse Gamification Service
    builder.Services.AddHttpClient<junie_des_1942stats.Gamification.Services.ClickHouseGamificationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10); // Longer timeout for bulk operations
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    // Register ClickHouse Gamification Service as Singleton to match other ClickHouse services
    builder.Services.AddSingleton<junie_des_1942stats.Gamification.Services.ClickHouseGamificationService>();

    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.HistoricalProcessor>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.KillStreakDetector>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.MilestoneCalculator>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.PerformanceBadgeCalculator>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.PlacementProcessor>();
    builder.Services.AddScoped<junie_des_1942stats.Gamification.Services.GamificationService>();

    // Register Gamification Background Service
    builder.Services.AddHostedService<junie_des_1942stats.Gamification.Services.GamificationBackgroundService>();

    // Register PlayerComparisonService (read-only)
    builder.Services.AddScoped<PlayerComparisonService>(sp =>
    {
        // Use read URL for PlayerComparisonService
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerComparisonService ClickHouse Read URL: {clickHouseReadUrl}");

        var uri = new Uri(clickHouseReadUrl);
        var connectionString = $"Host={uri.Host};Port={uri.Port};Database=default;User=default;Password=;Protocol={uri.Scheme}";
        var connection = new ClickHouse.Client.ADO.ClickHouseConnection(connectionString);
        var logger = sp.GetRequiredService<ILogger<PlayerComparisonService>>();
        var dbContext = sp.GetRequiredService<PlayerTrackerDbContext>();
        var cacheService = sp.GetRequiredService<ICacheService>();
        var cacheKeyService = sp.GetRequiredService<ICacheKeyService>();
        var playerInsightsService = sp.GetRequiredService<PlayerInsightsService>();
        return new PlayerComparisonService(connection, logger, dbContext, cacheService, cacheKeyService, playerInsightsService);
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
    host.UseCors("default");
    host.UseAuthentication();
    host.UseAuthorization();

    static SecurityKey CreateRsaKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa);
    }
    host.MapControllers();

    // Ensure databases are created and migrated
    using (var scope = host.Services.CreateScope())
    {
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
        var playerMetricsService = scope.ServiceProvider.GetRequiredService<PlayerMetricsWriteService>();
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
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