using System.Text.Json;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.StatsCollectors.Modals;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Prometheus;

namespace junie_des_1942stats.StatsCollectors;

public class StatsCollectionBackgroundService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly Gauge _totalPlayersGauge;
    private readonly Gauge _serverPlayersGauge;
    private readonly Gauge _fh2TotalPlayersGauge;
    private readonly Gauge _fh2ServerPlayersGauge;
    private readonly HttpClient _httpClient;
    
    private const string STATS_API_URL = "https://api.bflist.io/bf1942/v1/livestats";
    private const string SERVERS_API_URL = "https://api.bflist.io/bf1942/v1/servers/1?perPage=100";
    private const string FH2_STATS_API_URL = "https://api.bflist.io/fh2/v1/livestats";
    private const string FH2_SERVERS_API_URL = "https://api.bflist.io/fh2/v1/servers/1?perPage=100";
    
    public StatsCollectionBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        _totalPlayersGauge = Metrics.CreateGauge(
            "bf1942_players_online", 
            "Number of players currently online in BF1942"
        );
        
        _serverPlayersGauge = Metrics.CreateGauge(
            "bf1942_server_players",
            "Number of players on each BF1942 server",
            new GaugeConfiguration
            {
                LabelNames = ["server_name"]
            }
        );

        _fh2TotalPlayersGauge = Metrics.CreateGauge(
            "fh2_players_online", 
            "Number of players currently online in FH2"
        );
        
        _fh2ServerPlayersGauge = Metrics.CreateGauge(
            "fh2_server_players",
            "Number of players on each FH2 server",
            new GaugeConfiguration
            {
                LabelNames = ["server_name"]
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
                // Create a new scope for each operation
                using (var scope = _scopeFactory.CreateScope())
                {
                    // Get the scoped service from the scope
                    var playerTrackingService = scope.ServiceProvider.GetRequiredService<PlayerTrackingService>();

                    // Get BF1942 stats
                    await CollectTotalPlayersAsync(stoppingToken);
                    await CollectServerStatsAsync(playerTrackingService, stoppingToken);

                    // Get FH2 stats
                    await CollectFh2TotalPlayersAsync(stoppingToken);
                    await CollectFh2ServerStatsAsync(playerTrackingService, stoppingToken);
                }
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
        var stats = JsonSerializer.Deserialize<NumberPlayerStats>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (stats != null)
        {
            _totalPlayersGauge.Set(stats.Players);
            Console.WriteLine($"Updated total players metric - Players: {stats.Players}");
        }
    }

    private async Task CollectServerStatsAsync(PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(SERVERS_API_URL, stoppingToken);
        var serversData = JsonSerializer.Deserialize<Bf1942ServerInfo[]>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Track all current label sets
        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;

        if (serversData != null)
        {
            foreach (var server in serversData)
            {
                _serverPlayersGauge
                    .WithLabels(server.Name)
                    .Set(server.NumPlayers);

                currentLabelSets.Add(server.Name);
                
                // Track players in the database
                try
                {
                    await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error tracking BF1942 players from server: {ex.Message}");
                }
            }
            Console.WriteLine($"Updated servers metric - # servers: {serversData.Length}");
        }

        // Remove metrics for servers no longer online
        // Get all label sets currently in the gauge
        var allLabelSets = _serverPlayersGauge.GetAllLabelValues();

        foreach (var labelSet in allLabelSets)
        {
            var key = labelSet[0];
            if (!currentLabelSets.Contains(key))
            {
                _serverPlayersGauge.RemoveLabelled(labelSet);
            }
        }
    }

    private async Task CollectFh2TotalPlayersAsync(CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(FH2_STATS_API_URL, stoppingToken);
        var stats = JsonSerializer.Deserialize<NumberPlayerStats>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (stats != null)
        {
            _fh2TotalPlayersGauge.Set(stats.Players);
            Console.WriteLine($"Updated FH2 total players metric - Players: {stats.Players}");
        }
    }

    private async Task CollectFh2ServerStatsAsync(PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(FH2_SERVERS_API_URL, stoppingToken);
        var serversData = JsonSerializer.Deserialize<Fh2ServerInfo[]>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        // Track all current label sets
        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;

        if (serversData != null)
        {
            foreach (var server in serversData)
            {
                _fh2ServerPlayersGauge
                    .WithLabels(server.Name)
                    .Set(server.NumPlayers);

                currentLabelSets.Add(server.Name);
                
                // Track players in the database
                try
                {
                    await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error tracking FH2 players from server: {ex.Message}");
                };
            }
            Console.WriteLine($"Updated FH2 servers metric - # servers: {serversData.Length}");
        }

        // Remove metrics for servers no longer online
        var allLabelSets = _fh2ServerPlayersGauge.GetAllLabelValues();

        foreach (var labelSet in allLabelSets)
        {
            var key = labelSet[0];
            if (!currentLabelSets.Contains(key))
            {
                _fh2ServerPlayersGauge.RemoveLabelled(labelSet);
            }
        }
    }

    public override async Task StopAsync(CancellationToken stoppingToken)
    {
        _httpClient.Dispose();
        await base.StopAsync(stoppingToken);
    }
}