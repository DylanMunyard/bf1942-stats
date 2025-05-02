using Microsoft.Extensions.Hosting;
using Prometheus;
using System.Text.Json;

public class BF1942MetricsCollector : BackgroundService
{
    private readonly Gauge _playersGauge;
    private readonly HttpClient _httpClient;
    private const string API_URL = "https://api.bflist.io/bf1942/v1/livestats";
    
    public BF1942MetricsCollector()
    {
        _playersGauge = Metrics.CreateGauge("bf1942_players_online", "Number of players currently online in BF1942");
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                var response = await _httpClient.GetStringAsync(API_URL, stoppingToken);
                var stats = JsonSerializer.Deserialize<BF1942Stats>(response, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                if (stats != null)
                {
                    _playersGauge.Set(stats.Players);
                    Console.WriteLine($"Updated metrics - Players: {stats.Players}");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error collecting metrics: {ex.Message}");
            }

            // Wait for 1 minute before the next collection
            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _httpClient.Dispose();
        await base.StopAsync(stoppingToken);
    }
}