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
using junie_des_1942stats.ClickHouse;
using Serilog.Context;

namespace junie_des_1942stats.StatsCollectors;

public class StatsCollectionBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly TimeSpan _collectionInterval = TimeSpan.FromSeconds(15);
    private Timer _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;
    
    // Metrics
    private readonly Gauge _totalPlayersGauge;
    private readonly Gauge _serverPlayersGauge;
    private readonly Gauge _fh2TotalPlayersGauge;
    private readonly Gauge _fh2ServerPlayersGauge;
    private readonly HttpClient _httpClient;

    // API URLs
    private const string BF1942_BASE_URL = "https://api.bflist.io/v2/bf1942/";
    private const string FH2_BASE_URL = "https://api.bflist.io/v2/fh2/";

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
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Stats collection service starting (15s intervals)...");
        
        // Initialize timer to run immediately (dueTime: 0) and then every 15 seconds
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

        var currentCycle = Interlocked.Increment(ref _cycleCount);
        var isEvenCycle = currentCycle % 4 == 0; // Every 4th cycle (60s) for SQLite
        
        var cycleStopwatch = Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Starting stats collection cycle #{currentCycle} (ClickHouse: Always, SQLite: {(isEvenCycle ? "Yes" : "No")})...");

        try
        {
            // Add context properties to mark all logs in this scope as coming from stats collection
            using (LogContext.PushProperty("operation_type", "stats_collection"))
            using (LogContext.PushProperty("bulk_operation", true))
            using (LogContext.PushProperty("cycle_number", currentCycle))
            using (var scope = _scopeFactory.CreateScope())
            {
                var playerTrackingService = scope.ServiceProvider.GetRequiredService<PlayerTrackingService>();
                var playerMetricsService = scope.ServiceProvider.GetRequiredService<PlayerMetricsService>();
                
                // 1. Global timeout cleanup (only on every 4th cycle to avoid too frequent DB operations)
                if (isEvenCycle)
                {
                    var timeoutStopwatch = Stopwatch.StartNew();
                    await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                    timeoutStopwatch.Stop();
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Session cleanup: {timeoutStopwatch.ElapsedMilliseconds}ms");
                }

                // 2. BF1942 stats
                var bf1942Stopwatch = Stopwatch.StartNew();
                await CollectTotalPlayersAsync(CancellationToken.None);
                var bf1942ServersStopwatch = Stopwatch.StartNew();
                var bf1942Servers = await CollectServerStatsAsync(playerTrackingService, isEvenCycle, CancellationToken.None);
                bf1942ServersStopwatch.Stop();
                bf1942Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BF1942 stats: {bf1942Stopwatch.ElapsedMilliseconds}ms (Servers: {bf1942ServersStopwatch.ElapsedMilliseconds}ms)");

                // 3. FH2 stats
                var fh2Stopwatch = Stopwatch.StartNew();
                await CollectFh2TotalPlayersAsync(CancellationToken.None);
                var fh2ServersStopwatch = Stopwatch.StartNew();
                var fh2Servers = await CollectFh2ServerStatsAsync(playerTrackingService, isEvenCycle, CancellationToken.None);
                fh2ServersStopwatch.Stop();
                fh2Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] FH2 stats: {fh2Stopwatch.ElapsedMilliseconds}ms (Servers: {fh2ServersStopwatch.ElapsedMilliseconds}ms)");

                // 4. Batch store all player metrics to ClickHouse
                var clickHouseStopwatch = Stopwatch.StartNew();
                var allServers = new List<IGameServer>();
                allServers.AddRange(bf1942Servers);
                allServers.AddRange(fh2Servers);
                var timestamp = DateTime.UtcNow;
                await playerMetricsService.StoreBatchedPlayerMetricsAsync(allServers, timestamp);
                clickHouseStopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse batch storage: {clickHouseStopwatch.ElapsedMilliseconds}ms ({allServers.Count} servers)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Error in collection cycle: {ex}");
        }
        finally
        {
            cycleStopwatch.Stop();
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cycle #{currentCycle} completed in {cycleStopwatch.ElapsedMilliseconds}ms");
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private async Task CollectTotalPlayersAsync(CancellationToken stoppingToken)
    {
        var url = $"{BF1942_BASE_URL}livestats";
        var response = await _httpClient.GetStringAsync(url, stoppingToken);
        var stats = JsonSerializer.Deserialize<NumberPlayerStats>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (stats != null)
        {
            _totalPlayersGauge.Set(stats.Players);
        }
    }

    private async Task<List<TServer>> FetchAllServersAsync<TServer, TResponse>(
        string baseUrl, 
        string serverType,
        Func<TResponse, TServer[]> getServers,
        Func<TResponse, string> getCursor,
        Func<TResponse, bool> getHasMore,
        Func<TServer, string> getServerIp,
        Func<TServer, int> getServerPort,
        CancellationToken stoppingToken)
    {
        var allServers = new List<TServer>();
        var url = baseUrl;
        var pageCount = 0;
        const int maxPages = 5;
        
        // Fetch all pages with circuit breaker
        do
        {
            pageCount++;
            if (pageCount > maxPages)
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {serverType} servers: Circuit breaker triggered - reached max pages ({maxPages})");
                break;
            }

            var response = await _httpClient.GetStringAsync(url, stoppingToken);
            var serversResponse = JsonSerializer.Deserialize<TResponse>(response, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            if (serversResponse == null) break;
            
            var servers = getServers(serversResponse);
            if (servers == null || servers.Length == 0) break;
            
            allServers.AddRange(servers);
            
            if (!getHasMore(serversResponse)) break;
            
            // Build next page URL
            var lastServer = servers.Last();
            var cursor = getCursor(serversResponse);
            var serverIp = getServerIp(lastServer);
            var serverPort = getServerPort(lastServer);
            url = $"{baseUrl}&cursor={cursor}&after={serverIp}:{serverPort}";
            
        } while (true);

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] {serverType} servers: Fetched {allServers.Count} servers across {pageCount} pages");
        return allServers;
    }

    private async Task<List<IGameServer>> CollectServerStatsAsync(PlayerTrackingService playerTrackingService, bool enableSqliteStorage, CancellationToken stoppingToken)
    {
        var allServers = await FetchAllServersAsync<Bf1942ServerInfo, Bf1942ServersResponse>(
            $"{BF1942_BASE_URL}servers?perPage=100",
            "BF1942",
            response => response.Servers,
            response => response.Cursor,
            response => response.HasMore,
            server => server.Ip,
            server => server.Port,
            stoppingToken);

        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;
        var gameServerAdapters = new List<IGameServer>();

        foreach (var server in allServers)
        {
            _serverPlayersGauge.WithLabels(server.Name).Set(server.NumPlayers);
            currentLabelSets.Add(server.Name);
            
            // Create adapter for ClickHouse batching
            var adapter = new Bf1942ServerAdapter(server);
            gameServerAdapters.Add(adapter);
            
            // Only store to SQLite on every 4th cycle (every 60s)
            if (enableSqliteStorage)
            {
                await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
            }
        }

        // Clean up old server metrics
        foreach (var labelSet in _serverPlayersGauge.GetAllLabelValues())
        {
            if (!currentLabelSets.Contains(labelSet[0]))
            {
                _serverPlayersGauge.RemoveLabelled(labelSet);
            }
        }

        return gameServerAdapters;
    }

    private async Task CollectFh2TotalPlayersAsync(CancellationToken stoppingToken)
    {
        var url = $"{FH2_BASE_URL}livestats";
        var response = await _httpClient.GetStringAsync(url, stoppingToken);
        var stats = JsonSerializer.Deserialize<NumberPlayerStats>(response, new JsonSerializerOptions
        {
            PropertyNameCaseInsensitive = true
        });
        
        if (stats != null)
        {
            _fh2TotalPlayersGauge.Set(stats.Players);
        }
    }

    private async Task<List<IGameServer>> CollectFh2ServerStatsAsync(PlayerTrackingService playerTrackingService, bool enableSqliteStorage, CancellationToken stoppingToken)
    {
        var allServers = await FetchAllServersAsync<Fh2ServerInfo, Fh2ServersResponse>(
            $"{FH2_BASE_URL}servers?perPage=100",
            "FH2",
            response => response.Servers,
            response => response.Cursor,
            response => response.HasMore,
            server => server.Ip,
            server => server.Port,
            stoppingToken);

        var currentLabelSets = new HashSet<string>();
        var timestamp = DateTime.UtcNow;
        var gameServerAdapters = new List<IGameServer>();

        foreach (var server in allServers)
        {
            _fh2ServerPlayersGauge.WithLabels(server.Name).Set(server.NumPlayers);
            currentLabelSets.Add(server.Name);
            
            // Create adapter for ClickHouse batching
            var adapter = new Fh2ServerAdapter(server);
            gameServerAdapters.Add(adapter);
            
            // Only store to SQLite on every 4th cycle (every 60s)
            if (enableSqliteStorage)
            {
                await playerTrackingService.TrackPlayersFromServerInfo(server, timestamp);
            }
        }

        // Clean up old server metrics
        foreach (var labelSet in _fh2ServerPlayersGauge.GetAllLabelValues())
        {
            if (!currentLabelSets.Contains(labelSet[0]))
            {
                _fh2ServerPlayersGauge.RemoveLabelled(labelSet);
            }
        }

        return gameServerAdapters;
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