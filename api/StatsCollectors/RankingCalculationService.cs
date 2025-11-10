using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using Microsoft.Extensions.Logging;

namespace api.StatsCollectors;

public class RankingCalculationService(IServiceProvider services, ILogger<RankingCalculationService> logger) : BackgroundService
{

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
            try
            {
                logger.LogInformation("Starting ranking calculation for all servers");
                using var scope = services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

                await CalculateRankingsForAllServers(dbContext);
                logger.LogInformation("Ranking calculation completed successfully");
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating rankings");
            }
        }
    }

    private async Task CalculateRankingsForAllServers(PlayerTrackerDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;
        var currentYearString = currentYear.ToString();
        var currentMonthString = currentMonth.ToString("00");

        // Get all active servers
        var servers = await dbContext.Servers.ToListAsync();
        logger.LogInformation("Retrieved {ServerCount} active servers for ranking calculation", servers.Count);

        foreach (var server in servers)
        {
            logger.LogDebug("Processing rankings for server {ServerGuid} for {Year}-{Month}",
                server.Guid, currentYear, currentMonthString);

            try
            {
                // Delete existing rankings for this server, year, and month
                var existingRankingsForMonth = dbContext.ServerPlayerRankings
                    .Where(r => r.ServerGuid == server.Guid && r.Year == currentYear && r.Month == currentMonth);
                var existingCount = existingRankingsForMonth.Count();

                if (existingCount > 0)
                {
                    dbContext.ServerPlayerRankings.RemoveRange(existingRankingsForMonth);
                    await dbContext.SaveChangesAsync();
                    logger.LogDebug("Deleted {ExistingRankingCount} existing rankings for server {ServerGuid}",
                        existingCount, server.Guid);
                }

                // Use a raw SQL query to get the data for the current month, excluding bots
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
                    server.Guid, currentYearString, currentMonthString).ToListAsync();

                if (playerData.Count == 0)
                {
                    logger.LogDebug("No player data found for server {ServerGuid} in {Year}-{Month}",
                        server.Guid, currentYear, currentMonthString);
                    continue;
                }

                logger.LogDebug("Retrieved {PlayerCount} players for server {ServerGuid}",
                    playerData.Count, server.Guid);

                // Prepare ranking data
                var rankings = playerData
                    .Select((p, index) => new ServerPlayerRanking
                    {
                        ServerGuid = server.Guid,
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

                // Bulk insert in a single query instead of N individual INSERTs
                await BulkInsertRankings(dbContext, rankings, server.Guid);

                logger.LogInformation("Successfully calculated and inserted {RankingCount} rankings for server {ServerGuid}",
                    rankings.Count, server.Guid);
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error calculating rankings for server {ServerGuid}", server.Guid);
            }
        }
    }

    private async Task BulkInsertRankings(PlayerTrackerDbContext dbContext, List<ServerPlayerRanking> rankings, string serverGuid)
    {
        if (rankings.Count == 0)
            return;

        const int batchSize = 500; // 500 rows × 10 params = 5,000 params (safe under SQL Server's 2,100 limit with good margin)
        var batchCount = (rankings.Count + batchSize - 1) / batchSize;

        for (int batch = 0; batch < rankings.Count; batch += batchSize)
        {
            var batchRankings = rankings.Skip(batch).Take(batchSize).ToList();
            var currentBatch = (batch / batchSize) + 1;

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
                await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray());

                logger.LogDebug("Batch {CurrentBatch}/{TotalBatches} inserted ({RecordCount} rankings) for server {ServerGuid}",
                    currentBatch, batchCount, batchRankings.Count, serverGuid);
            }
            catch (Exception ex)
            {
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
