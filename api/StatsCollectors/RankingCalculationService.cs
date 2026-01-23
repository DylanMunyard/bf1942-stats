using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Diagnostics;
using System.Text;
using Microsoft.Extensions.Logging;
using Serilog.Context;

namespace api.StatsCollectors;

public class RankingCalculationService(IServiceProvider services, ILogger<RankingCalculationService> logger) : BackgroundService
{
    private static readonly TimeSpan StartupDelay = TimeSpan.FromMinutes(1);

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        logger.LogInformation("RankingCalculationService started, waiting {Delay} before first run", StartupDelay);

        // Delay startup to avoid blocking Kestrel initialization
        await Task.Delay(StartupDelay, stoppingToken);

        while (!stoppingToken.IsCancellationRequested)
        {
            using var activity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.Cycle");
            activity?.SetTag("bulk_operation", "true");

            var cycleStopwatch = Stopwatch.StartNew();
            try
            {
                logger.LogInformation("Starting ranking calculation for all servers");

                using (LogContext.PushProperty("operation_type", "ranking_calculation"))
                using (LogContext.PushProperty("bulk_operation", true))
                using (var scope = services.CreateScope())
                {
                    var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

                    await CalculateRankingsForAllServers(dbContext);

                    cycleStopwatch.Stop();
                    activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                    logger.LogInformation("Ranking calculation completed successfully in {DurationMs}ms", cycleStopwatch.ElapsedMilliseconds);
                }
            }
            catch (Exception ex)
            {
                cycleStopwatch.Stop();
                activity?.SetTag("cycle_duration_ms", cycleStopwatch.ElapsedMilliseconds);
                activity?.SetTag("error", ex.Message);
                activity?.SetStatus(ActivityStatusCode.Error, $"Ranking calculation failed: {ex.Message}");
                logger.LogError(ex, "Error calculating rankings");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CalculateRankingsForAllServers(PlayerTrackerDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;
        var currentYearString = currentYear.ToString();
        var currentMonthString = currentMonth.ToString("00");

        using var calculateActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.CalculateRankingsForAllServers");
        calculateActivity?.SetTag("year", currentYear);
        calculateActivity?.SetTag("month", currentMonth);

        var servers = await dbContext.Servers.Select(s => s.Guid).ToListAsync();
        logger.LogInformation("Retrieved {ServerCount} active servers for ranking calculation", servers.Count);
        calculateActivity?.SetTag("server_count", servers.Count);

        var totalRankingsInserted = 0;
        var serversProcessed = 0;
        var serversWithErrors = 0;

        foreach (var serverGuid in servers)
        {
            using var serverActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.ProcessServer");
            serverActivity?.SetTag("server_guid", serverGuid);
            serverActivity?.SetTag("year", currentYear);
            serverActivity?.SetTag("month", currentMonth);

            logger.LogDebug("Processing rankings for server {ServerGuid} for {Year}-{Month}",
                serverGuid, currentYear, currentMonthString);

            await using var transaction = await dbContext.Database.BeginTransactionAsync();
            try
            {
                var deleteStopwatch = Stopwatch.StartNew();
                var deletedCount = await dbContext.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM ""ServerPlayerRankings""
                    WHERE ""ServerGuid"" = {0} AND ""Year"" = {1} AND ""Month"" = {2}",
                    serverGuid, currentYear, currentMonth);
                deleteStopwatch.Stop();
                serverActivity?.SetTag("deleted_count", deletedCount);
                serverActivity?.SetTag("delete_duration_ms", deleteStopwatch.ElapsedMilliseconds);

                logger.LogDebug("Deleted {DeletedCount} existing rankings for server {ServerGuid}",
                    deletedCount, serverGuid);

                var queryStopwatch = Stopwatch.StartNew();
                var playerData = await dbContext.Database.SqlQueryRaw<PlayerRankingData>(@"
                    SELECT
                        ps.PlayerName,
                        SUM(ps.TotalScore) AS TotalScore,
                        SUM(ps.TotalKills) AS TotalKills,
                        SUM(ps.TotalDeaths) AS TotalDeaths,
                        CAST(SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS INTEGER) AS TotalPlayTimeMinutes
                    FROM PlayerSessions ps
                    INNER JOIN Players p ON ps.PlayerName = p.Name
                    WHERE ps.ServerGuid = {0}
                      AND strftime('%Y', ps.StartTime) = {1}
                      AND strftime('%m', ps.StartTime) = {2}
                      AND p.AiBot = 0
                    GROUP BY ps.PlayerName
                    ORDER BY SUM(ps.TotalScore) DESC",
                    serverGuid, currentYearString, currentMonthString).ToListAsync();
                queryStopwatch.Stop();
                serverActivity?.SetTag("query_duration_ms", queryStopwatch.ElapsedMilliseconds);

                if (playerData.Count == 0)
                {
                    await transaction.CommitAsync();
                    serverActivity?.SetTag("rankings_inserted", 0);
                    logger.LogDebug("No player data found for server {ServerGuid} in {Year}-{Month}",
                        serverGuid, currentYear, currentMonthString);
                    serversProcessed++;
                    continue;
                }

                logger.LogDebug("Retrieved {PlayerCount} players for server {ServerGuid}",
                    playerData.Count, serverGuid);
                serverActivity?.SetTag("player_count", playerData.Count);

                var rankings = playerData
                    .Select((p, index) => new ServerPlayerRanking
                    {
                        ServerGuid = serverGuid,
                        PlayerName = p.PlayerName,
                        Rank = index + 1,
                        Year = currentYear,
                        Month = currentMonth,
                        TotalScore = p.TotalScore,
                        TotalKills = p.TotalKills,
                        TotalDeaths = p.TotalDeaths,
                        KDRatio = p.TotalDeaths > 0
                            ? Math.Round((double)p.TotalKills / p.TotalDeaths, 2)
                            : p.TotalKills,
                        TotalPlayTimeMinutes = p.TotalPlayTimeMinutes
                    })
                    .ToList();

                var insertStopwatch = Stopwatch.StartNew();
                await BulkInsertRankings(dbContext, rankings, serverGuid);
                insertStopwatch.Stop();
                serverActivity?.SetTag("insert_duration_ms", insertStopwatch.ElapsedMilliseconds);
                serverActivity?.SetTag("rankings_inserted", rankings.Count);

                await transaction.CommitAsync();

                totalRankingsInserted += rankings.Count;
                serversProcessed++;

                logger.LogInformation("Successfully calculated and inserted {RankingCount} rankings for server {ServerGuid}",
                    rankings.Count, serverGuid);
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                serversWithErrors++;
                serverActivity?.SetTag("error", ex.Message);
                serverActivity?.SetStatus(ActivityStatusCode.Error, $"Error processing server {serverGuid}: {ex.Message}");
                logger.LogError(ex, "Error calculating rankings for server {ServerGuid}", serverGuid);
            }
        }

        calculateActivity?.SetTag("total_rankings_inserted", totalRankingsInserted);
        calculateActivity?.SetTag("servers_processed", serversProcessed);
        calculateActivity?.SetTag("servers_with_errors", serversWithErrors);
    }

    private async Task BulkInsertRankings(PlayerTrackerDbContext dbContext, List<ServerPlayerRanking> rankings, string serverGuid)
    {
        if (rankings.Count == 0)
            return;

        const int batchSize = 500; // 500 rows × 10 params = 5,000 params (safe under SQL Server's 2,100 limit with good margin)
        var batchCount = (rankings.Count + batchSize - 1) / batchSize;

        using var bulkInsertActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.BulkInsertRankings");
        bulkInsertActivity?.SetTag("server_guid", serverGuid);
        bulkInsertActivity?.SetTag("total_rankings", rankings.Count);
        bulkInsertActivity?.SetTag("batch_count", batchCount);
        bulkInsertActivity?.SetTag("batch_size", batchSize);

        for (int batch = 0; batch < rankings.Count; batch += batchSize)
        {
            var batchRankings = rankings.Skip(batch).Take(batchSize).ToList();
            var currentBatch = (batch / batchSize) + 1;

            using var batchActivity = ActivitySources.RankingCalculation.StartActivity("RankingCalculation.InsertBatch");
            batchActivity?.SetTag("server_guid", serverGuid);
            batchActivity?.SetTag("batch_number", currentBatch);
            batchActivity?.SetTag("batch_size", batchRankings.Count);

            try
            {
                var sql = new StringBuilder(@"
            INSERT INTO ""ServerPlayerRankings""
            (""KDRatio"", ""Month"", ""PlayerName"", ""Rank"", ""ServerGuid"", ""TotalDeaths"", ""TotalKills"", ""TotalPlayTimeMinutes"", ""TotalScore"", ""Year"")
            VALUES ");

                var parameters = new List<object>();

                for (int i = 0; i < batchRankings.Count; i++)
                {
                    var r = batchRankings[i];
                    if (i > 0) sql.Append(", ");

                    var paramIndex = i * 10;
                    sql.Append($"(@p{paramIndex}, @p{paramIndex + 1}, @p{paramIndex + 2}, @p{paramIndex + 3}, @p{paramIndex + 4}, @p{paramIndex + 5}, @p{paramIndex + 6}, @p{paramIndex + 7}, @p{paramIndex + 8}, @p{paramIndex + 9})");

                    parameters.AddRange([r.KDRatio, r.Month, r.PlayerName, r.Rank, r.ServerGuid, r.TotalDeaths, r.TotalKills, r.TotalPlayTimeMinutes, r.TotalScore, r.Year]);
                }

                sql.Append(";");

                var insertStopwatch = Stopwatch.StartNew();
                await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());
                insertStopwatch.Stop();

                batchActivity?.SetTag("insert_duration_ms", insertStopwatch.ElapsedMilliseconds);

                logger.LogDebug("Batch {CurrentBatch}/{TotalBatches} inserted ({RecordCount} rankings) for server {ServerGuid}",
                    currentBatch, batchCount, batchRankings.Count, serverGuid);
            }
            catch (Exception ex)
            {
                batchActivity?.SetTag("error", ex.Message);
                batchActivity?.SetStatus(ActivityStatusCode.Error, $"Error inserting batch {currentBatch}: {ex.Message}");
                logger.LogError(ex, "Error inserting batch {CurrentBatch}/{TotalBatches} for server {ServerGuid}",
                    currentBatch, batchCount, serverGuid);
                throw;
            }
        }
    }
}

// Add this class to your file to support the raw SQL query results
public class PlayerRankingData
{
    public string PlayerName { get; set; } = string.Empty;
    public int TotalScore { get; set; } // Changed from HighestScore
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}
