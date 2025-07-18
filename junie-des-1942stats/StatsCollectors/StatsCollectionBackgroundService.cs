using System;
using System.Diagnostics;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Prometheus;
using System.Text.Json;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.StatsCollectors.Modals;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Services;
using Serilog.Context;
using System.Collections.Generic;
using System.Linq;

namespace junie_des_1942stats.StatsCollectors;

public class StatsCollectionBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _collectionInterval = TimeSpan.FromSeconds(30);
    private Timer _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;
    
    // Configuration setting for round syncing
    private readonly bool _enableRoundSyncing;
    
    // Metrics
    private readonly Gauge _totalPlayersGauge;
    private readonly Gauge _serverPlayersGauge;
    private readonly Gauge _fh2TotalPlayersGauge;
    private readonly Gauge _fh2ServerPlayersGauge;

    public StatsCollectionBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        
        // Check environment variable for round syncing - default to false (disabled)
        _enableRoundSyncing = Environment.GetEnvironmentVariable("ENABLE_ROUND_SYNCING")?.ToLowerInvariant() == "true";
        
        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? "http://clickhouse.home.net";
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        var isWriteUrlSet = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") != null;
        
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse Read URL: {clickHouseReadUrl}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse Write URL: {clickHouseWriteUrl} {(isWriteUrlSet ? "(custom)" : "(fallback to read URL)")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Round syncing to ClickHouse: {(_enableRoundSyncing ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] To avoid writes to production: Set CLICKHOUSE_WRITE_URL to dev instance or leave ENABLE_ROUND_SYNCING=false");
        
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
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Stats collection service starting (30s intervals)...");
        
        // Initialize timer to run immediately (dueTime: 0) and then every 30 seconds
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
        var isEvenCycle = currentCycle % 2 == 0; // Every 2nd cycle (60s) for SQLite
        
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
                var playerMetricsService = scope.ServiceProvider.GetRequiredService<PlayerMetricsWriteService>();
                var playerRoundsService = scope.ServiceProvider.GetRequiredService<PlayerRoundsWriteService>();
                var bfListApiService = scope.ServiceProvider.GetRequiredService<IBfListApiService>();
                
                // 1. Global timeout cleanup (only on every 2nd cycle to avoid too frequent DB operations)
                if (isEvenCycle)
                {
                    var timeoutStopwatch = Stopwatch.StartNew();
                    await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                    timeoutStopwatch.Stop();
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Session cleanup: {timeoutStopwatch.ElapsedMilliseconds}ms");
                }

                // 2. BF1942 stats
                var bf1942Stopwatch = Stopwatch.StartNew();
                await CollectTotalPlayersAsync(bfListApiService, "bf1942", CancellationToken.None);
                var bf1942ServersStopwatch = Stopwatch.StartNew();
                var bf1942Servers = await CollectServerStatsAsync(bfListApiService, playerTrackingService, "bf1942", isEvenCycle, CancellationToken.None);
                bf1942ServersStopwatch.Stop();
                bf1942Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BF1942 stats: {bf1942Stopwatch.ElapsedMilliseconds}ms (Servers: {bf1942ServersStopwatch.ElapsedMilliseconds}ms)");

                // 3. FH2 stats
                var fh2Stopwatch = Stopwatch.StartNew();
                await CollectFh2TotalPlayersAsync(bfListApiService, CancellationToken.None);
                var fh2ServersStopwatch = Stopwatch.StartNew();
                var fh2Servers = await CollectFh2ServerStatsAsync(bfListApiService, playerTrackingService, isEvenCycle, CancellationToken.None);
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

                // 5. Sync completed PlayerSessions to ClickHouse player_rounds (every 2nd cycle)
                if (isEvenCycle && _enableRoundSyncing)
                {
                    var roundsSyncStopwatch = Stopwatch.StartNew();
                    try
                    {
                        // Use incremental sync based on last synced timestamp (no explicit paging needed)
                        // The method internally handles paging efficiently for the incremental data
                        var result = await playerRoundsService.SyncCompletedSessionsAsync();
                        
                        roundsSyncStopwatch.Stop();
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync: {result.ProcessedCount} records ({roundsSyncStopwatch.ElapsedMilliseconds}ms)");
                    }
                    catch (Exception ex)
                    {
                        roundsSyncStopwatch.Stop();
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync failed: {ex.Message} ({roundsSyncStopwatch.ElapsedMilliseconds}ms)");
                    }
                }
                else if (isEvenCycle && !_enableRoundSyncing)
                {
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync: SKIPPED (disabled by configuration)");
                }
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

    private async Task CollectTotalPlayersAsync(IBfListApiService bfListApiService, string game, CancellationToken stoppingToken)
    {
        var stats = await bfListApiService.FetchLiveStatsAsync(game);
        
        if (game == "bf1942")
        {
            _totalPlayersGauge.Set(stats.Players);
        }
        else
        {
            _fh2TotalPlayersGauge.Set(stats.Players);
        }
    }

    private async Task<List<IGameServer>> CollectServerStatsAsync(IBfListApiService bfListApiService, PlayerTrackingService playerTrackingService, string game, bool enableSqliteStorage, CancellationToken stoppingToken)
    {
        var allServersObjects = await bfListApiService.FetchAllServersAsync(game);
        var allServers = allServersObjects.Cast<Bf1942ServerInfo>().ToList();

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
            
            // Only store to SQLite on every 2nd cycle (every 60s)
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

    private async Task CollectFh2TotalPlayersAsync(IBfListApiService bfListApiService, CancellationToken stoppingToken)
    {
        await CollectTotalPlayersAsync(bfListApiService, "fh2", stoppingToken);
    }

    private async Task<List<IGameServer>> CollectFh2ServerStatsAsync(IBfListApiService bfListApiService, PlayerTrackingService playerTrackingService, bool enableSqliteStorage, CancellationToken stoppingToken)
    {
        var allServersObjects = await bfListApiService.FetchAllServersAsync("fh2");
        var allServers = allServersObjects.Cast<Fh2ServerInfo>().ToList();

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
            
            // Only store to SQLite on every 2nd cycle (every 60s)
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
    }
}