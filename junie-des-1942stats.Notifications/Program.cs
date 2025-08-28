using junie_des_1942stats.Notifications.Consumers;
using junie_des_1942stats.Notifications.Handlers;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Services;
using junie_des_1942stats.Notifications.Telemetry;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using Serilog;
using Microsoft.Extensions.Logging;
using System.Security.Cryptography;
using System.Diagnostics;
using OpenTelemetry.Trace;
using OpenTelemetry.Resources;
using OpenTelemetry.Exporter;

// Configure Serilog
var seqUrl = Environment.GetEnvironmentVariable("SEQ_URL") ?? "http://192.168.1.230:5341";
var serviceName = "junie-des-1942stats.Notifications";
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
    .MinimumLevel.Override("junie_des_1942stats.Notifications", Serilog.Events.LogEventLevel.Information)
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
    Log.Information("Starting up junie-des-1942stats.Notifications application");

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
                    // Don't trace health checks and SignalR negotiate
                    options.Filter = httpContext =>
                    {
                        var path = httpContext.Request.Path.Value?.ToLower() ?? "";
                        return !path.Contains("/health") && !path.Contains("/hub/negotiate");
                    };
                    options.RecordException = true;
                });
                tracing.AddHttpClientInstrumentation();
                tracing.AddOtlpExporter(opt =>
                {
                    opt.Endpoint = new Uri($"{seqUrl}/ingest/otlp/v1/traces");
                    opt.Protocol = OtlpExportProtocol.HttpProtobuf;
                });
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
