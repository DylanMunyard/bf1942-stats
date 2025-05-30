using junie_des_1942stats.PlayerStats;
using Microsoft.AspNetCore.Builder;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using junie_des_1942stats.PlayerTracking;
using Prometheus;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container
builder.Services.AddControllers();

// Configure SQLite
var dbPath = Path.Combine(Directory.GetCurrentDirectory(), "playertracker.db");
builder.Services.AddDbContext<PlayerTrackerDbContext>(options =>
    options.UseSqlite($"Data Source={dbPath}"));

// Register the player tracking service
builder.Services.AddScoped<PlayerTrackingService>();
builder.Services.AddScoped<PlayerStatsService>();

// Register the metrics collector as a hosted service
builder.Services.AddHostedService<BF1942MetricsCollector>();

// Add HTTP server for Prometheus to scrape
builder.Services.AddMetricServer(options =>
{
    options.Port = 9091;
});

var host = builder.Build();

// Enable routing and controllers
host.UseRouting();
host.MapControllers();

// Ensure database is created
using (var scope = host.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
    dbContext.Database.EnsureCreated();
}
await host.RunAsync();