using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using api.PlayerTracking;
using api.Bflist;
using api.Bflist.Models;
using api.Telemetry;
using api.Data.Entities;
using NodaTime;
using Serilog.Context;

namespace api.StatsCollectors;

public class StatsCollectionBackgroundService(
    IServiceScopeFactory scopeFactory,
    IConfiguration configuration,
    ILogger<StatsCollectionBackgroundService> logger)
    : IHostedService, IDisposable
{
    private readonly TimeSpan _collectionInterval = TimeSpan.FromSeconds(configuration.GetValue<int?>("STATS_COLLECTION_INTERVAL_SECONDS") ?? 30);
    private Timer? _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;

    public Task StartAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stats collection service starting ({IntervalSeconds}s intervals)", _collectionInterval.TotalSeconds);

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
            return; // Previous collection still running
        }

        // Clear any inherited activity context to prevent Timer callbacks from
        // accidentally correlating with unrelated HTTP request traces.
        Activity.Current = null;

        var currentCycle = Interlocked.Increment(ref _cycleCount);

        // Use explicit default parentContext to create a root activity with a fresh traceId.
        // Without this, StartActivity() could inherit Activity.Current from the thread pool,
        // causing background cycles to share a traceId with unrelated HTTP requests.
        using var activity = ActivitySources.StatsCollection.StartActivity(
            "StatsCollection.Cycle",
            ActivityKind.Internal,
            parentContext: default);
        activity?.SetTag("cycle_number", currentCycle);
        activity?.SetTag("collection_interval_seconds", _collectionInterval.TotalSeconds);
        activity?.SetTag("bulk_operation", "true");

        var cycleStopwatch = Stopwatch.StartNew();

        try
        {
            using (LogContext.PushProperty("bulk_operation", true))
            using (var scope = scopeFactory.CreateScope())
            {
                var playerTrackingService = scope.ServiceProvider.GetRequiredService<PlayerTrackingService>();
                var bfListApiService = scope.ServiceProvider.GetRequiredService<IBfListApiService>();

                // 1. Global timeout cleanup
                await playerTrackingService.CloseAllTimedOutSessionsAsync(DateTime.UtcNow);
                await playerTrackingService.MarkOfflineServersAsync(DateTime.UtcNow);

                // 2. Collect stats from all games
                var bf1942Servers = await CollectBf1942ServerStatsAsync(bfListApiService, playerTrackingService, "bf1942", CancellationToken.None);
                var fh2Servers = await CollectFh2ServerStatsAsync(bfListApiService, playerTrackingService, CancellationToken.None);
                var bfvietnamServers = await CollectBfvietnamServerStatsAsync(bfListApiService, playerTrackingService, CancellationToken.None);

                // 3. Collect all servers
                var allServers = new List<IGameServer>();
                allServers.AddRange(bf1942Servers);
                allServers.AddRange(fh2Servers);
                allServers.AddRange(bfvietnamServers);
                var timestamp = DateTime.UtcNow;

                // 4. Store server online counts to SQLite for analytics
                var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
                await UpsertServerOnlineCountsAsync(dbContext, bf1942Servers, "bf1942", timestamp);
                await UpsertServerOnlineCountsAsync(dbContext, fh2Servers, "fh2", timestamp);
                await UpsertServerOnlineCountsAsync(dbContext, bfvietnamServers, "bfvietnam", timestamp);

                activity?.SetTag("total_servers_processed", allServers.Count);
                activity?.SetTag("bf1942_servers_processed", bf1942Servers.Count);
                activity?.SetTag("fh2_servers_processed", fh2Servers.Count);
                activity?.SetTag("bfvietnam_servers_processed", bfvietnamServers.Count);

                // Calculate active player count for metrics
                var activePlayers = allServers.Sum(s => s.Players.Count());
                BackgroundJobMetrics.SetActivePlayers(activePlayers);

                // Emit metrics
                BackgroundJobMetrics.ServersProcessed.Add(allServers.Count,
                    new KeyValuePair<string, object?>("game", "all"));

                cycleStopwatch.Stop();
                activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);

                // Log summary every 10th cycle to reduce noise (every ~5 minutes at 30s intervals)
                if (currentCycle % 10 == 0)
                {
                    logger.LogInformation(
                        "Stats collection cycle #{Cycle}: {TotalServers} servers (BF1942={Bf1942}, FH2={Fh2}, BFV={Bfv}), {Players} players in {Duration}ms",
                        currentCycle, allServers.Count, bf1942Servers.Count, fh2Servers.Count, bfvietnamServers.Count,
                        activePlayers, cycleStopwatch.ElapsedMilliseconds);
                }
            }
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error in stats collection cycle #{Cycle}", currentCycle);
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Collection cycle failed: {ex.Message}");
        }
        finally
        {
            if (cycleStopwatch.IsRunning) cycleStopwatch.Stop();

            // Emit job execution metrics for correlation with memory/CPU spikes
            BackgroundJobMetrics.JobExecutions.Add(1,
                new KeyValuePair<string, object?>("job", "stats_collection"));
            BackgroundJobMetrics.JobDuration.Record(cycleStopwatch.Elapsed.TotalSeconds,
                new KeyValuePair<string, object?>("job", "stats_collection"));

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
            // Create adapter for batching
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
            // Create adapter for batching
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
            // Create adapter for batching
            var adapter = new Fh2ServerAdapter(server);
            gameServerAdapters.Add(adapter);

            // Store to SQLite every cycle
            await playerTrackingService.TrackPlayersFromServerInfo(adapter, timestamp, "fh2");
        }

        return gameServerAdapters;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        logger.LogInformation("Stats collection service stopping");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }

    private static async Task UpsertServerOnlineCountsAsync(
        PlayerTrackerDbContext dbContext,
        List<IGameServer> servers,
        string game,
        DateTime timestamp)
    {
        // Truncate timestamp to hour
        var hourTimestamp = Instant.FromDateTimeUtc(
            new DateTime(timestamp.Year, timestamp.Month, timestamp.Day, timestamp.Hour, 0, 0, DateTimeKind.Utc));

        // Deduplicate servers by GUID within this batch to avoid EF Core tracking conflicts
        // (e.g., when a server restarts and appears twice in the same collection cycle)
        var uniqueServers = servers
            .GroupBy(s => s.Guid)
            .Select(g => g.First())
            .ToList();

        foreach (var server in uniqueServers)
        {
            var playersOnline = server.Players.Count();

            // Find existing record for this server + hour
            var existing = await dbContext.ServerOnlineCounts
                .FirstOrDefaultAsync(s =>
                    s.ServerGuid == server.Guid &&
                    s.HourTimestamp == hourTimestamp);

            if (existing == null)
            {
                // Insert new record
                var newRecord = new ServerOnlineCount
                {
                    ServerGuid = server.Guid,
                    HourTimestamp = hourTimestamp,
                    Game = game,
                    AvgPlayers = playersOnline,
                    PeakPlayers = playersOnline,
                    SampleCount = 1
                };
                dbContext.ServerOnlineCounts.Add(newRecord);
            }
            else
            {
                // Update running average and peak
                existing.AvgPlayers = (existing.AvgPlayers * existing.SampleCount + playersOnline) / (existing.SampleCount + 1);
                existing.PeakPlayers = Math.Max(existing.PeakPlayers, playersOnline);
                existing.SampleCount++;
            }
        }

        await dbContext.SaveChangesAsync();
    }
}
