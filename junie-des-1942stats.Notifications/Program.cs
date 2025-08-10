using junie_des_1942stats.Notifications.Consumers;
using junie_des_1942stats.Notifications.Handlers;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Services;
using StackExchange.Redis;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;

var builder = WebApplication.CreateBuilder(args);

// Configure Google JWT Authentication
var googleClientId = builder.Configuration["Jwt:Audience"] ?? throw new InvalidOperationException("Jwt:Audience (Google Client ID) must be configured");

builder.Services.AddAuthentication(options =>
{
    options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
    options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
})
.AddJwtBearer(options =>
{
    options.Authority = "https://accounts.google.com";
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuers = new[] { "https://accounts.google.com", "accounts.google.com" },
        ValidateAudience = true,
        ValidAudience = googleClientId,
        ValidateLifetime = true,
        ClockSkew = TimeSpan.FromMinutes(5),
        ValidateIssuerSigningKey = true,
        NameClaimType = "email"
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
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "42redis.home.net:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

// Register services
builder.Services.AddHttpClient<IBuddyApiService, BuddyApiService>();
builder.Services.AddSingleton<IBuddyNotificationService, BuddyNotificationService>();
builder.Services.AddHostedService<PlayerEventConsumer>();

// Register notification handlers
builder.Services.AddScoped<ServerMapChangeNotificationHandler>();
builder.Services.AddScoped<PlayerOnlineNotificationHandler>();

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

// Map SignalR hub
app.MapHub<NotificationHub>("/hub");

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
