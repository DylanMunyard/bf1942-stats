using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using api.PlayerTracking;
using api.StatsCollectors;
using api.ClickHouse;
using api.ClickHouse.Interfaces;
using api.ClickHouse.Base;
using api.Caching;
using api.Services;
using api.Gamification.Services;
using Serilog;
using Serilog.Enrichers.Span;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using System.Security.Cryptography;
using System.Text.Json;
using api.Auth;
using api.Utils;
using NodaTime;
using NodaTime.Serialization.SystemTextJson;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Metrics;
using api.Telemetry;
using OpenTelemetry.Exporter;
using Serilog.Sinks.Grafana.Loki;
using Microsoft.AspNetCore.ResponseCompression;
using System.IO.Compression;
using api.Data.Migrations;
using api.Players;
using api.Servers;

// Configure Serilog
var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://localhost:3100";
var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL");
var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://localhost:4318/v1/traces";
var serviceName = "junie-des-1942stats";
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

var loggerConfig = new LoggerConfiguration()
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
    .MinimumLevel.Override("api.PlayerStats.PlayersController", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("api.ServerStats.ServersController", Serilog.Events.LogEventLevel.Information)
    .MinimumLevel.Override("api.RealTimeAnalyticsController", Serilog.Events.LogEventLevel.Information)
    // Suppress verbose HTTP client logs from the OTLP trace exporter
    .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpTraceExporter.ClientHandler", Serilog.Events.LogEventLevel.Warning)
    .Enrich.WithProperty("service.name", serviceName)
    .Enrich.WithProperty("service.version", "1.0.0")
    .Enrich.WithProperty("deployment.environment", environment)
    .Enrich.WithProperty("host.name", Environment.MachineName)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithSpan()
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}")
    .WriteTo.GrafanaLoki(lokiUrl,
        labels: new[]
        {
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "service", Value = serviceName },
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "environment", Value = environment },
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "host", Value = Environment.MachineName }
        },
        textFormatter: new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());

if (!string.IsNullOrEmpty(seqUrl))
{
    loggerConfig.WriteTo.Seq(seqUrl);
}

