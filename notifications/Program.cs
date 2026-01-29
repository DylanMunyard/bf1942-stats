using notifications.Consumers;
using notifications.Handlers;
using notifications.Hubs;
using notifications.Models;
using notifications.Services;
using notifications.Telemetry;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Serilog.Enrichers.Span;
using System.Security.Cryptography;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;
using OpenTelemetry.Logs;
using Serilog.Sinks.Grafana.Loki;
using Microsoft.Extensions.Configuration;
using Azure.Monitor.OpenTelemetry.Exporter;

// Early configuration for Application Insights connection string
var earlyConfig = new ConfigurationBuilder()
    .AddEnvironmentVariables()
    .AddUserSecrets<Program>(optional: true)
    .Build();

var lokiUrl = Environment.GetEnvironmentVariable("LOKI_URL") ?? "http://localhost:3100";
var otlpEndpoint = Environment.GetEnvironmentVariable("OTLP_ENDPOINT") ?? "http://localhost:4318/v1/traces";
var appInsightsConnectionString = earlyConfig["APPLICATIONINSIGHTS_CONNECTION_STRING"] ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING");
var useAzureMonitor = !string.IsNullOrEmpty(appInsightsConnectionString);
var serviceName = "junie-des-1942stats.Notifications";
var environment = Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development";

var loggerConfig = new LoggerConfiguration()
    .MinimumLevel.Warning()
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
    // Suppress verbose HTTP client logs from the OTLP trace exporter and buddy services
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
    .WriteTo.Console(
        outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}");

// Local development: use Loki only
// Production with Azure Monitor: logs go via OTEL to App Insights
if (!useAzureMonitor)
{
    loggerConfig.WriteTo.GrafanaLoki(lokiUrl,
        labels: new[]
        {
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "service", Value = serviceName },
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "environment", Value = environment },
            new Serilog.Sinks.Grafana.Loki.LokiLabel { Key = "host", Value = Environment.MachineName }
        },
        propertiesAsLabels: new[] { "request_path", "http_method", "ElapsedMs", "ElapsedMilliseconds", "RequestPath", "TraceId" },
        textFormatter: new Serilog.Formatting.Compact.RenderedCompactJsonFormatter());
}

Log.Logger = loggerConfig.CreateLogger();

