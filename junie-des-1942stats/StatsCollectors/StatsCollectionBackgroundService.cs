using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Prometheus;
using System.Text.Json;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.StatsCollectors.Modals;

namespace junie_des_1942stats.StatsCollectors;

public class StatsCollectionBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _collectionInterval = TimeSpan.FromMinutes(1);
    private Timer _timer;
    private int _isRunning = 0;
    
    // Metrics
    private readonly Gauge _totalPlayersGauge;
    private readonly Gauge _serverPlayersGauge;
    private readonly Gauge _fh2TotalPlayersGauge;
    private readonly Gauge _fh2ServerPlayersGauge;
    private readonly HttpClient _httpClient;

    // API URLs
    private const string STATS_API_URL = "https://api.bflist.io/bf1942/v1/livestats";
    private const string SERVERS_API_URL = "https://api.bflist.io/bf1942/v1/servers/1?perPage=100";
    private const string FH2_STATS_API_URL = "https://api.bflist.io/fh2/v1/livestats";
    private const string FH2_SERVERS_API_URL = "https://api.bflist.io/fh2/v1/servers/1?perPage=100";

    public StatsCollectionBackgroundService(IServiceScopeFactory scopeFactory)
    {
        _scopeFactory = scopeFactory;
        
        // Initialize metrics
        _totalPlayersGauge = Metrics.CreateGauge(
            "bf1942_players_online", 
            "Number of players currently online in BF1942");
        
        _serverPlayersGauge = Metrics.CreateGauge(
            "bf1942_server_players",
            "Number of players on each BF1942 server",
            new GaugeConfiguration { LabelNames = new[] { "server_name" } });

        _fh2TotalPlayersGauge = Metrics.CreateGauge(
            "fh2_players_online", 
            "Number of players currently online in FH2");
        
        _fh2ServerPlayersGauge = Metrics.CreateGauge(
            "fh2_server_players",
            "Number of players on each FH2 server",
            new GaugeConfiguration { LabelNames = new[] { "server_name" } });

        _httpClient = new HttpClient();
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Stats collection service starting...");
        
        // Initialize timer to run immediately (dueTime: 0) and then every minute
        _timer = new Timer(
            callback: ExecuteCollectionCycle,
            state: null,
            dueTime: TimeSpan.Zero,  // Run immediately
            period: _collectionInterval);
        
        return Task.CompletedTask;
    }

    private async void ExecuteCollectionCycle(object state)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Previous collection still running, skipping...");
            return;
        }

        var cycleStopwatch = Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Starting stats collection cycle...");

        try
        {
            using (var scope = _scopeFactory.CreateScope())
            {
                var playerTrackingService = scope.ServiceProvider.GetRequiredService<PlayerTrackingService>();
                
                // 1. Global timeout cleanup
                var timeoutStopwatch = Stopwatch.StartNew();
                await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                timeoutStopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Session cleanup: {timeoutStopwatch.ElapsedMilliseconds}ms");

                // 2. BF1942 stats
                var bf1942Stopwatch = Stopwatch.StartNew();
                await CollectTotalPlayersAsync(CancellationToken.None);
                var bf1942ServersStopwatch = Stopwatch.StartNew();
                await CollectServerStatsAsync(playerTrackingService, CancellationToken.None);
                bf1942ServersStopwatch.Stop();
                bf1942Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BF1942 stats: {bf1942Stopwatch.ElapsedMilliseconds}ms (Servers: {bf1942ServersStopwatch.ElapsedMilliseconds}ms)");

                // 3. FH2 stats
                var fh2Stopwatch = Stopwatch.StartNew();
                await CollectFh2TotalPlayersAsync(CancellationToken.None);
                var fh2ServersStopwatch = Stopwatch.StartNew();
                await CollectFh2ServerStatsAsync(playerTrackingService, CancellationToken.None);
                fh2ServersStopwatch.Stop();
                fh2Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] FH2 stats: {fh2Stopwatch.ElapsedMilliseconds}ms (Servers: {fh2ServersStopwatch.ElapsedMilliseconds}ms)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Error in collection cycle: {ex}");
        }
        finally
        {
            cycleStopwatch.Stop();
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cycle completed in {cycleStopwatch.ElapsedMilliseconds}ms");
            Interlocked.Exchange(ref _isRunning, 0);
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
        }
    }

    private async Task CollectServerStatsAsync(PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(SERVERS_API_URL, stoppingToken);
        var serversData = JsonSerializer.Deserialize<Bf1942ServerInfo[]>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (serversData == null) return;

        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;

        foreach (var server in serversData)
        {
            _serverPlayersGauge.WithLabels(server.Name).Set(server.NumPlayers);
            currentLabelSets.Add(server.Name);
            await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
        }

        // Clean up old server metrics
        foreach (var labelSet in _serverPlayersGauge.GetAllLabelValues())
        {
            if (!currentLabelSets.Contains(labelSet[0]))
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
        }
    }

    private async Task CollectFh2ServerStatsAsync(PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var response = await _httpClient.GetStringAsync(FH2_SERVERS_API_URL, stoppingToken);
        var serversData = JsonSerializer.Deserialize<Fh2ServerInfo[]>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });

        if (serversData == null) return;

        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;

        foreach (var server in serversData)
        {
            _fh2ServerPlayersGauge.WithLabels(server.Name).Set(server.NumPlayers);
            currentLabelSets.Add(server.Name);
            await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
        }

        // Clean up old server metrics
        foreach (var labelSet in _fh2ServerPlayersGauge.GetAllLabelValues())
        {
            if (!currentLabelSets.Contains(labelSet[0]))
            {
                _fh2ServerPlayersGauge.RemoveLabelled(labelSet);
            }
        }
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Stats collection service stopping...");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
        _httpClient?.Dispose();
    }
}