Log.Logger = loggerConfig.CreateLogger();

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
        .WithMetrics(metrics =>
            {
                // Add built-in ASP.NET Core meters
                metrics.AddMeter("Microsoft.AspNetCore.Hosting");
                metrics.AddMeter("Microsoft.AspNetCore.Server.Kestrel");
                metrics.AddMeter("System.Net.Http");

                // Add runtime instrumentation for GC, thread pool, etc.
                metrics.AddRuntimeInstrumentation();

                metrics.AddPrometheusExporter();
            })
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
                tracing.AddHttpClientInstrumentation(options =>
                {
                    // Only trace HTTP calls from API requests, not background services
                    options.FilterHttpRequestMessage = (httpRequestMessage) =>
                    {
                        var activity = System.Diagnostics.Activity.Current;
                        while (activity != null)
                        {
                            // Skip tracing if we're in a background service operation
                            if (activity.Tags.Any(tag =>
                                (tag.Key == "bulk_operation" && tag.Value == "true") ||
                                tag.Key == "ClickHouseSync.Cycle" ||
                                tag.Key == "StatsCollection.Cycle" ||
                                tag.Key == "Gamification.Processing"))
                            {
                                return false;
                            }
                            activity = activity.Parent;
                        }
                        return true;
                    };
                });
                tracing.AddEntityFrameworkCoreInstrumentation(options =>
                {
                    options.SetDbStatementForStoredProcedure = true;
                    options.SetDbStatementForText = true;
                    // Filter out database commands during bulk operations
                    options.Filter = (connectionString, command) =>
                    {
                        // Check if we're in a bulk operation context by looking at current activity
                        var activity = System.Diagnostics.Activity.Current;
                        while (activity != null)
                        {
                            if (activity.Tags.Any(tag => tag.Key == "bulk_operation" && tag.Value == "true"))
                            {
                                return false; // Don't trace this command
                            }
                            activity = activity.Parent;
                        }

                        return true; // Trace this command
                    };
                });
                // SqlClient instrumentation removed to reduce telemetry overhead
                // EF Core instrumentation above already covers most database operations
                tracing.AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri(otlpEndpoint);
                    opt.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
                // Only trace API-related activity sources, exclude background service sources
                tracing.AddSource("junie-des-1942stats.*");
                tracing.AddSource(ActivitySources.PlayerStats.Name);
                tracing.AddSource(ActivitySources.Database.Name);
                tracing.AddSource(ActivitySources.BfListApi.Name);
                tracing.AddSource(ActivitySources.Cache.Name);
                tracing.AddSource(ActivitySources.ClickHouse.Name);
                // Background service sources commented out to reduce telemetry overhead:
                // tracing.AddSource(ActivitySources.StatsCollection.Name);
                // tracing.AddSource(ActivitySources.ClickHouseSync.Name);
                // tracing.AddSource(ActivitySources.Gamification.Name);
            }
        );

    // Add services to the container
    builder.Services.AddControllers().AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.ConfigureForNodaTime(DateTimeZoneProviders.Tzdb);
    });

    // Configure response compression for bandwidth optimization
    builder.Services.AddResponseCompression(options =>
    {
        options.EnableForHttps = true;
        options.Providers.Add<BrotliCompressionProvider>();
        options.Providers.Add<GzipCompressionProvider>();
    });

    builder.Services.Configure<BrotliCompressionProviderOptions>(options =>
    {
        // Balanced compression level (fast enough for 2 CPU deployment)
        options.Level = CompressionLevel.Fastest;
    });

    builder.Services.Configure<GzipCompressionProviderOptions>(options =>
    {
        // Optimal compression for speed/size trade-off
        options.Level = CompressionLevel.Optimal;
    });

    // Register Auth services
    builder.Services.AddScoped<api.Auth.IDiscordAuthService, api.Auth.DiscordAuthService>();

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
    builder.Services.AddScoped<IPlayerStatsService>(sp => sp.GetRequiredService<PlayerStatsService>());

    // Register markdown sanitization service for tournament rules
    builder.Services.AddScoped<IMarkdownSanitizationService, MarkdownSanitizationService>();

    // Register tournament leaderboard services
    builder.Services.AddScoped<ITeamMappingService, TeamMappingService>();
    builder.Services.AddScoped<ITeamRankingCalculator, TeamRankingCalculator>();
    builder.Services.AddScoped<ITournamentMatchResultService, TournamentMatchResultService>();

    // Register the ServerStatsService and supporting services
    builder.Services.AddScoped<ServerStatsService>();
    builder.Services.AddScoped<IServerStatsService>(sp => sp.GetRequiredService<ServerStatsService>());
    builder.Services.AddScoped<RoundsService>();
    builder.Services.AddScoped<PlayersOnlineHistoryService>();

    // Register the stat collector background services
    builder.Services.AddHostedService<StatsCollectionBackgroundService>();
    builder.Services.AddHostedService<ClickHouseSyncBackgroundService>();
    builder.Services.AddHostedService<RankingCalculationService>();


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

    builder.Services.AddHttpClient<PlayerRoundsMigrationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for migration operations
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerAchievementsGameMigrationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for migration operations
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerMetricsGameMigrationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(300); // 5 minutes for migration operations
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    builder.Services.AddHttpClient<PlayerAchievementsMigrationService>(client =>
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

    builder.Services.AddSingleton<PlayerAchievementsMigrationService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerAchievementsMigrationService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerAchievementsMigrationService ClickHouse Write URL: {clickHouseWriteUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerAchievementsMigrationService>>();
        return new PlayerAchievementsMigrationService(httpClient, clickHouseWriteUrl, logger);
    });

    builder.Services.AddSingleton<PlayerRoundsMigrationService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerRoundsMigrationService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRoundsMigrationService ClickHouse Write URL: {clickHouseWriteUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerRoundsMigrationService>>();
        return new PlayerRoundsMigrationService(httpClient, clickHouseWriteUrl, logger);
    });

    builder.Services.AddSingleton<PlayerAchievementsGameMigrationService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerAchievementsGameMigrationService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerAchievementsGameMigrationService ClickHouse Write URL: {clickHouseWriteUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerAchievementsGameMigrationService>>();
        return new PlayerAchievementsGameMigrationService(httpClient, clickHouseWriteUrl, logger);
    });

    builder.Services.AddSingleton<PlayerMetricsGameMigrationService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(PlayerMetricsGameMigrationService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerMetricsGameMigrationService ClickHouse Write URL: {clickHouseWriteUrl}");

        var logger = sp.GetRequiredService<ILogger<PlayerMetricsGameMigrationService>>();
        return new PlayerMetricsGameMigrationService(httpClient, clickHouseWriteUrl, logger);
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
    builder.Services.AddSingleton<IPlayerInsightsService>(sp => sp.GetRequiredService<PlayerInsightsService>());

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
    builder.Services.AddSingleton<IServerStatisticsService>(sp => sp.GetRequiredService<ServerStatisticsService>());

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

    builder.Services.AddScoped<api.Bflist.IBfListApiService, api.Bflist.BfListApiService>();

    // Register Gamification Services
    builder.Services.AddScoped<api.Gamification.Services.BadgeDefinitionsService>();
    builder.Services.AddScoped<api.Gamification.Services.IBadgeDefinitionsService, api.Gamification.Services.BadgeDefinitionsService>();
    builder.Services.AddScoped<api.Gamification.Services.AchievementLabelingService>();

    // Register HttpClient for ClickHouse Gamification Service
    builder.Services.AddHttpClient<api.Gamification.Services.ClickHouseGamificationService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10); // Longer timeout for bulk operations
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    // Register ClickHouse Gamification Service as Singleton to match other ClickHouse services
    builder.Services.AddSingleton<api.Gamification.Services.ClickHouseGamificationService>();

    builder.Services.AddScoped<api.Gamification.Services.HistoricalProcessor>();
    builder.Services.AddScoped<api.Gamification.Services.KillStreakDetector>();
    builder.Services.AddScoped<api.Gamification.Services.MilestoneCalculator>();
    builder.Services.AddScoped<api.Gamification.Services.PerformanceBadgeCalculator>();
    builder.Services.AddScoped<api.Gamification.Services.PlacementProcessor>();
    builder.Services.AddScoped<api.Gamification.Services.TeamVictoryProcessor>();
    builder.Services.AddScoped<api.Gamification.Services.GamificationService>();

    // Register Gamification Background Service
    builder.Services.AddHostedService<api.Gamification.Services.GamificationBackgroundService>();

    // Register ImageStorage Services
    builder.Services.AddScoped<api.ImageStorage.IImageIndexingService, api.ImageStorage.ImageIndexingService>();
    builder.Services.AddScoped<api.ImageStorage.IAssetServingService, api.ImageStorage.AssetServingService>();

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
    builder.Services.AddScoped<IPlayerComparisonService>(sp => sp.GetRequiredService<PlayerComparisonService>());

    // Add HttpClient for GameTrendsService
    builder.Services.AddHttpClient<GameTrendsService>(client =>
    {
        client.Timeout = TimeSpan.FromSeconds(10);
        client.DefaultRequestHeaders.Add("X-ClickHouse-User", "default");
    })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

    // Register GameTrendsService (read-only for trend analysis)
    builder.Services.AddScoped<GameTrendsService>(sp =>
    {
        var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient(nameof(GameTrendsService));

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] GameTrendsService ClickHouse Read URL: {clickHouseReadUrl}");

        var logger = sp.GetRequiredService<ILogger<GameTrendsService>>();
        var dbContext = sp.GetRequiredService<PlayerTrackerDbContext>();
        return new GameTrendsService(httpClient, clickHouseReadUrl, logger, dbContext);
    });
    builder.Services.AddScoped<IGameTrendsService>(sp => sp.GetRequiredService<GameTrendsService>());

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

    // Enable response compression (must be early in pipeline)
    host.UseResponseCompression();

    // Enable routing and controllers
    host.UseRouting();
    host.UseCors("default");
    host.UseAuthentication();
    host.UseAuthorization();

    // Serve tournament map images - configurable path via environment variable
    try
    {
        var imagePath = api.ImageStorage.TournamentImagesConfig.ResolveTournamentsPath();

        if (Directory.Exists(imagePath))
        {
            var fileProvider = new Microsoft.Extensions.FileProviders.PhysicalFileProvider(imagePath);
            var options = new StaticFileOptions
            {
                FileProvider = fileProvider,
                RequestPath = "/images/tournament-maps"
            };
            host.UseStaticFiles(options);
            host.Logger.LogInformation("Tournament images serving enabled at {ImagePath}", imagePath);
        }
        else
        {
            host.Logger.LogWarning("Tournament images directory not found at {ImagePath}. Static file serving disabled.", imagePath);
        }
    }
    catch (Exception ex)
    {
        host.Logger.LogWarning(ex, "Failed to initialize tournament image serving. This feature will be disabled.");
    }

    static SecurityKey CreateRsaKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new Microsoft.IdentityModel.Tokens.RsaSecurityKey(rsa);
    }
    host.MapControllers();
    host.MapPrometheusScrapingEndpoint();

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