try
{
    Log.Information("Starting up junie-des-1942stats.Notifications application");
    var loggingBackend = useAzureMonitor ? "Azure Application Insights (OTEL)" : "Loki";
    Log.Information("Telemetry backend: {Backend}", loggingBackend);
    if (useAzureMonitor)
    {
        Log.Information("APPLICATIONINSIGHTS_CONNECTION_STRING is set: {IsSet}, length: {Length}", 
            !string.IsNullOrEmpty(appInsightsConnectionString), 
            appInsightsConnectionString?.Length ?? 0);
    }

    var builder = WebApplication.CreateBuilder(args);

    // Configure OTEL logging for Azure Monitor FIRST (before Serilog) to ensure provider is registered
    // This is important because Serilog's writeToProviders: true forwards logs to registered ILogger providers
    if (useAzureMonitor)
    {
        // Read connection string from builder.Configuration as well (may have additional sources)
        var azureMonitorConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] 
            ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
            ?? appInsightsConnectionString;
        
        if (string.IsNullOrEmpty(azureMonitorConnectionString))
        {
            Log.Warning("APPLICATIONINSIGHTS_CONNECTION_STRING is not set. Azure Monitor logging will not work.");
        }
        else
        {
            Log.Information("Configuring Azure Monitor logging with connection string (length: {Length}, starts with: {Prefix})", 
                azureMonitorConnectionString.Length, 
                azureMonitorConnectionString.Substring(0, Math.Min(50, azureMonitorConnectionString.Length)));
        }
        
        builder.Logging.AddOpenTelemetry(logging =>
        {
            logging.IncludeScopes = true;
            logging.IncludeFormattedMessage = true;
            logging.AddAzureMonitorLogExporter(options =>
            {
                // Explicitly set connection string - Azure Monitor exporter will also read from env var if not set
                if (!string.IsNullOrEmpty(azureMonitorConnectionString))
                {
                    options.ConnectionString = azureMonitorConnectionString;
                    Log.Information("Azure Monitor log exporter configured with explicit connection string");
                }
                else
                {
                    Log.Warning("Azure Monitor log exporter will attempt to read connection string from APPLICATIONINSIGHTS_CONNECTION_STRING environment variable");
                }
            });
        });
    }

    // Add Serilog to the application
    // writeToProviders: true forwards logs to other configured providers (e.g., OTEL logging for Azure Monitor)
    if (useAzureMonitor)
    {
        // When using Azure Monitor, forward logs to both Serilog and OTEL logging provider
        builder.Host.UseSerilog((context, services, configuration) =>
        {
            configuration
                .MinimumLevel.Warning()
                .Enrich.WithProperty("service.name", serviceName)
                .Enrich.WithProperty("deployment.environment", environment)
                .Enrich.FromLogContext()
                .Enrich.WithMachineName()
                .Enrich.WithSpan()
                .WriteTo.Console(
                    outputTemplate: "[{Timestamp:yyyy-MM-dd HH:mm:ss.fff} {Level:u3}] [{SourceContext}] {Message:lj} {Properties:j}{NewLine}{Exception}");
        }, writeToProviders: true);
    }
    else
    {
        // Local dev: use the static Log.Logger configured earlier (with Loki)
        builder.Host.UseSerilog();
    }

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
                    // Don't trace health checks and SignalR negotiate
                    options.Filter = httpContext =>
                    {
                        var path = httpContext.Request.Path.Value?.ToLower() ?? "";
                        return !path.Contains("/health") && !path.Contains("/hub/negotiate");
                    };
                    options.RecordException = true;
                });
                tracing.AddHttpClientInstrumentation();
                
                // Configure trace exporter: Azure Monitor for production, OTLP for local dev
                if (useAzureMonitor)
                {
                    // Read connection string from builder.Configuration as well (may have additional sources)
                    var azureMonitorTraceConnectionString = builder.Configuration["APPLICATIONINSIGHTS_CONNECTION_STRING"] 
                        ?? Environment.GetEnvironmentVariable("APPLICATIONINSIGHTS_CONNECTION_STRING")
                        ?? appInsightsConnectionString;
                    
                    tracing.AddAzureMonitorTraceExporter(options =>
                    {
                        if (!string.IsNullOrEmpty(azureMonitorTraceConnectionString))
                        {
                            options.ConnectionString = azureMonitorTraceConnectionString;
                        }
                    });
                }
                else
                {
                    tracing.AddOtlpExporter(opt =>
                    {
                        opt.Endpoint = new Uri(otlpEndpoint);
                        opt.Protocol = OtlpExportProtocol.HttpProtobuf;
                    });
                }
                
                tracing.AddSource("junie-des-1942stats.Notifications.*");
                tracing.AddSource(ActivitySources.Redis.Name);
                tracing.AddSource(ActivitySources.Http.Name);
                tracing.AddSource(ActivitySources.SignalR.Name);
                tracing.AddSource(ActivitySources.Events.Name);
            }
        );

    // Configure JWT Authentication to validate self-minted tokens from main app
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

        // Clear default claim type mappings to preserve original JWT claims
        options.MapInboundClaims = false;

        // Configure SignalR token handling
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var accessToken = context.Request.Query["access_token"];
                var path = context.HttpContext.Request.Path;
                if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub"))
                {
                    context.Token = accessToken;
                }
                return Task.CompletedTask;
            }
        };
    });

    builder.Services.AddAuthorization();

    // Add services to the container
    builder.Services.AddSignalR();
    // Register EventAggregator
    builder.Services.AddSingleton<IEventAggregator, EventAggregator>();

    // Configure Redis
    var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "42redis.home.net:6380";
    builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
    builder.Services.AddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

    // Register services
    builder.Services.AddHttpClient<IBuddyApiService, BuddyApiService>();
    builder.Services.AddSingleton<IBuddyNotificationService, BuddyNotificationService>();
    builder.Services.AddHostedService<PlayerEventConsumer>();

    // Register notification handlers
    builder.Services.AddSingleton<ServerMapChangeNotificationHandler>();
    builder.Services.AddSingleton<PlayerOnlineNotificationHandler>();

    var app = builder.Build();

    // Subscribe handlers to event aggregator
    using (var scope = app.Services.CreateScope())
    {
        var eventAggregator = scope.ServiceProvider.GetRequiredService<IEventAggregator>();
        var mapChangeHandler = scope.ServiceProvider.GetRequiredService<ServerMapChangeNotificationHandler>();
        var playerOnlineHandler = scope.ServiceProvider.GetRequiredService<PlayerOnlineNotificationHandler>();

        eventAggregator.Subscribe<ServerMapChangeNotification>((notification, ct) => mapChangeHandler.Handle(notification, ct));
        eventAggregator.Subscribe<PlayerOnlineNotification>((notification, ct) => playerOnlineHandler.Handle(notification, ct));
    }

    // Configure middleware
    if (app.Environment.IsDevelopment())
    {
        app.UseDeveloperExceptionPage();
    }

    app.UseRouting();
    app.UseAuthentication();
    app.UseAuthorization();

    static SecurityKey CreateRsaKey(string pem)
    {
        var rsa = RSA.Create();
        rsa.ImportFromPem(pem);
        return new RsaSecurityKey(rsa);
    }

    // Map SignalR hub
    app.MapHub<NotificationHub>("/hub");

    // Health check endpoint
    app.MapGet("/health", () => Results.Ok("Healthy"));

    Log.Information("Application started successfully");
    await app.RunAsync();
}
catch (Exception ex)
{
    Log.Fatal(ex, "Application terminated unexpectedly");
}
finally
{
    Log.CloseAndFlush();
}
