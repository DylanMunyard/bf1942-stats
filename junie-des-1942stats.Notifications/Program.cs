using junie_des_1942stats.Notifications.Consumers;
using junie_des_1942stats.Notifications.Handlers;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Services;
using MediatR;
using StackExchange.Redis;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddSignalR();
builder.Services.AddMediatR(cfg => cfg.RegisterServicesFromAssembly(typeof(Program).Assembly));

// Configure Redis
var redisConnection = builder.Configuration.GetConnectionString("Redis") ?? "42redis.home.net:6379";
builder.Services.AddSingleton<IConnectionMultiplexer>(ConnectionMultiplexer.Connect(redisConnection));
builder.Services.AddSingleton<IDatabase>(sp => sp.GetRequiredService<IConnectionMultiplexer>().GetDatabase());

// Register services
builder.Services.AddHttpClient<IBuddyApiService, BuddyApiService>();
builder.Services.AddSingleton<IBuddyNotificationService, BuddyNotificationService>();
builder.Services.AddSingleton<IPlayerEventPublisher, PlayerEventPublisher>();
builder.Services.AddHostedService<PlayerEventConsumer>();

// Register notification handlers
builder.Services.AddScoped<MapChangeNotificationHandler>();
builder.Services.AddScoped<PlayerOnlineNotificationHandler>();

var app = builder.Build();

// Configure middleware
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}

app.UseRouting();

// Map SignalR hub
app.MapHub<NotificationHub>("/notificationHub");

// Health check endpoint
app.MapGet("/health", () => Results.Ok("Healthy"));

app.Run();
