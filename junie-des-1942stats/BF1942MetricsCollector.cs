using Microsoft.Extensions.Hosting;
using Prometheus;
using System.Text.Json;

public class BF1942MetricsCollector : BackgroundService
{
    private readonly Gauge _totalPlayersGauge;
    private readonly Gauge _serverPlayersGauge;
    private readonly HttpClient _httpClient;
    private const string STATS_API_URL = "https://api.bflist.io/bf1942/v1/livestats";
    private const string SERVERS_API_URL = "https://api.bflist.io/bf1942/v1/servers/1?perPage=100";
    
    public BF1942MetricsCollector()
    {
        _totalPlayersGauge = Metrics.CreateGauge(
            "bf1942_players_online", 
            "Number of players currently online in BF1942"
        );
        
        _serverPlayersGauge = Metrics.CreateGauge(
            "bf1942_server_players",
            "Number of players on each BF1942 server",
            new GaugeConfiguration
            {
                LabelNames = new[] { "server_name", "map" }
            }
        );
        
        _httpClient = new HttpClient();
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                // Get total players stats
                await CollectTotalPlayersAsync(stoppingToken);
                
                // Get per-server stats
                await CollectServerStatsAsync(stoppingToken);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error collecting metrics: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken);
        }
    }

    private async Task CollectTotalPlayersAsync(CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(STATS_API_URL, stoppingToken);
        var stats = JsonSerializer.Deserialize<BF1942Stats>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (stats != null)
        {
            _totalPlayersGauge.Set(stats.Players);
            Console.WriteLine($"Updated total players metric - Players: {stats.Players}");
        }
    }

    private async Task CollectServerStatsAsync(CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(SERVERS_API_URL, stoppingToken);
        var serversData = JsonSerializer.Deserialize<ServerInfo[]>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (serversData != null)
        {
            foreach (var server in serversData)
            {
                _serverPlayersGauge
                    .WithLabels(
                        server.Name,
                        server.MapName
                    )
                    .Set(server.NumPlayers);
            }
            Console.WriteLine($"Updated servers metric - # servers: {serversData.Length}");
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _httpClient.Dispose();
        await base.StopAsync(stoppingToken);
    }
}

public class ServerInfo
{
    public string Name { get; set; } = "";
    public int NumPlayers { get; set; }
    public string MapName { get; set; } = "";
}