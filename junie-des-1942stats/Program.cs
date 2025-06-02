using junie_des_1942stats.PlayerStats;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure SQLite database path - check for environment variable first
string dbPath;
var envDbPath = Environment.GetEnvironmentVariable("DB_PATH");

if (!string.IsNullOrEmpty(envDbPath))
{
    // Use the environment variable path if it exists
    dbPath = envDbPath;
    Console.WriteLine($"Using database path from environment variable: {dbPath}");
}
else
{
    // Default to current directory
    dbPath = Path.Combine(Directory.GetCurrentDirectory(), "playertracker.db");
    Console.WriteLine($"Using default database path: {dbPath}");
}

// Configure SQLite
builder.Services.AddDbContext<PlayerTrackerDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register the player tracking service
builder.Services.AddScoped<PlayerTrackingService>();
builder.Services.AddScoped<PlayerStatsService>();

// Register the ServerStatsService
builder.Services.AddScoped<ServerStatsService>();

// Register the metrics collector as a hosted service
builder.Services.AddHostedService<BF1942MetricsCollector>();

// Add HTTP server for Prometheus to scrape
builder.Services.AddMetricServer(options =>
{
    options.Port = 9091;
});

// Add Prometheus service
builder.Services.AddHttpClient<PrometheusService>(client => { client.Timeout = TimeSpan.FromSeconds(30); })
    .ConfigurePrimaryHttpMessageHandler(() => new HttpClientHandler
    {
        ServerCertificateCustomValidationCallback = HttpClientHandler.DangerousAcceptAnyServerCertificateValidator
    });

builder.Services.AddSingleton<PrometheusService>(sp =>
{
    var httpClient = sp.GetRequiredService<IHttpClientFactory>().CreateClient();
    var prometheusUrl = builder.Configuration["Prometheus:Url"] ?? "http://localhost:9090/api/v1";
    return new PrometheusService(httpClient, prometheusUrl);
});

var host = builder.Build();

// Enable routing and controllers
host.UseRouting();
host.MapControllers();

// Ensure database is created
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
    try
    {
        dbContext.Database.Migrate();
        Console.WriteLine("Database migrations applied successfully.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"An error occurred while applying migrations: {ex.Message}");
        // Consider logging the error properly using ILogger
    }

}
await host.RunAsync();