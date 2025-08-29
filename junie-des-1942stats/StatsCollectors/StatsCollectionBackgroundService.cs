using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Services;
using junie_des_1942stats.Telemetry;
using Serilog.Context;

namespace junie_des_1942stats.StatsCollectors;

public class StatsCollectionBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly TimeSpan _collectionInterval = TimeSpan.FromSeconds(30);
    private Timer? _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;

    // Configuration setting for round syncing
    private readonly bool _enableClickhouseSyncing;
    private readonly bool _enablePlayerMetricsSyncing;
    private readonly bool _enableServerOnlineCountsSyncing;



    public StatsCollectionBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;

        // Check environment variable for round syncing - default to false (disabled)
        _enableClickhouseSyncing = Environment.GetEnvironmentVariable("ENABLE_ROUND_SYNCING")?.ToLowerInvariant() == "true";
        _enablePlayerMetricsSyncing = Environment.GetEnvironmentVariable("ENABLE_PLAYER_METRICS_SYNCING")?.ToLowerInvariant() == "true";
        _enableServerOnlineCountsSyncing = Environment.GetEnvironmentVariable("ENABLE_SERVER_ONLINE_COUNTS_SYNCING")?.ToLowerInvariant() == "true";

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        var isWriteUrlSet = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") != null;

        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse Read URL: {clickHouseReadUrl}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse Write URL: {clickHouseWriteUrl} {(isWriteUrlSet ? "(custom)" : "(fallback to read URL)")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Round syncing to ClickHouse: {(_enableClickhouseSyncing ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Player metrics syncing to ClickHouse: {(_enablePlayerMetricsSyncing ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Server online counts syncing to ClickHouse: {(_enableServerOnlineCountsSyncing ? "ENABLED" : "DISABLED")}");
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] To avoid writes to production: Set CLICKHOUSE_WRITE_URL to dev instance or leave syncing flags=false");
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

    private async void ExecuteCollectionCycle(object? state)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Previous collection still running, skipping...");
            return;
        }

        var currentCycle = Interlocked.Increment(ref _cycleCount);

        using var activity = ActivitySources.StatsCollection.StartActivity("StatsCollection.Cycle");
        activity?.SetTag("cycle_number", currentCycle);
        activity?.SetTag("collection_interval_seconds", _collectionInterval.TotalSeconds);
        activity?.SetTag("enable_round_syncing", _enableClickhouseSyncing);
        activity?.SetTag("enable_player_metrics_syncing", _enablePlayerMetricsSyncing);
        activity?.SetTag("enable_server_online_counts_syncing", _enableServerOnlineCountsSyncing);

        var cycleStopwatch = Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Starting stats collection cycle #{currentCycle}...");

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

                // 1. Global timeout cleanup
                var timeoutStopwatch = Stopwatch.StartNew();
                await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                timeoutStopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Session cleanup: {timeoutStopwatch.ElapsedMilliseconds}ms");

                // 2. BF1942 stats
                var bf1942Stopwatch = Stopwatch.StartNew();
                var bf1942ServersStopwatch = Stopwatch.StartNew();
                var bf1942Servers = await CollectBf1942ServerStatsAsync(bfListApiService, playerTrackingService, "bf1942", CancellationToken.None);
                bf1942ServersStopwatch.Stop();
                bf1942Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BF1942 stats: {bf1942Stopwatch.ElapsedMilliseconds}ms (Servers: {bf1942ServersStopwatch.ElapsedMilliseconds}ms)");

                // 3. FH2 stats
                var fh2Stopwatch = Stopwatch.StartNew();
                var fh2ServersStopwatch = Stopwatch.StartNew();
                var fh2Servers = await CollectFh2ServerStatsAsync(bfListApiService, playerTrackingService, CancellationToken.None);
                fh2ServersStopwatch.Stop();
                fh2Stopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] FH2 stats: {fh2Stopwatch.ElapsedMilliseconds}ms (Servers: {fh2ServersStopwatch.ElapsedMilliseconds}ms)");

                // BFV stats
                var bfvietnamStopwatch = Stopwatch.StartNew();
                var bfvietnamServersStopwatch = Stopwatch.StartNew();
                var bfvietnamServers = await CollectBfvietnamServerStatsAsync(bfListApiService, playerTrackingService, CancellationToken.None);
                bfvietnamServersStopwatch.Stop();
                bfvietnamStopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] BFV stats: {bfvietnamStopwatch.ElapsedMilliseconds}ms (Servers: {bfvietnamServersStopwatch.ElapsedMilliseconds}ms)");

                // 4. Batch store all player metrics to ClickHouse
                var allServers = new List<IGameServer>();
                allServers.AddRange(bf1942Servers);
                allServers.AddRange(fh2Servers);
                allServers.AddRange(bfvietnamServers);
                var timestamp = DateTime.UtcNow;

                if (_enablePlayerMetricsSyncing)
                {
                    var clickHouseStopwatch = Stopwatch.StartNew();
                    await playerMetricsService.StoreBatchedPlayerMetricsAsync(allServers, timestamp);
                    clickHouseStopwatch.Stop();
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse batch storage: {clickHouseStopwatch.ElapsedMilliseconds}ms ({allServers.Count} servers)");
                    activity?.SetTag("player_metrics_duration_ms", clickHouseStopwatch.ElapsedMilliseconds);
                }
                else
                {
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse batch storage: SKIPPED (disabled by configuration)");
                }

                // 5. Store server online counts alongside detailed metrics
                if (_enableServerOnlineCountsSyncing)
                {
                    var onlineCountsStopwatch = Stopwatch.StartNew();
                    try
                    {
                        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
                        await playerMetricsService.StoreServerOnlineCountsAsync(allServers, timestamp, dbContext);
                        onlineCountsStopwatch.Stop();
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse online counts: {onlineCountsStopwatch.ElapsedMilliseconds}ms ({allServers.Count} servers)");
                        activity?.SetTag("online_counts_duration_ms", onlineCountsStopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        onlineCountsStopwatch.Stop();
                        // Log but don't fail - this is expected during transition period
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Online counts storage failed (expected during transition): {ex.Message} ({onlineCountsStopwatch.ElapsedMilliseconds}ms)");
                        activity?.SetTag("online_counts_error", ex.Message);
                        // Don't set activity status as error since this is expected during transition
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] ClickHouse online counts: SKIPPED (disabled by configuration)");
                }

                activity?.SetTag("total_servers_processed", allServers.Count);
                activity?.SetTag("bf1942_servers_processed", bf1942Servers.Count);
                activity?.SetTag("fh2_servers_processed", fh2Servers.Count);
                activity?.SetTag("bfvietnam_servers_processed", bfvietnamServers.Count);

                // 6. Sync completed PlayerSessions to ClickHouse player_rounds
                if (_enableClickhouseSyncing)
                {
                    var roundsSyncStopwatch = Stopwatch.StartNew();
                    try
                    {
                        // Use incremental sync based on last synced timestamp (no explicit paging needed)
                        // The method internally handles paging efficiently for the incremental data
                        var result = await playerRoundsService.SyncCompletedSessionsAsync();

                        roundsSyncStopwatch.Stop();
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync: {result.ProcessedCount} records ({roundsSyncStopwatch.ElapsedMilliseconds}ms)");
                        activity?.SetTag("rounds_synced_count", result.ProcessedCount);
                        activity?.SetTag("rounds_sync_duration_ms", roundsSyncStopwatch.ElapsedMilliseconds);
                    }
                    catch (Exception ex)
                    {
                        roundsSyncStopwatch.Stop();
                        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync failed: {ex.Message} ({roundsSyncStopwatch.ElapsedMilliseconds}ms)");
                        activity?.SetTag("rounds_sync_error", ex.Message);
                        activity?.SetStatus(ActivityStatusCode.Error, $"PlayerRounds sync failed: {ex.Message}");
                    }
                }
                else
                {
                    Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] PlayerRounds sync: SKIPPED (disabled by configuration)");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Error in collection cycle: {ex}");
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Collection cycle failed: {ex.Message}");
        }
        finally
        {
            cycleStopwatch.Stop();
            Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Cycle #{currentCycle} completed in {cycleStopwatch.ElapsedMilliseconds}ms");
            activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private async Task<List<IGameServer>> CollectBf1942ServerStatsAsync(IBfListApiService bfListApiService, PlayerTrackingService playerTrackingService, string game, CancellationToken stoppingToken)
    {
        var allServersObjects = await bfListApiService.FetchAllServersAsync(game);
        var allServers = allServersObjects.Cast<Bf1942ServerInfo>().ToList();

        var timestamp = DateTime.UtcNow;
        var gameServerAdapters = new List<IGameServer>();

        foreach (var server in allServers)
        {
            // Create adapter for ClickHouse batching
            var adapter = new Bf1942ServerAdapter(server);
            gameServerAdapters.Add(adapter);

            // Store to SQLite every cycle
            await playerTrackingService.TrackPlayersFromServerInfo(adapter, timestamp, "bf1942");
        }

        return gameServerAdapters;
    }

    private async Task<List<IGameServer>> CollectBfvietnamServerStatsAsync(IBfListApiService bfListApiService, PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var allServersObjects = await bfListApiService.FetchAllServersAsync("bfvietnam");
        var allServers = allServersObjects.Cast<BfvietnamServerInfo>().ToList();

        var timestamp = DateTime.UtcNow;
        var gameServerAdapters = new List<IGameServer>();

        foreach (var server in allServers)
        {
            // Create adapter for ClickHouse batching
            var adapter = new BfvietnamServerAdapter(server);
            gameServerAdapters.Add(adapter);

            // Store to SQLite every cycle
            await playerTrackingService.TrackPlayersFromServerInfo(adapter, timestamp, "bfvietnam");
        }

        return gameServerAdapters;
    }

    private async Task<List<IGameServer>> CollectFh2ServerStatsAsync(IBfListApiService bfListApiService, PlayerTrackingService playerTrackingService, CancellationToken stoppingToken)
    {
        var allServersObjects = await bfListApiService.FetchAllServersAsync("fh2");
        var allServers = allServersObjects.Cast<Fh2ServerInfo>().ToList();

        var timestamp = DateTime.UtcNow;
        var gameServerAdapters = new List<IGameServer>();

        foreach (var server in allServers)
        {
            // Create adapter for ClickHouse batching
            var adapter = new Fh2ServerAdapter(server);
            gameServerAdapters.Add(adapter);

            // Store to SQLite every cycle
            await playerTrackingService.TrackPlayersFromServerInfo(adapter, timestamp, "fh2");
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