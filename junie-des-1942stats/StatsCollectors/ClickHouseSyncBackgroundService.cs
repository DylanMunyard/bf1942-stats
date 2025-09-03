using System.Diagnostics;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Telemetry;

namespace junie_des_1942stats.StatsCollectors;

public class ClickHouseSyncBackgroundService : IHostedService, IDisposable
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<ClickHouseSyncBackgroundService> _logger;
    private readonly TimeSpan _syncInterval = TimeSpan.FromMinutes(1);
    private Timer? _timer;
    private int _isRunning = 0;
    private int _cycleCount = 0;

    private readonly bool _enablePlayerMetricsSyncing;
    private readonly bool _enableServerOnlineCountsSyncing;
    private readonly bool _enableRoundsSyncing;

    public ClickHouseSyncBackgroundService(IServiceScopeFactory scopeFactory, IConfiguration configuration, ILogger<ClickHouseSyncBackgroundService> logger)
    {
        _scopeFactory = scopeFactory;
        _configuration = configuration;
        _logger = logger;

        _enablePlayerMetricsSyncing = Environment.GetEnvironmentVariable("ENABLE_PLAYER_METRICS_SYNCING")?.ToLowerInvariant() == "true";
        _enableServerOnlineCountsSyncing = Environment.GetEnvironmentVariable("ENABLE_SERVER_ONLINE_COUNTS_SYNCING")?.ToLowerInvariant() == "true";
        _enableRoundsSyncing = Environment.GetEnvironmentVariable("ENABLE_CLICKHOUSE_ROUND_SYNCING")?.ToLowerInvariant() == "true";

        _logger.LogInformation("Config ENABLE_PLAYER_METRICS_SYNCING: {Enabled}", _enablePlayerMetricsSyncing);
        _logger.LogInformation("Config ENABLE_SERVER_ONLINE_COUNTS_SYNCING: {Enabled}", _enableServerOnlineCountsSyncing);
        _logger.LogInformation("Config ENABLE_CLICKHOUSE_ROUND_SYNCING: {Enabled}", _enableRoundsSyncing);

        var clickHouseReadUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_URL") ?? throw new InvalidOperationException("CLICKHOUSE_URL environment variable must be set");
        var clickHouseWriteUrl = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") ?? clickHouseReadUrl;
        var isWriteUrlSet = Environment.GetEnvironmentVariable("CLICKHOUSE_WRITE_URL") != null;

        _logger.LogInformation("ClickHouse Read URL: {ReadUrl}", clickHouseReadUrl);
        _logger.LogInformation("ClickHouse Write URL: {WriteUrl} {Source}", clickHouseWriteUrl, isWriteUrlSet ? "(custom)" : "(fallback to read URL)");
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ClickHouse sync service starting (1m intervals)...");
        _timer = new Timer(ExecuteSyncCycle, null, TimeSpan.Zero, _syncInterval);
        return Task.CompletedTask;
    }

    private async void ExecuteSyncCycle(object? state)
    {
        if (Interlocked.CompareExchange(ref _isRunning, 1, 0) != 0) return;

        var currentCycle = Interlocked.Increment(ref _cycleCount);
        using var activity = ActivitySources.ClickHouseSync.StartActivity("ClickHouseSync.Cycle");
        activity?.SetTag("cycle_number", currentCycle);

        var cycleStopwatch = Stopwatch.StartNew();
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();
            var metricsWriter = scope.ServiceProvider.GetRequiredService<PlayerMetricsWriteService>();
            var roundsWriter = scope.ServiceProvider.GetRequiredService<PlayerRoundsWriteService>();

            var now = DateTime.UtcNow;
            var from = now.AddMinutes(-60); // sync last 5 minutes window idempotently

            if (_enablePlayerMetricsSyncing)
            {
                var metricsStopwatch = Stopwatch.StartNew();
                var totalMetrics = 0;
                var windowStartMinute = new DateTime(from.Year, from.Month, from.Day, from.Hour, from.Minute, 0, DateTimeKind.Utc);
                var windowEndMinute = new DateTime(now.Year, now.Month, now.Day, now.Hour, now.Minute, 0, DateTimeKind.Utc);

                for (var minute = windowStartMinute; minute < windowEndMinute; minute = minute.AddMinutes(1))
                {
                    var minuteEnd = minute.AddMinutes(1);
                    DateTime? afterTs = null;
                    int? afterSessionId = null;
                    var deletedMinute = false;

                    while (true)
                    {
                        var batch = await LoadPlayerMetricsBatchAsync(dbContext, minute, minuteEnd, afterTs, afterSessionId, 10000);
                        if (batch.Count == 0)
                        {
                            break;
                        }

                        if (!deletedMinute)
                        {
                            var fromStr = minute.ToString("yyyy-MM-dd HH:mm:ss");
                            var toStr = minuteEnd.ToString("yyyy-MM-dd HH:mm:ss");
                            await metricsWriter.ExecuteCommandAsync($"ALTER TABLE player_metrics DELETE WHERE timestamp >= '{fromStr}' AND timestamp < '{toStr}'");
                            deletedMinute = true;
                        }

                        await metricsWriter.WritePlayerMetricsAsync(batch.Select(b => b.Metric));
                        totalMetrics += batch.Count;

                        var last = batch[^1];
                        afterTs = last.Timestamp;
                        afterSessionId = last.SessionId;

                        if (batch.Count < 10000)
                        {
                            break;
                        }
                    }
                }

                metricsStopwatch.Stop();
                _logger.LogInformation("ClickHouse player_metrics sync: {TotalMetrics} rows ({Duration}ms)", totalMetrics, metricsStopwatch.ElapsedMilliseconds);
                activity?.SetTag("player_metrics_count", totalMetrics);
                activity?.SetTag("player_metrics_duration_ms", metricsStopwatch.ElapsedMilliseconds);
            }

            if (_enableServerOnlineCountsSyncing)
            {
                var countsStopwatch = Stopwatch.StartNew();
                var counts = await ComputeServerOnlineCountsAsync(dbContext, from, now);
                if (counts.Count > 0)
                {
                    var fromStr = from.ToString("yyyy-MM-dd HH:mm:ss");
                    var toStr = now.ToString("yyyy-MM-dd HH:mm:ss");
                    await metricsWriter.ExecuteCommandAsync($"ALTER TABLE server_online_counts DELETE WHERE timestamp >= '{fromStr}' AND timestamp < '{toStr}'");
                    await metricsWriter.WriteServerOnlineCountsAsync(counts);
                }
                countsStopwatch.Stop();
                _logger.LogInformation("ClickHouse online_counts sync: {Count} rows ({Duration}ms)", counts.Count, countsStopwatch.ElapsedMilliseconds);
                activity?.SetTag("online_counts_count", counts.Count);
                activity?.SetTag("online_counts_duration_ms", countsStopwatch.ElapsedMilliseconds);
            }

            if (_enableRoundsSyncing)
            {
                var roundsStopwatch = Stopwatch.StartNew();
                var result = await roundsWriter.SyncCompletedSessionsAsync();
                roundsStopwatch.Stop();
                _logger.LogInformation("ClickHouse player_rounds sync: {ProcessedCount} records ({Duration}ms)", result.ProcessedCount, roundsStopwatch.ElapsedMilliseconds);
                activity?.SetTag("rounds_synced_count", result.ProcessedCount);
                activity?.SetTag("rounds_sync_duration_ms", roundsStopwatch.ElapsedMilliseconds);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error in ClickHouse sync cycle");
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"ClickHouse sync cycle failed: {ex.Message}");
        }
        finally
        {
            cycleStopwatch.Stop();
            activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
            Interlocked.Exchange(ref _isRunning, 0);
        }
    }

    private sealed class PlayerMetricWithKey
    {
        public PlayerMetric Metric { get; set; } = new PlayerMetric();
        public DateTime Timestamp { get; set; }
        public int SessionId { get; set; }
    }

    // Build player_metrics batches (keyset pagination by Timestamp, SessionId)
    private static async Task<List<PlayerMetricWithKey>> LoadPlayerMetricsBatchAsync(
        PlayerTrackerDbContext db,
        DateTime fromUtc,
        DateTime toUtc,
        DateTime? afterTimestamp,
        int? afterSessionId,
        int batchSize)
    {
        var baseQuery = from po in db.PlayerObservations
                        where po.Timestamp >= fromUtc && po.Timestamp < toUtc
                        join ps in db.PlayerSessions on po.SessionId equals ps.SessionId
                        join s in db.Servers on ps.ServerGuid equals s.Guid
                        join p in db.Players on ps.PlayerName equals p.Name
                        select new
                        {
                            po.Timestamp,
                            SessionId = ps.SessionId,
                            ServerGuid = s.Guid,
                            ServerName = s.Name,
                            PlayerName = ps.PlayerName,
                            po.Score,
                            po.Kills,
                            po.Deaths,
                            po.Ping,
                            TeamLabel = po.TeamLabel,
                            MapName = ps.MapName,
                            GameType = ps.GameType,
                            IsBot = p.AiBot
                        };

        if (afterTimestamp.HasValue)
        {
            var ts = afterTimestamp.Value;
            var sid = afterSessionId ?? 0;
            baseQuery = baseQuery.Where(x => x.Timestamp > ts || (x.Timestamp == ts && x.SessionId > sid));
        }

        var rows = await baseQuery
            .OrderBy(x => x.Timestamp)
            .ThenBy(x => x.SessionId)
            .Take(batchSize)
            .ToListAsync();

        return rows.Select(x => new PlayerMetricWithKey
        {
            Timestamp = x.Timestamp,
            SessionId = x.SessionId,
            Metric = new PlayerMetric
            {
                Timestamp = x.Timestamp,
                ServerGuid = x.ServerGuid,
                ServerName = x.ServerName,
                PlayerName = x.PlayerName,
                Score = x.Score,
                Kills = (ushort)Math.Max(0, x.Kills),
                Deaths = (ushort)Math.Max(0, x.Deaths),
                Ping = (ushort)Math.Max(0, x.Ping),
                TeamName = x.TeamLabel ?? "",
                MapName = x.MapName,
                GameType = x.GameType,
                IsBot = x.IsBot
            }
        }).ToList();
    }

    // Estimate server_online_counts by computing active non-bot sessions per minute in window
    // Uses Round table to get the correct map for each specific minute, avoiding old map data issues
    // This prevents the issue where players staying connected through map changes would appear on the old map
    private static async Task<List<ServerOnlineCount>> ComputeServerOnlineCountsAsync(PlayerTrackerDbContext db, DateTime fromUtc, DateTime toUtc)
    {
        // Get all rounds that overlap with our time window to determine the correct map for each minute
        var overlappingRounds = await db.Rounds
            .Where(r => r.StartTime < toUtc && (r.EndTime == null || r.EndTime > fromUtc))
            .Select(r => new { r.ServerGuid, r.ServerName, r.GameType, r.MapName, r.StartTime, r.EndTime })
            .ToListAsync();

        // Also get server info for the Game field
        var serverGuids = overlappingRounds.Select(r => r.ServerGuid).Distinct().ToList();
        var servers = await db.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .Select(s => new { s.Guid, s.Game })
            .ToDictionaryAsync(s => s.Guid, s => s);

        // Load sessions that overlap window
        var sessions = await db.PlayerSessions
            .Where(ps => ps.LastSeenTime >= fromUtc.AddMinutes(-60)
                         && ps.StartTime <= toUtc
                         && !ps.Player.AiBot)
            .Select(ps => new { ps.PlayerName, ps.ServerGuid, ps.StartTime, ps.LastSeenTime, ps.IsActive, ps.MapName })
            .ToListAsync();

        // Build minute buckets
        var buckets = new Dictionary<(DateTime minute, string serverGuid, string mapName), ushort>();

        foreach (var s in sessions)
        {
            var startMinute = new DateTime(Math.Max(s.StartTime.Ticks, fromUtc.Ticks), DateTimeKind.Utc);
            var endMinuteExclusive = new DateTime(Math.Min(s.LastSeenTime.Ticks, toUtc.Ticks), DateTimeKind.Utc);

            // Snap to minute boundaries
            startMinute = new DateTime(startMinute.Year, startMinute.Month, startMinute.Day, startMinute.Hour, startMinute.Minute, 0, DateTimeKind.Utc);
            endMinuteExclusive = new DateTime(endMinuteExclusive.Year, endMinuteExclusive.Month, endMinuteExclusive.Day, endMinuteExclusive.Hour, endMinuteExclusive.Minute, 0, DateTimeKind.Utc);

            for (var t = startMinute; t <= endMinuteExclusive; t = t.AddMinutes(1))
            {
                // Find the round that was active at this specific minute
                var activeRound = overlappingRounds.FirstOrDefault(r => 
                    r.ServerGuid == s.ServerGuid && 
                    r.StartTime <= t && 
                    (r.EndTime == null || r.EndTime > t));

                if (activeRound != null)
                {
                    // Use the map from the active round at this specific minute
                    var key = (t, s.ServerGuid, activeRound.MapName);
                    if (!buckets.TryGetValue(key, out var count))
                    {
                        buckets[key] = 1;
                    }
                    else
                    {
                        buckets[key] = (ushort)(count + 1);
                    }
                }
            }
        }

        // Build results using round and server information
        var results = new List<ServerOnlineCount>();
        foreach (var kvp in buckets)
        {
            var key = kvp.Key;
            var count = kvp.Value;
            
            // Get server info for name and game
            if (!servers.TryGetValue(key.serverGuid, out var serverInfo)) continue;
            
            // Find the round info for this specific minute and map
            var roundInfo = overlappingRounds.FirstOrDefault(r => 
                r.ServerGuid == key.serverGuid && 
                r.MapName == key.mapName &&
                r.StartTime <= key.minute && 
                (r.EndTime == null || r.EndTime > key.minute));
            
            if (roundInfo != null)
            {
                results.Add(new ServerOnlineCount
                {
                    Timestamp = key.minute,
                    ServerGuid = key.serverGuid,
                    ServerName = roundInfo.ServerName,
                    PlayersOnline = count,
                    MapName = key.mapName, // This is the map from the active round at this minute
                    Game = serverInfo.Game
                });
            }
        }

        return results;
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("ClickHouse sync service stopping...");
        _timer?.Change(Timeout.Infinite, 0);
        return Task.CompletedTask;
    }

    public void Dispose()
    {
        _timer?.Dispose();
    }
}


