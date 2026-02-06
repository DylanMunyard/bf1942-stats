using System.Diagnostics;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using NodaTime;
using NodaTime.Text;
using Serilog.Context;

namespace api.Services.BackgroundJobs;

/// <summary>
/// Executes the daily aggregate refresh logic. Can be triggered on-demand or by the scheduled job.
/// </summary>
public class DailyAggregateRefreshJob(
    IServiceScopeFactory scopeFactory,
    api.Services.IAggregateConcurrencyService concurrency,
    ILogger<DailyAggregateRefreshJob> logger,
    IClock clock
) : IDailyAggregateRefreshBackgroundService
{
    public async Task RunAsync(CancellationToken ct = default)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("DailyAggregateRefresh");
        activity?.SetTag("bulk_operation", "true");
        var stopwatch = Stopwatch.StartNew();

        logger.LogInformation("Starting daily aggregate refresh");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        // Suppress EF SQL logging during bulk operations
        using (LogContext.PushProperty("bulk_operation", true))
        {
            try
            {
                var results = new Dictionary<string, int>();

                results["serverHourlyPatterns"] = await RefreshServerHourlyPatternsAsync(dbContext, ct);
                results["hourlyPlayerPredictions"] = await RefreshHourlyPlayerPredictionsAsync(dbContext, ct);
                results["hourlyActivityPatterns"] = await RefreshHourlyActivityPatternsAsync(dbContext, ct);
                results["mapGlobalAverages"] = await RefreshMapGlobalAveragesAsync(dbContext, ct);
                results["serverMapStats"] = await concurrency.ExecuteWithServerMapStatsLockAsync(async (c) => await RefreshServerMapStatsAsync(dbContext, c), ct);
                results["mapServerHourlyPatterns"] = await RefreshMapServerHourlyPatternsAsync(dbContext, ct);
                results["bestScores"] = await concurrency.ExecuteWithPlayerAggregatesLockAsync(
                    async (c) => await RefreshBestScoresAsync(dbContext, c), ct);

                stopwatch.Stop();

                var totalRecords = results.Values.Sum();
                activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
                activity?.SetTag("result.total_records", totalRecords);

                logger.LogInformation(
                    "Daily aggregate refresh completed: {TotalRecords} records in {Duration}ms (patterns={Patterns}, predictions={Predictions}, activity={Activity}, mapAvg={MapAvg}, serverMap={ServerMap}, mapHourly={MapHourly}, bestScores={BestScores})",
                    totalRecords, stopwatch.ElapsedMilliseconds,
                    results["serverHourlyPatterns"], results["hourlyPlayerPredictions"], results["hourlyActivityPatterns"],
                    results["mapGlobalAverages"], results["serverMapStats"], results["mapServerHourlyPatterns"],
                    results["bestScores"]);
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                logger.LogError(ex, "Failed to complete daily aggregate refresh");
                throw;
            }
        }
    }

    /// <summary>
    /// One-time full backfill of ServerMapStats from all historical Rounds data.
    /// Use for initial population - daily refresh only updates last 2 months.
    /// </summary>
    public async Task BackfillServerMapStatsAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Starting full ServerMapStats backfill from all Rounds data");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        await concurrency.ExecuteWithServerMapStatsLockAsync(async (c) =>
        {
            var nowIso = InstantPattern.ExtendedIso.Format(clock.GetCurrentInstant());

            var sql = @"
            INSERT OR REPLACE INTO ServerMapStats
                (ServerGuid, MapName, Year, Month, TotalRounds, TotalPlayTimeMinutes,
                 AvgConcurrentPlayers, PeakConcurrentPlayers, Team1Victories, Team2Victories,
                 Team1Label, Team2Label, UpdatedAt)
            SELECT
                ServerGuid,
                MapName,
                CAST(strftime('%Y', StartTime) AS INTEGER) as Year,
                CAST(strftime('%m', StartTime) AS INTEGER) as Month,
                COUNT(*) as TotalRounds,
                COALESCE(SUM(DurationMinutes), 0) as TotalPlayTimeMinutes,
                ROUND(AVG(COALESCE(ParticipantCount, 0)), 2) as AvgConcurrentPlayers,
                COALESCE(MAX(ParticipantCount), 0) as PeakConcurrentPlayers,
                SUM(CASE
                    WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                         AND Tickets1 > Tickets2 THEN 1
                    ELSE 0
                END) as Team1Victories,
                SUM(CASE
                    WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                         AND Tickets2 > Tickets1 THEN 1
                    ELSE 0
                END) as Team2Victories,
                MAX(Team1Label) as Team1Label,
                MAX(Team2Label) as Team2Label,
                @p0 as UpdatedAt
            FROM Rounds
            WHERE IsActive = 0
              AND (IsDeleted = 0 OR IsDeleted IS NULL)
              AND MapName IS NOT NULL
              AND MapName != ''
            GROUP BY ServerGuid, MapName, CAST(strftime('%Y', StartTime) AS INTEGER), CAST(strftime('%m', StartTime) AS INTEGER)";

            var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, [nowIso], c);

            stopwatch.Stop();
            logger.LogInformation("Backfilled {Count} server map stats from all historical data in {Duration}ms",
                rowsAffected, stopwatch.ElapsedMilliseconds);
        }, ct);
    }

    /// <summary>
    /// Refreshes ServerMapStats for a single (ServerGuid, MapName, Year, Month) from Rounds,
    /// excluding soft-deleted rounds. Used after round delete/undelete.
    /// </summary>
    public async Task RefreshServerMapStatsForServerMapPeriodAsync(string serverGuid, string mapName, int year, int month, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        await concurrency.ExecuteWithServerMapStatsLockAsync(async (c) =>
        {
            var nowIso = InstantPattern.ExtendedIso.Format(clock.GetCurrentInstant());
            var yearString = year.ToString();
            var monthString = month.ToString("00");

            // Remove existing row so we can replace with recomputed value (or leave deleted if no non-deleted rounds)
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ServerMapStats
                WHERE ServerGuid = {0} AND MapName = {1} AND Year = {2} AND Month = {3}",
                [serverGuid, mapName, year, month], c);

            // Re-insert from Rounds for this server+map+period, excluding deleted rounds
            var insertSql = @"
                INSERT INTO ServerMapStats
                    (ServerGuid, MapName, Year, Month, TotalRounds, TotalPlayTimeMinutes,
                     AvgConcurrentPlayers, PeakConcurrentPlayers, Team1Victories, Team2Victories,
                     Team1Label, Team2Label, UpdatedAt)
                SELECT
                    ServerGuid,
                    MapName,
                    CAST(strftime('%Y', StartTime) AS INTEGER) as Year,
                    CAST(strftime('%m', StartTime) AS INTEGER) as Month,
                    COUNT(*) as TotalRounds,
                    COALESCE(SUM(DurationMinutes), 0) as TotalPlayTimeMinutes,
                    ROUND(AVG(COALESCE(ParticipantCount, 0)), 2) as AvgConcurrentPlayers,
                    COALESCE(MAX(ParticipantCount), 0) as PeakConcurrentPlayers,
                    SUM(CASE
                        WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                             AND Tickets1 > Tickets2 THEN 1
                        ELSE 0
                    END) as Team1Victories,
                    SUM(CASE
                        WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                             AND Tickets2 > Tickets1 THEN 1
                        ELSE 0
                    END) as Team2Victories,
                    MAX(Team1Label) as Team1Label,
                    MAX(Team2Label) as Team2Label,
                    {4} as UpdatedAt
                FROM Rounds
                WHERE IsActive = 0
                  AND (IsDeleted = 0 OR IsDeleted IS NULL)
                  AND MapName IS NOT NULL
                  AND MapName != ''
                  AND ServerGuid = {0}
                  AND MapName = {1}
                  AND strftime('%Y', StartTime) = {2}
                  AND strftime('%m', StartTime) = {3}
                GROUP BY ServerGuid, MapName, CAST(strftime('%Y', StartTime) AS INTEGER), CAST(strftime('%m', StartTime) AS INTEGER)";

            var rowsInserted = await dbContext.Database.ExecuteSqlRawAsync(insertSql, [serverGuid, mapName, yearString, monthString, nowIso], c);

            logger.LogDebug("Refreshed ServerMapStats for {ServerGuid} / {MapName} {Year}-{Month}: {Rows} rows",
                serverGuid, mapName, year, monthString, rowsInserted);
        }, ct);
    }

    /// <summary>
    /// Refreshes server hourly patterns using SQL grouping with GROUP_CONCAT for percentile calculation.
    /// Much more efficient than loading all raw data - we load only pre-grouped results.
    /// </summary>
    private async Task<int> RefreshServerHourlyPatternsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var lookbackDays = 60;
        var cutoffTimeIso = InstantPattern.ExtendedIso.Format(now.Minus(Duration.FromDays(lookbackDays)));
        var nowIso = InstantPattern.ExtendedIso.Format(now);

        // Use SQL to group and aggregate, collecting all values via GROUP_CONCAT for percentile calculation
        // This reduces data transfer from ~1.4M rows to ~N_servers × 168 rows
        var sql = $@"
            SELECT
                ServerGuid,
                CAST(strftime('%w', HourTimestamp) AS INTEGER) as DayOfWeek,
                CAST(strftime('%H', HourTimestamp) AS INTEGER) as HourOfDay,
                AVG(AvgPlayers) as AvgPlayers,
                MIN(AvgPlayers) as MinPlayers,
                MAX(AvgPlayers) as MaxPlayers,
                COUNT(*) as DataPoints,
                GROUP_CONCAT(AvgPlayers, ',') as AllValues
            FROM ServerOnlineCounts
            WHERE HourTimestamp >= @p0
            GROUP BY ServerGuid, CAST(strftime('%w', HourTimestamp) AS INTEGER), CAST(strftime('%H', HourTimestamp) AS INTEGER)";

        var groupedData = await dbContext.Database
            .SqlQueryRaw<ServerHourlyGroupedData>(sql, cutoffTimeIso)
            .ToListAsync(ct);

        if (groupedData.Count == 0)
        {
            return 0;
        }

        // Calculate percentiles from the grouped values and build INSERT statement
        var insertSql = @"INSERT OR REPLACE INTO ServerHourlyPatterns
            (ServerGuid, DayOfWeek, HourOfDay, AvgPlayers, MinPlayers, Q25Players, MedianPlayers, Q75Players, Q90Players, MaxPlayers, DataPoints, UpdatedAt)
            VALUES ";

        var allParameters = new List<object>();
        var valueClauses = new List<string>();

        for (var i = 0; i < groupedData.Count; i++)
        {
            var group = groupedData[i];
            var sortedValues = ParseAndSortValues(group.AllValues);

            var baseParamIndex = i * 12;
            valueClauses.Add($"(@p{baseParamIndex}, @p{baseParamIndex + 1}, @p{baseParamIndex + 2}, @p{baseParamIndex + 3}, @p{baseParamIndex + 4}, @p{baseParamIndex + 5}, @p{baseParamIndex + 6}, @p{baseParamIndex + 7}, @p{baseParamIndex + 8}, @p{baseParamIndex + 9}, @p{baseParamIndex + 10}, @p{baseParamIndex + 11})");

            allParameters.AddRange([
                group.ServerGuid,
                group.DayOfWeek,
                group.HourOfDay,
                group.AvgPlayers,
                group.MinPlayers,
                GetPercentile(sortedValues, 0.25),
                GetPercentile(sortedValues, 0.5),
                GetPercentile(sortedValues, 0.75),
                GetPercentile(sortedValues, 0.9),
                group.MaxPlayers,
                group.DataPoints,
                nowIso
            ]);
        }

        // Execute in batches to avoid SQLite parameter limits (max ~999 parameters)
        const int batchSize = 80; // 80 × 12 = 960 parameters per batch
        var totalInserted = 0;

        for (var batchStart = 0; batchStart < valueClauses.Count; batchStart += batchSize)
        {
            var batchClauses = valueClauses.Skip(batchStart).Take(batchSize).ToList();
            var batchParams = allParameters.Skip(batchStart * 12).Take(batchClauses.Count * 12).ToList();

            // Renumber parameters for this batch
            var batchSql = insertSql + string.Join(", ", batchClauses.Select((_, i) =>
            {
                var baseIdx = i * 12;
                return $"(@p{baseIdx}, @p{baseIdx + 1}, @p{baseIdx + 2}, @p{baseIdx + 3}, @p{baseIdx + 4}, @p{baseIdx + 5}, @p{baseIdx + 6}, @p{baseIdx + 7}, @p{baseIdx + 8}, @p{baseIdx + 9}, @p{baseIdx + 10}, @p{baseIdx + 11})";
            }));

            totalInserted += await dbContext.Database.ExecuteSqlRawAsync(batchSql, batchParams, ct);
        }

        return totalInserted;
    }

    /// <summary>
    /// Refreshes hourly player predictions using a single SQL INSERT OR REPLACE with aggregation.
    /// </summary>
    private async Task<int> RefreshHourlyPlayerPredictionsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var lookbackDays = 60;
        var cutoffTimeIso = InstantPattern.ExtendedIso.Format(now.Minus(Duration.FromDays(lookbackDays)));
        var nowIso = InstantPattern.ExtendedIso.Format(now);

        // Pure SQL approach: aggregate in a single query
        // Step 1: Sum players across all servers for each (game, date, day_of_week, hour)
        // Step 2: Average those sums across dates to get prediction
        var sql = @"
            INSERT OR REPLACE INTO HourlyPlayerPredictions (Game, DayOfWeek, HourOfDay, PredictedPlayers, DataPoints, UpdatedAt)
            SELECT
                Game,
                DayOfWeek,
                HourOfDay,
                AVG(HourlyTotal) as PredictedPlayers,
                COUNT(*) as DataPoints,
                @p1 as UpdatedAt
            FROM (
                SELECT
                    Game,
                    CAST(strftime('%w', HourTimestamp) AS INTEGER) as DayOfWeek,
                    CAST(strftime('%H', HourTimestamp) AS INTEGER) as HourOfDay,
                    date(HourTimestamp) as DateKey,
                    SUM(AvgPlayers) as HourlyTotal
                FROM ServerOnlineCounts
                WHERE HourTimestamp >= @p0
                  AND Game IN ('bf1942', 'fh2', 'bfvietnam')
                GROUP BY Game, date(HourTimestamp), CAST(strftime('%w', HourTimestamp) AS INTEGER), CAST(strftime('%H', HourTimestamp) AS INTEGER)
            ) hourly_totals
            GROUP BY Game, DayOfWeek, HourOfDay";

        return await dbContext.Database.ExecuteSqlRawAsync(sql, [cutoffTimeIso, nowIso], ct);
    }

    /// <summary>
    /// Refreshes hourly activity patterns from PlayerSessions.
    /// Calculates unique players, total rounds, and average round duration per game/day/hour.
    /// Used by GetWeeklyActivityPatternsAsync in SqliteGameTrendsService.
    /// </summary>
    private async Task<int> RefreshHourlyActivityPatternsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var lookbackDays = 30;
        var cutoffTime = now.Minus(Duration.FromDays(lookbackDays)).ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm:ss");
        var nowIso = InstantPattern.ExtendedIso.Format(now);

        // Aggregate from PlayerSessions - compute daily stats first, then average across days
        // This gives us typical activity for each hour slot per game
        var sql = @"
            INSERT OR REPLACE INTO HourlyActivityPatterns
                (Game, DayOfWeek, HourOfDay, UniquePlayersAvg, TotalRoundsAvg, AvgRoundDuration, PeriodType, UpdatedAt)
            SELECT
                Game,
                DayOfWeek,
                HourOfDay,
                AVG(UniquePlayersDaily) as UniquePlayersAvg,
                AVG(TotalRoundsDaily) as TotalRoundsAvg,
                AVG(AvgRoundDurationDaily) as AvgRoundDuration,
                CASE WHEN DayOfWeek IN (0, 6) THEN 'Weekend' ELSE 'Weekday' END as PeriodType,
                @p1 as UpdatedAt
            FROM (
                SELECT
                    s.Game,
                    CAST(strftime('%w', ps.StartTime) AS INTEGER) as DayOfWeek,
                    CAST(strftime('%H', ps.StartTime) AS INTEGER) as HourOfDay,
                    date(ps.StartTime) as DateKey,
                    COUNT(DISTINCT ps.PlayerName) as UniquePlayersDaily,
                    COUNT(DISTINCT ps.RoundId) as TotalRoundsDaily,
                    AVG((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) as AvgRoundDurationDaily
                FROM PlayerSessions ps
                INNER JOIN Servers s ON ps.ServerGuid = s.Guid
                WHERE ps.StartTime >= @p0
                  AND s.Game IN ('bf1942', 'fh2', 'bfvietnam')
                  AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                GROUP BY s.Game, date(ps.StartTime), CAST(strftime('%w', ps.StartTime) AS INTEGER), CAST(strftime('%H', ps.StartTime) AS INTEGER)
            ) daily_stats
            GROUP BY Game, DayOfWeek, HourOfDay";

        return await dbContext.Database.ExecuteSqlRawAsync(sql, [cutoffTime, nowIso], ct);
    }

    /// <summary>
    /// Refreshes map global averages using a single SQL INSERT OR REPLACE.
    /// </summary>
    private async Task<int> RefreshMapGlobalAveragesAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var nowIso = InstantPattern.ExtendedIso.Format(clock.GetCurrentInstant());

        // Single SQL statement to aggregate and upsert all map averages
        var sql = @"
            INSERT OR REPLACE INTO MapGlobalAverages (MapName, ServerGuid, AvgKillRate, AvgScoreRate, SampleCount, UpdatedAt)
            SELECT
                MapName,
                '' as ServerGuid,
                ROUND(CAST(SUM(TotalKills) AS REAL) / SUM(TotalPlayTimeMinutes), 3) as AvgKillRate,
                ROUND(CAST(SUM(TotalScore) AS REAL) / SUM(TotalPlayTimeMinutes), 3) as AvgScoreRate,
                COUNT(*) as SampleCount,
                @p0 as UpdatedAt
            FROM PlayerMapStats
            WHERE ServerGuid = '' AND TotalPlayTimeMinutes > 0
            GROUP BY MapName";

        return await dbContext.Database.ExecuteSqlRawAsync(sql, [nowIso], ct);
    }

    /// <summary>
    /// Refreshes server map statistics aggregated from the Rounds table.
    /// Monthly aggregation provides map insights (play time, player counts, team victories).
    /// Only refreshes current and previous month (recent data that may have changed).
    /// Used by /stats/servers/{name}/insights/maps endpoint.
    /// </summary>
    private async Task<int> RefreshServerMapStatsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var nowIso = InstantPattern.ExtendedIso.Format(now);

        // Only refresh last 2 months - historical data doesn't change
        var cutoffTime = now.Minus(Duration.FromDays(62)).ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm:ss");

        // Aggregate from Rounds table - group by server/map/year/month
        // Team victory detection: lower ticket count means that team lost
        // Labels: pick the most recent non-null label (MAX works since we often have consistent labels)
        var sql = @"
            INSERT OR REPLACE INTO ServerMapStats
                (ServerGuid, MapName, Year, Month, TotalRounds, TotalPlayTimeMinutes,
                 AvgConcurrentPlayers, PeakConcurrentPlayers, Team1Victories, Team2Victories,
                 Team1Label, Team2Label, UpdatedAt)
            SELECT
                ServerGuid,
                MapName,
                CAST(strftime('%Y', StartTime) AS INTEGER) as Year,
                CAST(strftime('%m', StartTime) AS INTEGER) as Month,
                COUNT(*) as TotalRounds,
                COALESCE(SUM(DurationMinutes), 0) as TotalPlayTimeMinutes,
                ROUND(AVG(COALESCE(ParticipantCount, 0)), 2) as AvgConcurrentPlayers,
                COALESCE(MAX(ParticipantCount), 0) as PeakConcurrentPlayers,
                SUM(CASE
                    WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                         AND Tickets1 > Tickets2 THEN 1
                    ELSE 0
                END) as Team1Victories,
                SUM(CASE
                    WHEN Tickets1 IS NOT NULL AND Tickets2 IS NOT NULL
                         AND Tickets2 > Tickets1 THEN 1
                    ELSE 0
                END) as Team2Victories,
                MAX(Team1Label) as Team1Label,
                MAX(Team2Label) as Team2Label,
                @p0 as UpdatedAt
            FROM Rounds
            WHERE IsActive = 0
              AND (IsDeleted = 0 OR IsDeleted IS NULL)
              AND MapName IS NOT NULL
              AND MapName != ''
              AND StartTime >= @p1
            GROUP BY ServerGuid, MapName, CAST(strftime('%Y', StartTime) AS INTEGER), CAST(strftime('%m', StartTime) AS INTEGER)";

        return await dbContext.Database.ExecuteSqlRawAsync(sql, [nowIso, cutoffTime], ct);
    }

    /// <summary>
    /// Refreshes map-server hourly patterns aggregated from Rounds table.
    /// Used for "When is this map played?" heatmaps in Data Explorer.
    /// Groups by server guid, map name, game, day of week, and hour to show server-specific activity patterns.
    /// </summary>
    private async Task<int> RefreshMapServerHourlyPatternsAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        var now = clock.GetCurrentInstant();
        var lookbackDays = 60;
        var cutoffTime = now.Minus(Duration.FromDays(lookbackDays)).ToDateTimeUtc().ToString("yyyy-MM-dd HH:mm:ss");
        var nowIso = InstantPattern.ExtendedIso.Format(now);

        // Aggregate from Rounds table - group by server, map, game, day of week, hour
        // AvgPlayers = average participant count when this map was played at this time
        // TimesPlayed = total number of rounds at this time slot
        // DataPoints = number of distinct days with data (for statistical validity)
        var sql = @"
            INSERT OR REPLACE INTO MapServerHourlyPatterns
                (ServerGuid, MapName, Game, DayOfWeek, HourOfDay, AvgPlayers, TimesPlayed, DataPoints, UpdatedAt)
            SELECT
                r.ServerGuid,
                r.MapName,
                s.Game,
                CAST(strftime('%w', r.StartTime) AS INTEGER) as DayOfWeek,
                CAST(strftime('%H', r.StartTime) AS INTEGER) as HourOfDay,
                ROUND(AVG(COALESCE(r.ParticipantCount, 0)), 2) as AvgPlayers,
                COUNT(*) as TimesPlayed,
                COUNT(DISTINCT date(r.StartTime)) as DataPoints,
                @p0 as UpdatedAt
            FROM Rounds r
            INNER JOIN Servers s ON r.ServerGuid = s.Guid
            WHERE r.StartTime >= @p1
              AND r.IsActive = 0
              AND (r.IsDeleted = 0 OR r.IsDeleted IS NULL)
              AND r.MapName IS NOT NULL
              AND r.MapName != ''
              AND s.Game IN ('bf1942', 'fh2', 'bfvietnam')
            GROUP BY r.ServerGuid, r.MapName, s.Game,
                     CAST(strftime('%w', r.StartTime) AS INTEGER),
                     CAST(strftime('%H', r.StartTime) AS INTEGER)";

        return await dbContext.Database.ExecuteSqlRawAsync(sql, [nowIso, cutoffTime], ct);
    }

    /// <summary>
    /// Refreshes PlayerBestScores (top 3 scores per player per period) for recently active players.
    /// Targets players active in the last 7 days. Uses the same SQL pattern as the backfill service.
    /// </summary>
    private async Task<int> RefreshBestScoresAsync(PlayerTrackerDbContext dbContext, CancellationToken ct)
    {
        using var activity = ActivitySources.SqliteAnalytics.StartActivity("DailyAggregateRefresh.BestScores");
        activity?.SetTag("bulk_operation", "true");
        var stopwatch = Stopwatch.StartNew();

        var now = clock.GetCurrentInstant();
        var nowUtc = now.ToDateTimeUtc();
        var cutoffTime = nowUtc.AddDays(-7).ToString("yyyy-MM-dd HH:mm:ss");

        // Get distinct player names active in the last 7 days (non-bot, non-deleted sessions)
        var playerNamesSql = $@"
            SELECT DISTINCT ps.PlayerName
            FROM PlayerSessions ps
            INNER JOIN Players p ON ps.PlayerName = p.Name
            WHERE ps.LastSeenTime >= @p0
              AND p.AiBot = 0
              AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
            ORDER BY ps.PlayerName";

        var playerNames = await dbContext.Database
            .SqlQueryRaw<string>(playerNamesSql, cutoffTime)
            .ToListAsync(ct);

        logger.LogInformation("Best scores refresh: found {PlayerCount} recently active players", playerNames.Count);
        activity?.SetTag("result.player_count", playerNames.Count);

        if (playerNames.Count == 0)
            return 0;

        var totalCount = 0;
        const int batchSize = 100;
        var totalBatches = (playerNames.Count + batchSize - 1) / batchSize;

        var periods = new[]
        {
            ("all_time", (DateTime?)null),
            ("last_30_days", nowUtc.AddDays(-30)),
            ("this_week", nowUtc.AddDays(-7))
        };

        for (var batchStart = 0; batchStart < playerNames.Count; batchStart += batchSize)
        {
            if (ct.IsCancellationRequested) break;

            var batchPlayers = playerNames.Skip(batchStart).Take(batchSize).ToList();
            var batchIndex = batchStart / batchSize + 1;

            // Build parameterized IN clause for player names
            var placeholders = string.Join(", ", batchPlayers.Select((_, i) => $"@p{i}"));
            var playerNamesFilter = $"ps.PlayerName IN ({placeholders})";
            var playerNamesFilterNoAlias = $"PlayerName IN ({placeholders})";
            var playerParams = batchPlayers.Cast<object>().ToArray();

            foreach (var (period, cutoff) in periods)
            {
                var periodParamIndex = batchPlayers.Count;
                var deleteParams = playerParams.Append(period).ToArray();

                // Delete existing records for this period and batch's players
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

                // Insert top 3 scores per player for this period
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

            logger.LogDebug("Best scores refresh: batch {Batch}/{TotalBatches} ({BatchSize} players)",
                batchIndex, totalBatches, batchPlayers.Count);
        }

        stopwatch.Stop();
        activity?.SetTag("result.record_count", totalCount);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);

        logger.LogInformation("Best scores refresh completed: {RecordCount} records for {PlayerCount} players in {Duration}ms",
            totalCount, playerNames.Count, stopwatch.ElapsedMilliseconds);

        return totalCount;
    }

    /// <summary>
    /// One-time full backfill of MapHourlyPatterns from all historical Rounds data.
    /// Use for initial population - daily refresh only updates last 60 days.
    /// </summary>
    public async Task BackfillMapHourlyPatternsAsync(CancellationToken ct = default)
    {
        var stopwatch = Stopwatch.StartNew();
        logger.LogInformation("Starting full MapServerHourlyPatterns backfill from all Rounds data");

        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        var nowIso = InstantPattern.ExtendedIso.Format(clock.GetCurrentInstant());

        var sql = @"
            INSERT OR REPLACE INTO MapServerHourlyPatterns
                (ServerGuid, MapName, Game, DayOfWeek, HourOfDay, AvgPlayers, TimesPlayed, DataPoints, UpdatedAt)
            SELECT
                r.ServerGuid,
                r.MapName,
                s.Game,
                CAST(strftime('%w', r.StartTime) AS INTEGER) as DayOfWeek,
                CAST(strftime('%H', r.StartTime) AS INTEGER) as HourOfDay,
                ROUND(AVG(COALESCE(r.ParticipantCount, 0)), 2) as AvgPlayers,
                COUNT(*) as TimesPlayed,
                COUNT(DISTINCT date(r.StartTime)) as DataPoints,
                @p0 as UpdatedAt
            FROM Rounds r
            INNER JOIN Servers s ON r.ServerGuid = s.Guid
            WHERE r.IsActive = 0
              AND (r.IsDeleted = 0 OR r.IsDeleted IS NULL)
              AND r.MapName IS NOT NULL
              AND r.MapName != ''
              AND s.Game IN ('bf1942', 'fh2', 'bfvietnam')
            GROUP BY r.ServerGuid, r.MapName, s.Game,
                     CAST(strftime('%w', r.StartTime) AS INTEGER),
                     CAST(strftime('%H', r.StartTime) AS INTEGER)";

        var rowsAffected = await dbContext.Database.ExecuteSqlRawAsync(sql, [nowIso], ct);

        stopwatch.Stop();
        logger.LogInformation("Backfilled {Count} map-server hourly patterns from all historical data in {Duration}ms",
            rowsAffected, stopwatch.ElapsedMilliseconds);
    }

    private static List<double> ParseAndSortValues(string? commaSeparatedValues)
    {
        if (string.IsNullOrEmpty(commaSeparatedValues))
            return [];

        var values = commaSeparatedValues
            .Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(s => double.TryParse(s, out var v) ? v : 0.0)
            .OrderBy(v => v)
            .ToList();

        return values;
    }

    private static double GetPercentile(List<double> sortedValues, double percentile)
    {
        if (sortedValues.Count == 0) return 0;
        if (sortedValues.Count == 1) return sortedValues[0];

        var index = percentile * (sortedValues.Count - 1);
        var lower = (int)Math.Floor(index);
        var upper = (int)Math.Ceiling(index);
        var weight = index - lower;

        if (upper >= sortedValues.Count)
            return sortedValues[^1];

        return sortedValues[lower] * (1 - weight) + sortedValues[upper] * weight;
    }

    /// <summary>
    /// DTO for receiving grouped server hourly data from SQL query.
    /// </summary>
    private class ServerHourlyGroupedData
    {
        public string ServerGuid { get; set; } = "";
        public int DayOfWeek { get; set; }
        public int HourOfDay { get; set; }
        public double AvgPlayers { get; set; }
        public double MinPlayers { get; set; }
        public double MaxPlayers { get; set; }
        public int DataPoints { get; set; }
        public string? AllValues { get; set; }
    }
}
