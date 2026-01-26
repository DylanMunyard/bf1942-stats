using System.Diagnostics;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Backfills aggregate tables from historical PlayerSessions data.
/// Uses SQL-native aggregation for efficiency - no need to load all sessions into memory.
/// Processes players in tiers based on recent activity to prioritize active players.
/// </summary>
public class AggregateBackfillBackgroundService(
    IServiceScopeFactory scopeFactory,
    api.Services.IAggregateConcurrencyService concurrency,
    ILogger<AggregateBackfillBackgroundService> logger,
    IClock clock
) : IAggregateBackfillBackgroundService
{
    /// <summary>
    /// Batch size for processing players. Smaller batches = less memory, more DB roundtrips.
    /// </summary>
    private const int PlayerBatchSize = 100;

    /// <summary>
    /// Tier definitions: (tier number, days back, description)
    /// </summary>
    private static readonly (int Tier, int DaysBack, string Description)[] Tiers =
    [
        (1, 7, "active within 7 days"),
        (2, 30, "active within 30 days"),
        (3, 90, "active within 90 days"),
        (4, int.MaxValue, "all remaining players")
    ];

    public async Task RunAsync(CancellationToken ct = default)
    {
        logger.LogInformation("Starting full aggregate backfill (all tiers)");

        foreach (var (tier, _, _) in Tiers)
        {
            if (ct.IsCancellationRequested) break;
            await RunTierAsync(tier, ct);
        }

        logger.LogInformation("Full aggregate backfill completed");
    }

    public async Task RunTierAsync(int tier, CancellationToken ct = default)
    {
        if (tier < 1 || tier > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(tier), "Tier must be between 1 and 4");
        }

        using var activity = ActivitySources.SqliteAnalytics.StartActivity($"AggregateBackfill.Tier{tier}");
        var stopwatch = Stopwatch.StartNew();

        var tierInfo = Tiers[tier - 1];
        logger.LogInformation("Starting tier {Tier} backfill: {Description}", tier, tierInfo.Description);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        await concurrency.ExecuteWithPlayerAggregatesLockAsync(async (c) =>
        {
            try
            {
                var now = clock.GetCurrentInstant();
                var nowUtc = now.ToDateTimeUtc();
                var nowIso = InstantPattern.ExtendedIso.Format(now);

                // First, get all player names for this tier
                var playerNames = await GetPlayerNamesForTierAsync(dbContext, tier, nowUtc, c);
                logger.LogInformation("Tier {Tier}: Found {Count} players to process", tier, playerNames.Count);

                if (playerNames.Count == 0)
                {
                    logger.LogInformation("Tier {Tier}: No players to process", tier);
                    return;
                }

                // Process players in batches
                var totalBatches = (playerNames.Count + PlayerBatchSize - 1) / PlayerBatchSize;
                var lifetimeCount = 0;
                var serverStatsCount = 0;
                var mapStatsCount = 0;
                var bestScoresCount = 0;
                var processedPlayers = 0;
                var batchStopwatch = new Stopwatch();

                logger.LogInformation(
                    "Tier {Tier}: Starting backfill of {TotalPlayers} players in {TotalBatches} batches (batch size: {BatchSize})",
                    tier, playerNames.Count, totalBatches, PlayerBatchSize);

                for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    if (c.IsCancellationRequested) break;

                    var batchPlayers = playerNames
                        .Skip(batchIndex * PlayerBatchSize)
                        .Take(PlayerBatchSize)
                        .ToList();

                    batchStopwatch.Restart();

                    var batchLifetime = await BackfillMonthlyStatsAsync(dbContext, batchPlayers, nowIso, c);
                    var batchServerStats = await BackfillServerStatsAsync(dbContext, batchPlayers, nowIso, c);
                    var batchMapStats = await BackfillMapStatsAsync(dbContext, batchPlayers, nowIso, c);
                    var batchBestScores = await BackfillBestScoresAsync(dbContext, batchPlayers, nowUtc, c);

                    batchStopwatch.Stop();

                    lifetimeCount += batchLifetime;
                    serverStatsCount += batchServerStats;
                    mapStatsCount += batchMapStats;
                    bestScoresCount += batchBestScores;
                    processedPlayers += batchPlayers.Count;

                    var percentComplete = (processedPlayers * 100.0) / playerNames.Count;
                    var batchRecordsCreated = batchLifetime + batchServerStats + batchMapStats + batchBestScores;

                    logger.LogInformation(
                        "Tier {Tier}: Batch {Batch}/{Total} completed in {BatchMs}ms - {PlayersProcessed}/{TotalPlayers} players ({Percent:F1}%) - {BatchRecords} records this batch",
                        tier, batchIndex + 1, totalBatches, batchStopwatch.ElapsedMilliseconds,
                        processedPlayers, playerNames.Count, percentComplete, batchRecordsCreated);
                }

                stopwatch.Stop();

                var totalRecords = lifetimeCount + serverStatsCount + mapStatsCount + bestScoresCount;
                var playersPerSecond = stopwatch.ElapsedMilliseconds > 0 ? (playerNames.Count * 1000.0) / stopwatch.ElapsedMilliseconds : 0;
                var recordsPerSecond = stopwatch.ElapsedMilliseconds > 0 ? (totalRecords * 1000.0) / stopwatch.ElapsedMilliseconds : 0;

                logger.LogInformation(
                    "Tier {Tier} summary: {TotalPlayers} players, {TotalRecords} total records created",
                    tier, playerNames.Count, totalRecords);
                logger.LogInformation(
                    "Tier {Tier} breakdown: Lifetime={Lifetime}, ServerStats={ServerStats}, MapStats={MapStats}, BestScores={BestScores}",
                    tier, lifetimeCount, serverStatsCount, mapStatsCount, bestScoresCount);
                logger.LogInformation(
                    "Tier {Tier} throughput: {PlayersPerSec:F1} players/sec, {RecordsPerSec:F1} records/sec",
                    tier, playersPerSecond, recordsPerSecond);
                activity?.SetTag("result.players_processed", playerNames.Count);
                activity?.SetTag("result.total_records", totalRecords);
                activity?.SetTag("result.lifetime_stats", lifetimeCount);
                activity?.SetTag("result.server_stats", serverStatsCount);
                activity?.SetTag("result.map_stats", mapStatsCount);
                activity?.SetTag("result.best_scores", bestScoresCount);
                activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("result.players_per_sec", playersPerSecond);
                activity?.SetTag("result.records_per_sec", recordsPerSecond);

                logger.LogInformation(
                    "Tier {Tier} backfill completed in {Duration}ms",
                    tier, stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Failed to complete tier {Tier} backfill", tier);
                throw;
            }
        }, ct);
    }

    public async Task RunForPlayersAsync(IEnumerable<string> playerNames, CancellationToken ct = default)
    {
        var playerList = playerNames.ToList();
        if (playerList.Count == 0)
        {
            logger.LogInformation("RunForPlayersAsync: No players to process");
            return;
        }

        using var activity = ActivitySources.SqliteAnalytics.StartActivity("AggregateBackfill.ForPlayers");
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Starting aggregate backfill for {PlayerCount} specific players", playerList.Count);

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        await concurrency.ExecuteWithPlayerAggregatesLockAsync(async (c) =>
        {
            try
            {
                var now = clock.GetCurrentInstant();
                var nowUtc = now.ToDateTimeUtc();
                var nowIso = InstantPattern.ExtendedIso.Format(now);

                // Process players in batches
                var totalBatches = (playerList.Count + PlayerBatchSize - 1) / PlayerBatchSize;
                var lifetimeCount = 0;
                var serverStatsCount = 0;
                var mapStatsCount = 0;
                var bestScoresCount = 0;

                for (var batchIndex = 0; batchIndex < totalBatches; batchIndex++)
                {
                    if (c.IsCancellationRequested) break;

                    var batchPlayers = playerList
                        .Skip(batchIndex * PlayerBatchSize)
                        .Take(PlayerBatchSize)
                        .ToList();

                    lifetimeCount += await BackfillMonthlyStatsAsync(dbContext, batchPlayers, nowIso, c);
                    serverStatsCount += await BackfillServerStatsAsync(dbContext, batchPlayers, nowIso, c);
                    mapStatsCount += await BackfillMapStatsAsync(dbContext, batchPlayers, nowIso, c);
                    bestScoresCount += await BackfillBestScoresAsync(dbContext, batchPlayers, nowUtc, c);
                }

                stopwatch.Stop();

                var totalRecords = lifetimeCount + serverStatsCount + mapStatsCount + bestScoresCount;
                logger.LogInformation(
                    "Completed aggregate backfill for {PlayerCount} players: {TotalRecords} records in {Duration}ms",
                    playerList.Count, totalRecords, stopwatch.ElapsedMilliseconds);

                activity?.SetTag("result.players_processed", playerList.Count);
                activity?.SetTag("result.total_records", totalRecords);
                activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Failed to complete backfill for {PlayerCount} players", playerList.Count);
                throw;
            }
        }, ct);
    }

    private async Task<List<string>> GetPlayerNamesForTierAsync(
        PlayerTrackerDbContext dbContext,
        int tier,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var tierWhereClause = BuildTierWhereClause(tier, nowUtc);

        // Query distinct player names for this tier
        // Uses the PlayerName index and filters by LastSeenTime
        var sql = $@"
            SELECT DISTINCT ps.PlayerName
            FROM PlayerSessions ps
            INNER JOIN Players p ON ps.PlayerName = p.Name
            WHERE {tierWhereClause}
              AND p.AiBot = 0
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            ORDER BY ps.PlayerName";

        return await dbContext.Database
            .SqlQueryRaw<string>(sql)
            .ToListAsync(ct);
    }

    /// <summary>
    /// Builds a parameterized IN clause for player names.
    /// Returns (sql_fragment, parameters) where sql_fragment is "ps.PlayerName IN (@p0, @p1, ...)"
    /// </summary>
    private static (string SqlFragment, object[] Parameters) BuildPlayerNamesFilter(List<string> playerNames, int parameterOffset = 0)
    {
        var placeholders = string.Join(", ", playerNames.Select((_, i) => $"@p{parameterOffset + i}"));
        var sqlFragment = $"ps.PlayerName IN ({placeholders})";
        var parameters = playerNames.Cast<object>().ToArray();
        return (sqlFragment, parameters);
    }

    /// <summary>
    /// Builds a parameterized IN clause for player names (without table alias).
    /// Returns (sql_fragment, parameters) where sql_fragment is "PlayerName IN (@p0, @p1, ...)"
    /// </summary>
    private static (string SqlFragment, object[] Parameters) BuildPlayerNamesFilterNoAlias(List<string> playerNames, int parameterOffset = 0)
    {
        var placeholders = string.Join(", ", playerNames.Select((_, i) => $"@p{parameterOffset + i}"));
        var sqlFragment = $"PlayerName IN ({placeholders})";
        var parameters = playerNames.Cast<object>().ToArray();
        return (sqlFragment, parameters);
    }

    private static string BuildTierWhereClause(int tier, DateTime nowUtc)
    {
        if (tier == 1)
        {
            var cutoff = nowUtc.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss");
            return $"ps.LastSeenTime >= '{cutoff}'";
        }

        if (tier == 2)
        {
            var cutoff30 = nowUtc.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss");
            var cutoff7 = nowUtc.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss");
            return $"ps.LastSeenTime >= '{cutoff30}' AND ps.LastSeenTime < '{cutoff7}'";
        }

        if (tier == 3)
        {
            var cutoff90 = nowUtc.AddDays(-90).ToString("yyyy-MM-dd HH:mm:ss");
            var cutoff30 = nowUtc.AddDays(-30).ToString("yyyy-MM-dd HH:mm:ss");
            return $"ps.LastSeenTime >= '{cutoff90}' AND ps.LastSeenTime < '{cutoff30}'";
        }
        else
        {
            var cutoff90 = nowUtc.AddDays(-90).ToString("yyyy-MM-dd HH:mm:ss");
            return $"ps.LastSeenTime < '{cutoff90}'";
        }
    }

    private async Task<int> BackfillMonthlyStatsAsync(
        PlayerTrackerDbContext dbContext,
        List<string> batchPlayers,
        string nowIso,
        CancellationToken ct)
    {
        var (playerNamesFilter, playerParams) = BuildPlayerNamesFilter(batchPlayers);
        var (playerNamesFilterNoAlias, _) = BuildPlayerNamesFilterNoAlias(batchPlayers);

        // Delete existing rows for these players so that periods with no remaining (non-deleted)
        // sessions are removed rather than left as stale data (e.g. after round delete).
        await dbContext.Database.ExecuteSqlRawAsync(
            $"DELETE FROM PlayerStatsMonthly WHERE {playerNamesFilterNoAlias}",
            playerParams,
            ct);

        // Use INSERT OR REPLACE to upsert aggregated stats per month
        // Filter directly on PlayerName for index utilization (parameterized)
        var sql = $@"
            INSERT OR REPLACE INTO PlayerStatsMonthly (
                PlayerName, Year, Month, TotalRounds, TotalKills, TotalDeaths, TotalScore,
                TotalPlayTimeMinutes, AvgScorePerRound, KdRatio, KillRate,
                FirstRoundTime, LastRoundTime, UpdatedAt
            )
            SELECT
                ps.PlayerName,
                CAST(strftime('%Y', ps.StartTime) AS INTEGER) as Year,
                CAST(strftime('%m', ps.StartTime) AS INTEGER) as Month,
                COUNT(*) as TotalRounds,
                SUM(ps.TotalKills) as TotalKills,
                SUM(ps.TotalDeaths) as TotalDeaths,
                SUM(ps.TotalScore) as TotalScore,
                SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes,
                CAST(SUM(ps.TotalScore) AS REAL) / COUNT(*) as AvgScorePerRound,
                CASE
                    WHEN SUM(ps.TotalDeaths) > 0 THEN ROUND(CAST(SUM(ps.TotalKills) AS REAL) / SUM(ps.TotalDeaths), 3)
                    ELSE SUM(ps.TotalKills)
                END as KdRatio,
                CASE
                    WHEN SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) > 0
                    THEN ROUND(SUM(ps.TotalKills) / SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60), 3)
                    ELSE 0
                END as KillRate,
                strftime('%Y-%m-%dT%H:%M:%fZ', MIN(ps.StartTime)) as FirstRoundTime,
                strftime('%Y-%m-%dT%H:%M:%fZ', MAX(ps.LastSeenTime)) as LastRoundTime,
                @pNowIso as UpdatedAt
            FROM PlayerSessions ps
            WHERE {playerNamesFilter}
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            GROUP BY ps.PlayerName, strftime('%Y', ps.StartTime), strftime('%m', ps.StartTime)";

        var allParams = playerParams.Append(nowIso).ToArray();
        // Replace @pNowIso with the correct parameter index
        sql = sql.Replace("@pNowIso", $"@p{batchPlayers.Count}");

        return await dbContext.Database.ExecuteSqlRawAsync(sql, allParams, ct);
    }

    private async Task<int> BackfillServerStatsAsync(
        PlayerTrackerDbContext dbContext,
        List<string> batchPlayers,
        string nowIso,
        CancellationToken ct)
    {
        var (playerNamesFilter, playerParams) = BuildPlayerNamesFilter(batchPlayers);
        var (playerNamesFilterNoAlias, _) = BuildPlayerNamesFilterNoAlias(batchPlayers);
        var nowIsoParamIndex = batchPlayers.Count;

        // Delete existing rows for these players so that (server,year,week) with no remaining
        // (non-deleted) sessions are removed (e.g. after round delete).
        await dbContext.Database.ExecuteSqlRawAsync(
            $"DELETE FROM PlayerServerStats WHERE {playerNamesFilterNoAlias}",
            playerParams,
            ct);

        // Filter directly on PlayerName for index utilization (parameterized)
        // Group by Year/Week for weekly aggregation using ISO week calculation
        // ISO week: Thursday determines the week's year, week 1 contains January 4th
        var sql = $@"
            INSERT OR REPLACE INTO PlayerServerStats (
                PlayerName, ServerGuid, Year, Week, TotalRounds, TotalKills, TotalDeaths, TotalScore,
                TotalPlayTimeMinutes, UpdatedAt
            )
            SELECT
                ps.PlayerName,
                ps.ServerGuid,
                CAST(strftime('%Y', datetime(ps.StartTime, '+3 days', 'weekday 4', '-3 days')) AS INTEGER) as Year,
                CAST((strftime('%j', datetime(ps.StartTime, '+3 days', 'weekday 4', '-3 days')) + 6) / 7 AS INTEGER) as Week,
                COUNT(*) as TotalRounds,
                SUM(ps.TotalKills) as TotalKills,
                SUM(ps.TotalDeaths) as TotalDeaths,
                SUM(ps.TotalScore) as TotalScore,
                SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes,
                @p{nowIsoParamIndex} as UpdatedAt
            FROM PlayerSessions ps
            WHERE {playerNamesFilter}
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            GROUP BY ps.PlayerName, ps.ServerGuid,
                strftime('%Y', datetime(ps.StartTime, '+3 days', 'weekday 4', '-3 days')),
                (strftime('%j', datetime(ps.StartTime, '+3 days', 'weekday 4', '-3 days')) + 6) / 7";

        var allParams = playerParams.Append(nowIso).ToArray();
        return await dbContext.Database.ExecuteSqlRawAsync(sql, allParams, ct);
    }

    private async Task<int> BackfillMapStatsAsync(
        PlayerTrackerDbContext dbContext,
        List<string> batchPlayers,
        string nowIso,
        CancellationToken ct)
    {
        var (playerNamesFilter, playerParams) = BuildPlayerNamesFilter(batchPlayers);
        var (playerNamesFilterNoAlias, _) = BuildPlayerNamesFilterNoAlias(batchPlayers);
        var nowIsoParamIndex = batchPlayers.Count;
        var allParams = playerParams.Append(nowIso).ToArray();

        // Delete existing rows for these players so that (map,server,year,month) with no
        // remaining (non-deleted) sessions are removed (e.g. after round delete).
        await dbContext.Database.ExecuteSqlRawAsync(
            $"DELETE FROM PlayerMapStats WHERE {playerNamesFilterNoAlias}",
            playerParams,
            ct);

        // Server-specific map stats - filter directly on PlayerName (parameterized)
        // Group by Year/Month for monthly aggregation
        var serverMapSql = $@"
            INSERT OR REPLACE INTO PlayerMapStats (
                PlayerName, MapName, ServerGuid, Year, Month, TotalRounds, TotalKills,
                TotalDeaths, TotalScore, TotalPlayTimeMinutes, UpdatedAt
            )
            SELECT
                ps.PlayerName,
                ps.MapName,
                ps.ServerGuid,
                CAST(strftime('%Y', ps.StartTime) AS INTEGER) as Year,
                CAST(strftime('%m', ps.StartTime) AS INTEGER) as Month,
                COUNT(*) as TotalRounds,
                SUM(ps.TotalKills) as TotalKills,
                SUM(ps.TotalDeaths) as TotalDeaths,
                SUM(ps.TotalScore) as TotalScore,
                SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes,
                @p{nowIsoParamIndex} as UpdatedAt
            FROM PlayerSessions ps
            WHERE {playerNamesFilter}
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            GROUP BY ps.PlayerName, ps.MapName, ps.ServerGuid, strftime('%Y', ps.StartTime), strftime('%m', ps.StartTime)";

        var serverMapCount = await dbContext.Database.ExecuteSqlRawAsync(serverMapSql, allParams, ct);

        // Global map stats (ServerGuid = '') - parameterized
        // Group by Year/Month for monthly aggregation
        var globalMapSql = $@"
            INSERT OR REPLACE INTO PlayerMapStats (
                PlayerName, MapName, ServerGuid, Year, Month, TotalRounds, TotalKills,
                TotalDeaths, TotalScore, TotalPlayTimeMinutes, UpdatedAt
            )
            SELECT
                ps.PlayerName,
                ps.MapName,
                '' as ServerGuid,
                CAST(strftime('%Y', ps.StartTime) AS INTEGER) as Year,
                CAST(strftime('%m', ps.StartTime) AS INTEGER) as Month,
                COUNT(*) as TotalRounds,
                SUM(ps.TotalKills) as TotalKills,
                SUM(ps.TotalDeaths) as TotalDeaths,
                SUM(ps.TotalScore) as TotalScore,
                SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes,
                @p{nowIsoParamIndex} as UpdatedAt
            FROM PlayerSessions ps
            WHERE {playerNamesFilter}
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            GROUP BY ps.PlayerName, ps.MapName, strftime('%Y', ps.StartTime), strftime('%m', ps.StartTime)";

        var globalMapCount = await dbContext.Database.ExecuteSqlRawAsync(globalMapSql, allParams, ct);

        return serverMapCount + globalMapCount;
    }

    private async Task<int> BackfillBestScoresAsync(
        PlayerTrackerDbContext dbContext,
        List<string> batchPlayers,
        DateTime nowUtc,
        CancellationToken ct)
    {
        var totalCount = 0;
        var (playerNamesFilter, playerParams) = BuildPlayerNamesFilter(batchPlayers);
        var (playerNamesFilterNoAlias, _) = BuildPlayerNamesFilterNoAlias(batchPlayers);

        // Process each period
        var periods = new[]
        {
            ("all_time", (DateTime?)null),
            ("last_30_days", nowUtc.AddDays(-30)),
            ("this_week", nowUtc.AddDays(-7))
        };

        foreach (var (period, cutoff) in periods)
        {
            // Build parameters for this period
            // For DELETE: @p0..@pN-1 = player names, @pN = period
            var periodParamIndex = batchPlayers.Count;
            var deleteParams = playerParams.Append(period).ToArray();

            // Delete existing records for this period and batch's players (parameterized)
            var deleteSql = $@"
                DELETE FROM PlayerBestScores
                WHERE Period = @p{periodParamIndex}
                  AND {playerNamesFilterNoAlias}";

            await dbContext.Database.ExecuteSqlRawAsync(deleteSql, deleteParams, ct);

            // Build INSERT query with parameterized period and optional cutoff
            var insertParams = new List<object>(playerParams) { period };
            var cutoffFilter = "";

            if (cutoff.HasValue)
            {
                var cutoffParamIndex = batchPlayers.Count + 1;
                insertParams.Add(cutoff.Value.ToString("yyyy-MM-dd HH:mm:ss"));
                cutoffFilter = $"AND ps.LastSeenTime >= @p{cutoffParamIndex}";
            }

            // Insert top 3 scores per player for this period (parameterized)
            var insertSql = $@"
                INSERT INTO PlayerBestScores (
                    PlayerName, Period, Rank, FinalScore, FinalKills, FinalDeaths,
                    MapName, ServerGuid, RoundEndTime, RoundId
                )
                SELECT
                    PlayerName, Period, Rank, FinalScore, FinalKills, FinalDeaths,
                    MapName, ServerGuid, RoundEndTime, RoundId
                FROM (
                    SELECT
                        ps.PlayerName,
                        @p{periodParamIndex} as Period,
                        ROW_NUMBER() OVER (PARTITION BY ps.PlayerName ORDER BY ps.TotalScore DESC) as Rank,
                        ps.TotalScore as FinalScore,
                        ps.TotalKills as FinalKills,
                        ps.TotalDeaths as FinalDeaths,
                        ps.MapName,
                        ps.ServerGuid,
                        strftime('%Y-%m-%dT%H:%M:%fZ', ps.LastSeenTime) as RoundEndTime,
                        COALESCE(ps.RoundId, '') as RoundId
                    FROM PlayerSessions ps
                    WHERE {playerNamesFilter}
                      AND ps.TotalScore > 0
                      AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                      {cutoffFilter}
                ) ranked
                WHERE Rank <= 3";

            totalCount += await dbContext.Database.ExecuteSqlRawAsync(insertSql, insertParams.ToArray(), ct);
        }

        return totalCount;
    }
}
