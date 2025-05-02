using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;
using System.Text.Json;

var builder = Host.CreateApplicationBuilder(args);

// Register our metric collector service
builder.Services.AddHostedService<BF1942MetricsCollector>();

// Add HTTP server for Prometheus to scrape
builder.Services.AddMetricServer(options =>
{
    options.Port = 9091;
});

var host = builder.Build();
await host.RunAsync();