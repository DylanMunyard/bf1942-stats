using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using api.PlayerTracking;
using api.Bflist;
using api.Bflist.Models;
using api.Telemetry;
using Serilog.Context;

namespace api.StatsCollectors;

public class StatsCollectionBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration) : IHostedService, IDisposable
{

    // Read interval from config, default to 30 seconds
    private readonly TimeSpan _collectionInterval = TimeSpan.FromSeconds(configuration.GetValue<int?>("STATS_COLLECTION_INTERVAL_SECONDS") ?? 30);
    private Timer? _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Stats collection service starting ({_collectionInterval.TotalSeconds}s intervals)...");

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
        activity?.SetTag("bulk_operation", "true");

        var cycleStopwatch = Stopwatch.StartNew();
        Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Starting stats collection cycle #{currentCycle}...");

        try
        {
            // Add context properties to mark all logs in this scope as coming from stats collection
            using (LogContext.PushProperty("operation_type", "stats_collection"))
            using (LogContext.PushProperty("bulk_operation", true))
            using (LogContext.PushProperty("cycle_number", currentCycle))
            using (var scope = scopeFactory.CreateScope())
            {
                var playerTrackingService = scope.ServiceProvider.GetRequiredService<PlayerTrackingService>();
                var bfListApiService = scope.ServiceProvider.GetRequiredService<IBfListApiService>();

                // 1. Global timeout cleanup
                var timeoutStopwatch = Stopwatch.StartNew();
                await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                await playerTrackingService.MarkOfflineServersAsync(DateTime.UtcNow);
                timeoutStopwatch.Stop();
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Session & server cleanup: {timeoutStopwatch.ElapsedMilliseconds}ms");

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

                // Removed ClickHouse batch storage. Metrics syncing handled by ClickHouseSyncBackgroundService.

                // 5. Store server online counts alongside detailed metrics
                // Removed ClickHouse online counts storage. Online counts syncing handled by ClickHouseSyncBackgroundService.

                activity?.SetTag("total_servers_processed", allServers.Count);
                activity?.SetTag("bf1942_servers_processed", bf1942Servers.Count);
                activity?.SetTag("fh2_servers_processed", fh2Servers.Count);
                activity?.SetTag("bfvietnam_servers_processed", bfvietnamServers.Count);

                // Removed PlayerRounds sync to ClickHouse. Rounds syncing handled by ClickHouseSyncBackgroundService.
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
