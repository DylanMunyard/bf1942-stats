using api.Data.Entities;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NodaTime;
using Serilog.Context;
using System.Diagnostics;
using System.Text;

namespace api.StatsCollectors;

/// <summary>
/// Background service that periodically recalculates monthly aggregate statistics.
/// Uses idempotent delete + rebuild pattern per month to ensure data consistency.
/// </summary>
public class AggregateCalculationService(
    IServiceProvider services,
    api.Services.IAggregateConcurrencyService concurrency,
    ILogger<AggregateCalculationService> logger,
    IClock clock) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("AggregateCalculationService started, waiting {Delay} before first run", StartupDelay);

        // Delay startup to avoid blocking Kestrel initialization
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = ActivitySources.AggregateCalculation.StartActivity("AggregateCalculation.Cycle");
            activity?.SetTag("bulk_operation", "true");

            var cycleStopwatch = Stopwatch.StartNew();
            try
            {
                using (LogContext.PushProperty("bulk_operation", true))
                using (var scope = services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

                    var now = clock.GetCurrentInstant().ToDateTimeUtc();
                    var currentYear = now.Year;
                    var currentMonth = now.Month;
                    var currentWeek = System.Globalization.ISOWeek.GetWeekOfYear(now);
                    var isoYear = System.Globalization.ISOWeek.GetYear(now);

                    var results = new Dictionary<string, int>();

                    await concurrency.ExecuteWithPlayerAggregatesLockAsync(async (_) =>
                    {
                        results["monthly"] = await CalculatePlayerStatsMonthly(dbContext, currentYear, currentMonth);
                        results["server"] = await CalculatePlayerServerStats(dbContext, isoYear, currentWeek);
                        results["map"] = await CalculatePlayerMapStats(dbContext, currentYear, currentMonth);
                    }, stoppingToken);

                    cycleStopwatch.Stop();
                    activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                    activity?.SetTag("monthly_records", results.GetValueOrDefault("monthly"));
                    activity?.SetTag("server_records", results.GetValueOrDefault("server"));
                    activity?.SetTag("map_records", results.GetValueOrDefault("map"));

                    var totalRecords = results.Values.Sum();
                    logger.LogInformation(
                        "Aggregate calculation: {TotalRecords} records (monthly={Monthly}, server={Server}, map={Map}) for {Year}-{Month:00} in {Duration}ms",
                        totalRecords, results.GetValueOrDefault("monthly"), results.GetValueOrDefault("server"),
                        results.GetValueOrDefault("map"), currentYear, currentMonth, cycleStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                cycleStopwatch.Stop();
                activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                activity?.SetTag("error", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, $"Aggregate calculation failed: {ex.Message}");
                logger.LogError(ex, "Error during aggregate calculation");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    /// <summary>
    /// Calculate PlayerStatsMonthly for all players active in the current month.
    /// </summary>
    private async Task<int> CalculatePlayerStatsMonthly(PlayerTrackerDbContext dbContext, int year, int month)
    {
        using var activity = ActivitySources.AggregateCalculation.StartActivity("AggregateCalculation.PlayerStatsMonthly");
        activity?.SetTag("year", year);
        activity?.SetTag("month", month);

        var yearString = year.ToString();
        var monthString = month.ToString("00");

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Delete existing records for this month
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""PlayerStatsMonthly""
                WHERE ""Year"" = {0} AND ""Month"" = {1}",
                year, month);

            // Query aggregated data from PlayerSessions
            var playerData = await dbContext.Database.SqlQueryRaw<PlayerStatsAggregateData>(@"
                SELECT
                    ps.PlayerName,
                    COUNT(DISTINCT ps.RoundId) AS TotalRounds,
                    SUM(ps.TotalKills) AS TotalKills,
                    SUM(ps.TotalDeaths) AS TotalDeaths,
                    SUM(ps.TotalScore) AS TotalScore,
                    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS TotalPlayTimeMinutes,
                    MIN(ps.StartTime) AS FirstRoundTime,
                    MAX(ps.LastSeenTime) AS LastRoundTime
                FROM PlayerSessions ps
                INNER JOIN Players p ON ps.PlayerName = p.Name
                WHERE strftime('%Y', ps.StartTime) = {0}
                  AND strftime('%m', ps.StartTime) = {1}
                  AND p.AiBot = 0
                  AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                GROUP BY ps.PlayerName",
                yearString, monthString).ToListAsync();

            activity?.SetTag("player_count", playerData.Count);

            if (playerData.Count == 0)
            {
                await transaction.CommitAsync();
                return 0;
            }

            // Build and insert records
            var now = clock.GetCurrentInstant();
            var records = playerData.Select(p => new PlayerStatsMonthly
            {
                PlayerName = p.PlayerName,
                Year = year,
                Month = month,
                TotalRounds = p.TotalRounds,
                TotalKills = p.TotalKills,
                TotalDeaths = p.TotalDeaths,
                TotalScore = p.TotalScore,
                TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
                AvgScorePerRound = p.TotalRounds > 0 ? (double)p.TotalScore / p.TotalRounds : 0,
                KdRatio = p.TotalDeaths > 0 ? (double)p.TotalKills / p.TotalDeaths : p.TotalKills,
                KillRate = p.TotalPlayTimeMinutes > 0 ? p.TotalKills / p.TotalPlayTimeMinutes : 0,
                FirstRoundTime = Instant.FromDateTimeUtc(DateTime.SpecifyKind(p.FirstRoundTime, DateTimeKind.Utc)),
                LastRoundTime = Instant.FromDateTimeUtc(DateTime.SpecifyKind(p.LastRoundTime, DateTimeKind.Utc)),
                UpdatedAt = now
            }).ToList();

            await BulkInsertPlayerStatsMonthly(dbContext, records);
            activity?.SetTag("records_inserted", records.Count);

            await transaction.CommitAsync();
            return records.Count;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Error calculating PlayerStatsMonthly: {ex.Message}");
            logger.LogError(ex, "Error calculating PlayerStatsMonthly for {Year}-{Month}", year, monthString);
            throw;
        }
    }

    /// <summary>
    /// Calculate PlayerServerStats for all player-server combinations active in the current ISO week.
    /// Uses weekly buckets for finer granularity in leaderboard queries.
    /// </summary>
    private async Task<int> CalculatePlayerServerStats(PlayerTrackerDbContext dbContext, int year, int week)
    {
        using var activity = ActivitySources.AggregateCalculation.StartActivity("AggregateCalculation.PlayerServerStats");
        activity?.SetTag("year", year);
        activity?.SetTag("week", week);

        var weekString = week.ToString("00");

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Delete existing records for this week
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""PlayerServerStats""
                WHERE ""Year"" = {0} AND ""Week"" = {1}",
                year, week);

            // Query aggregated data from PlayerSessions for this ISO week
            var weekStart = System.Globalization.ISOWeek.ToDateTime(year, week, DayOfWeek.Monday);
            var weekEnd = weekStart.AddDays(7);

            var serverData = await dbContext.Database.SqlQueryRaw<PlayerServerStatsAggregateData>(@"
                SELECT
                    ps.PlayerName,
                    ps.ServerGuid,
                    COUNT(DISTINCT ps.RoundId) AS TotalRounds,
                    SUM(ps.TotalKills) AS TotalKills,
                    SUM(ps.TotalDeaths) AS TotalDeaths,
                    SUM(ps.TotalScore) AS TotalScore,
                    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS TotalPlayTimeMinutes
                FROM PlayerSessions ps
                INNER JOIN Players p ON ps.PlayerName = p.Name
                WHERE ps.StartTime >= {0}
                  AND ps.StartTime < {1}
                  AND p.AiBot = 0
                  AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                GROUP BY ps.PlayerName, ps.ServerGuid",
                weekStart.ToString("yyyy-MM-dd HH:mm:ss"),
                weekEnd.ToString("yyyy-MM-dd HH:mm:ss")).ToListAsync();

            activity?.SetTag("record_count", serverData.Count);

            if (serverData.Count == 0)
            {
                await transaction.CommitAsync();
                return 0;
            }

            var now = clock.GetCurrentInstant();
            var records = serverData.Select(p => new PlayerServerStats
            {
                PlayerName = p.PlayerName,
                ServerGuid = p.ServerGuid,
                Year = year,
                Week = week,
                TotalRounds = p.TotalRounds,
                TotalKills = p.TotalKills,
                TotalDeaths = p.TotalDeaths,
                TotalScore = p.TotalScore,
                TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
                UpdatedAt = now
            }).ToList();

            await BulkInsertPlayerServerStats(dbContext, records);
            activity?.SetTag("records_inserted", records.Count);

            await transaction.CommitAsync();
            return records.Count;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Error calculating PlayerServerStats: {ex.Message}");
            logger.LogError(ex, "Error calculating PlayerServerStats for {Year}-W{Week}", year, weekString);
            throw;
        }
    }

    /// <summary>
    /// Calculate PlayerMapStats for all player-map-server combinations active in the current month.
    /// Also calculates global (cross-server) map stats.
    /// </summary>
    private async Task<int> CalculatePlayerMapStats(PlayerTrackerDbContext dbContext, int year, int month)
    {
        using var activity = ActivitySources.AggregateCalculation.StartActivity("AggregateCalculation.PlayerMapStats");
        activity?.SetTag("year", year);
        activity?.SetTag("month", month);

        var yearString = year.ToString();
        var monthString = month.ToString("00");

        await using var transaction = await dbContext.Database.BeginTransactionAsync();
        try
        {
            // Delete existing records for this month
            await dbContext.Database.ExecuteSqlRawAsync(@"
                DELETE FROM ""PlayerMapStats""
                WHERE ""Year"" = {0} AND ""Month"" = {1}",
                year, month);

            // Query aggregated data from PlayerSessions - per server
            var mapData = await dbContext.Database.SqlQueryRaw<PlayerMapStatsAggregateData>(@"
                SELECT
                    ps.PlayerName,
                    ps.MapName,
                    ps.ServerGuid,
                    COUNT(DISTINCT ps.RoundId) AS TotalRounds,
                    SUM(ps.TotalKills) AS TotalKills,
                    SUM(ps.TotalDeaths) AS TotalDeaths,
                    SUM(ps.TotalScore) AS TotalScore,
                    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS TotalPlayTimeMinutes
                FROM PlayerSessions ps
                INNER JOIN Players p ON ps.PlayerName = p.Name
                WHERE strftime('%Y', ps.StartTime) = {0}
                  AND strftime('%m', ps.StartTime) = {1}
                  AND p.AiBot = 0
                  AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                GROUP BY ps.PlayerName, ps.MapName, ps.ServerGuid",
                yearString, monthString).ToListAsync();

            activity?.SetTag("per_server_record_count", mapData.Count);

            // Also query global (cross-server) map stats
            var globalMapData = await dbContext.Database.SqlQueryRaw<PlayerMapStatsAggregateData>(@"
                SELECT
                    ps.PlayerName,
                    ps.MapName,
                    '' AS ServerGuid,
                    COUNT(DISTINCT ps.RoundId) AS TotalRounds,
                    SUM(ps.TotalKills) AS TotalKills,
                    SUM(ps.TotalDeaths) AS TotalDeaths,
                    SUM(ps.TotalScore) AS TotalScore,
                    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS TotalPlayTimeMinutes
                FROM PlayerSessions ps
                INNER JOIN Players p ON ps.PlayerName = p.Name
                WHERE strftime('%Y', ps.StartTime) = {0}
                  AND strftime('%m', ps.StartTime) = {1}
                  AND p.AiBot = 0
                  AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                GROUP BY ps.PlayerName, ps.MapName",
                yearString, monthString).ToListAsync();

            activity?.SetTag("global_record_count", globalMapData.Count);

            var allMapData = mapData.Concat(globalMapData).ToList();

            if (allMapData.Count == 0)
            {
                await transaction.CommitAsync();
                return 0;
            }

            var now = clock.GetCurrentInstant();
            var records = allMapData.Select(p => new PlayerMapStats
            {
                PlayerName = p.PlayerName,
                MapName = p.MapName,
                ServerGuid = p.ServerGuid,
                Year = year,
                Month = month,
                TotalRounds = p.TotalRounds,
                TotalKills = p.TotalKills,
                TotalDeaths = p.TotalDeaths,
                TotalScore = p.TotalScore,
                TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
                UpdatedAt = now
            }).ToList();

            await BulkInsertPlayerMapStats(dbContext, records);
            activity?.SetTag("records_inserted", records.Count);

            await transaction.CommitAsync();
            return records.Count;
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync();
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Error calculating PlayerMapStats: {ex.Message}");
            logger.LogError(ex, "Error calculating PlayerMapStats for {Year}-{Month}", year, monthString);
            throw;
        }
    }

    private async Task BulkInsertPlayerStatsMonthly(PlayerTrackerDbContext dbContext, List<PlayerStatsMonthly> records)
    {
        if (records.Count == 0) return;

        const int batchSize = 100;
        for (int batch = 0; batch < records.Count; batch += batchSize)
        {
            var batchRecords = records.Skip(batch).Take(batchSize).ToList();
            var sql = new StringBuilder(@"
                INSERT INTO ""PlayerStatsMonthly""
                (""PlayerName"", ""Year"", ""Month"", ""TotalRounds"", ""TotalKills"", ""TotalDeaths"", ""TotalScore"",
                 ""TotalPlayTimeMinutes"", ""AvgScorePerRound"", ""KdRatio"", ""KillRate"", ""FirstRoundTime"", ""LastRoundTime"", ""UpdatedAt"")
                VALUES ");

            var parameters = new List<object>();
            for (int i = 0; i < batchRecords.Count; i++)
            {
                var r = batchRecords[i];
                if (i > 0) sql.Append(", ");
                var pi = i * 14;
                sql.Append($"(@p{pi}, @p{pi + 1}, @p{pi + 2}, @p{pi + 3}, @p{pi + 4}, @p{pi + 5}, @p{pi + 6}, @p{pi + 7}, @p{pi + 8}, @p{pi + 9}, @p{pi + 10}, @p{pi + 11}, @p{pi + 12}, @p{pi + 13})");
                parameters.AddRange([
                    r.PlayerName, r.Year, r.Month, r.TotalRounds, r.TotalKills, r.TotalDeaths, r.TotalScore,
                    r.TotalPlayTimeMinutes, r.AvgScorePerRound, r.KdRatio, r.KillRate,
                    r.FirstRoundTime.ToString(), r.LastRoundTime.ToString(), r.UpdatedAt.ToString()
                ]);
            }
            sql.Append(';');
            await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());
        }
    }

    private async Task BulkInsertPlayerServerStats(PlayerTrackerDbContext dbContext, List<PlayerServerStats> records)
    {
        if (records.Count == 0) return;

        const int batchSize = 200;
        for (int batch = 0; batch < records.Count; batch += batchSize)
        {
            var batchRecords = records.Skip(batch).Take(batchSize).ToList();
            var sql = new StringBuilder(@"
                INSERT INTO ""PlayerServerStats""
                (""PlayerName"", ""ServerGuid"", ""Year"", ""Week"", ""TotalRounds"", ""TotalKills"", ""TotalDeaths"", ""TotalScore"", ""TotalPlayTimeMinutes"", ""UpdatedAt"")
                VALUES ");

            var parameters = new List<object>();
            for (int i = 0; i < batchRecords.Count; i++)
            {
                var r = batchRecords[i];
                if (i > 0) sql.Append(", ");
                var pi = i * 10;
                sql.Append($"(@p{pi}, @p{pi + 1}, @p{pi + 2}, @p{pi + 3}, @p{pi + 4}, @p{pi + 5}, @p{pi + 6}, @p{pi + 7}, @p{pi + 8}, @p{pi + 9})");
                parameters.AddRange([
                    r.PlayerName, r.ServerGuid, r.Year, r.Week, r.TotalRounds, r.TotalKills, r.TotalDeaths,
                    r.TotalScore, r.TotalPlayTimeMinutes, r.UpdatedAt.ToString()
                ]);
            }
            sql.Append(';');
            await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());
        }
    }

    private async Task BulkInsertPlayerMapStats(PlayerTrackerDbContext dbContext, List<PlayerMapStats> records)
    {
        if (records.Count == 0) return;

        const int batchSize = 200;
        for (int batch = 0; batch < records.Count; batch += batchSize)
        {
            var batchRecords = records.Skip(batch).Take(batchSize).ToList();
            var sql = new StringBuilder(@"
                INSERT INTO ""PlayerMapStats""
                (""PlayerName"", ""MapName"", ""ServerGuid"", ""Year"", ""Month"", ""TotalRounds"", ""TotalKills"", ""TotalDeaths"", ""TotalScore"", ""TotalPlayTimeMinutes"", ""UpdatedAt"")
                VALUES ");

            var parameters = new List<object>();
            for (int i = 0; i < batchRecords.Count; i++)
            {
                var r = batchRecords[i];
                if (i > 0) sql.Append(", ");
                var pi = i * 11;
                sql.Append($"(@p{pi}, @p{pi + 1}, @p{pi + 2}, @p{pi + 3}, @p{pi + 4}, @p{pi + 5}, @p{pi + 6}, @p{pi + 7}, @p{pi + 8}, @p{pi + 9}, @p{pi + 10})");
                parameters.AddRange([
                    r.PlayerName, r.MapName, r.ServerGuid, r.Year, r.Month, r.TotalRounds, r.TotalKills,
                    r.TotalDeaths, r.TotalScore, r.TotalPlayTimeMinutes, r.UpdatedAt.ToString()
                ]);
            }
            sql.Append(';');
            await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());
        }
    }
}

// DTOs for raw SQL query results
public class PlayerStatsAggregateData
{
    public string PlayerName { get; set; } = string.Empty;
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public DateTime FirstRoundTime { get; set; }
    public DateTime LastRoundTime { get; set; }
}

public class PlayerServerStatsAggregateData
{
    public string PlayerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
}

public class PlayerMapStatsAggregateData
{
    public string PlayerName { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
}
