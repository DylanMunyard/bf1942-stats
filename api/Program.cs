using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using api.PlayerTracking;
using api.StatsCollectors;
using api.Caching;
using api.Services;
using api.Gamification.Services;
using api.Services.BackgroundJobs;
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
    // Suppress verbose HTTP client logs from the OTLP trace exporter and buddy services
    .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpTraceExporter.LogicalHandler", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient.OtlpTraceExporter.ClientHandler", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("System.Net.Http.HttpClient.IBuddyApiService.ClientHandler", Serilog.Events.LogEventLevel.Warning)
    // Filter out OTLP trace export HTTP requests
    .Filter.ByExcluding(logEvent =>
    {
        if (logEvent.MessageTemplate.Text?.Contains("Sending HTTP request") == true &&
            logEvent.MessageTemplate.Text?.Contains("http://tempo.monitoring:4318/v1/traces") == true)
        {
            return true; // Exclude this log
        }
        return false; // Include this log
    })
    .Enrich.WithProperty("service.name", serviceName)
    .Enrich.WithProperty("service.version", "1.0.0")
    .Enrich.WithProperty("deployment.environment", environment)
    .Enrich.WithProperty("host.name", Environment.MachineName)
    .Enrich.FromLogContext()
    .Enrich.WithMachineName()
    .Enrich.WithEnvironmentUserName()
    .Enrich.WithSpan()
    .Enrich.With<UserAgentEnricher>()
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

                // Add custom background job meters for correlation analysis
                metrics.AddMeter("junie-des-1942stats.BackgroundJobs");

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
                // Background service sources commented out to reduce telemetry overhead:
                // tracing.AddSource(ActivitySources.StatsCollection.Name);
                tracing.AddSource(ActivitySources.RankingCalculation.Name);
                // tracing.AddSource(ActivitySources.Gamification.Name);
            }
        );

    // Add services to the container
    builder.Services.AddControllers().AddJsonOptions(o =>
    {
        o.JsonSerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
        o.JsonSerializerOptions.PropertyNameCaseInsensitive = true;
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
    builder.Services.AddAuthorization(options =>
    {
        options.AddPolicy("Admin", policy =>
            policy.RequireClaim(System.Security.Claims.ClaimTypes.Email, "dmunyard@gmail.com"));
    });

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

    // Configure SQLite with proper connection settings to prevent locking issues
    // - busy_timeout: Wait up to 5 seconds for locks instead of failing immediately (via interceptor)
    // - journal_mode: WAL provides better concurrent read/write performance
    var connectionString = $"Data Source={dbPath}";

    // Register the connection interceptor as a singleton so it can be injected into DbContext
    builder.Services.AddSingleton<SqliteConnectionInterceptor>();

    builder.Services.AddDbContext<PlayerTrackerDbContext>((serviceProvider, options) =>
    {
        var interceptor = serviceProvider.GetRequiredService<SqliteConnectionInterceptor>();
        
        options.UseSqlite(connectionString, sqliteOptions =>
        {
            sqliteOptions.CommandTimeout(60); // 60 second command timeout
        })
        .AddInterceptors(interceptor)
        .EnableSensitiveDataLogging(false)
        .LogTo(message => { }, LogLevel.Warning);
    });

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
    builder.Services.AddScoped<TournamentFeedService>();

    // Register the ServerStatsService and supporting services
    builder.Services.AddScoped<ServerStatsService>();
    builder.Services.AddScoped<IServerStatsService>(sp => sp.GetRequiredService<ServerStatsService>());
    builder.Services.AddScoped<RoundsService>();
    // Register the stat collector background services
    builder.Services.AddHostedService<StatsCollectionBackgroundService>();
    builder.Services.AddHostedService<RankingCalculationService>();
    builder.Services.AddHostedService<AggregateCalculationService>();

    // Register NodaTime clock for time-based services
    builder.Services.AddSingleton<IClock>(SystemClock.Instance);

    // Register job runners (can be triggered on-demand via AdminJobsController)
    builder.Services.AddScoped<IDailyAggregateRefreshBackgroundService, DailyAggregateRefreshJob>();
    builder.Services.AddScoped<IWeeklyCleanupBackgroundService, WeeklyCleanupBackgroundService>();
    builder.Services.AddScoped<IAggregateBackfillBackgroundService, AggregateBackfillBackgroundService>();

    // Register background jobs for scheduled execution
    builder.Services.AddHostedService<DailyAggregateRefreshBackgroundService>();
    builder.Services.AddHostedService<WeeklyCleanupJob>();


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

    builder.Services.AddScoped<api.Gamification.Services.SqliteGamificationService>();
    builder.Services.AddScoped<api.Gamification.Services.KillStreakDetector>();
    builder.Services.AddScoped<api.Gamification.Services.MilestoneCalculator>();
    builder.Services.AddScoped<api.Gamification.Services.PlacementProcessor>();
    builder.Services.AddScoped<api.Gamification.Services.TeamVictoryProcessor>();
    builder.Services.AddScoped<api.Gamification.Services.GamificationService>();

    // Register Gamification Background Service
    builder.Services.AddHostedService<api.Gamification.Services.GamificationBackgroundService>();

    // Register ImageStorage Services
    builder.Services.AddScoped<api.ImageStorage.IImageIndexingService, api.ImageStorage.ImageIndexingService>();
    builder.Services.AddScoped<api.ImageStorage.IAssetServingService, api.ImageStorage.AssetServingService>();

    // SQLite-based analytics services
    builder.Services.AddScoped<api.GameTrends.ISqliteGameTrendsService, api.GameTrends.SqliteGameTrendsService>();
    builder.Services.AddScoped<api.PlayerStats.ISqliteLeaderboardService, api.PlayerStats.SqliteLeaderboardService>();
    builder.Services.AddScoped<api.PlayerStats.ISqlitePlayerStatsService, api.PlayerStats.SqlitePlayerStatsService>();
    builder.Services.AddScoped<api.PlayerStats.ISqlitePlayerComparisonService, api.PlayerStats.SqlitePlayerComparisonService>();
    builder.Services.AddScoped<api.PlayerStats.IPlayerBestScoresService, api.PlayerStats.PlayerBestScoresService>();

    // Register Data Explorer service
    builder.Services.AddScoped<api.DataExplorer.IDataExplorerService, api.DataExplorer.DataExplorerService>();

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
        var logger = scope.ServiceProvider.GetRequiredService<ILogger<Program>>();

        try
        {
            // Apply EF Core migrations for SQLite
            dbContext.Database.Migrate();
            logger.LogInformation("SQLite database migrations applied successfully");

            // Configure SQLite PRAGMAs for optimal performance and to prevent locking issues
            // These need to be set after the connection is established
            var connection = dbContext.Database.GetDbConnection();
            await connection.OpenAsync();
            
            using var command = connection.CreateCommand();
            
            // Set busy_timeout to 5 seconds - wait for locks instead of failing immediately
            command.CommandText = "PRAGMA busy_timeout = 5000;";
            await command.ExecuteNonQueryAsync();
            logger.LogInformation("SQLite busy_timeout set to 5000ms");
            
            // Ensure WAL mode is enabled for better concurrent access
            command.CommandText = "PRAGMA journal_mode = WAL;";
            var journalMode = await command.ExecuteScalarAsync();
            logger.LogInformation("SQLite journal_mode: {JournalMode}", journalMode);
            
            // Checkpoint WAL to ensure clean state (important after database restore)
            command.CommandText = "PRAGMA wal_checkpoint(TRUNCATE);";
            await command.ExecuteNonQueryAsync();
            logger.LogInformation("SQLite WAL checkpoint completed");
            
            await connection.CloseAsync();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "An error occurred while applying SQLite migrations or configuring PRAGMAs");
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
