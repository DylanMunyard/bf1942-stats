using System.Text;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace api.StatsCollectors;

public class ServerPlayerRankingsRecalculationService(
    IServiceScopeFactory scopeFactory,
    api.Services.IAggregateConcurrencyService concurrency,
    ILogger<ServerPlayerRankingsRecalculationService> logger
) : IServerPlayerRankingsRecalculationService
{
    public async Task<int> RecalculateForServerAndPeriodAsync(string serverGuid, int year, int month, CancellationToken ct = default)
    {
        using var scope = scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

        return await concurrency.ExecuteWithServerPlayerRankingsLockAsync(async (c) =>
        {
            var yearString = year.ToString();
            var monthString = month.ToString("00");

            await using var transaction = await dbContext.Database.BeginTransactionAsync(c);
            try
            {
                await dbContext.Database.ExecuteSqlRawAsync(@"
                    DELETE FROM ""ServerPlayerRankings""
                    WHERE ""ServerGuid"" = {0} AND ""Year"" = {1} AND ""Month"" = {2}",
                    serverGuid, year, month);

                var playerData = await dbContext.Database.SqlQueryRaw<PlayerRankingRow>(@"
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
                      AND (ps.IsDeleted = 0 OR ps.IsDeleted IS NULL)
                    GROUP BY ps.PlayerName
                    ORDER BY SUM(ps.TotalScore) DESC",
                    serverGuid, yearString, monthString).ToListAsync(c);

                if (playerData.Count == 0)
                {
                    await transaction.CommitAsync(c);
                    logger.LogDebug("No player data for server {ServerGuid} in {Year}-{Month}", serverGuid, year, monthString);
                    return 0;
                }

                var rankings = playerData
                    .Select((p, index) => new ServerPlayerRanking
                    {
                        ServerGuid = serverGuid,
                        PlayerName = p.PlayerName,
                        Rank = index + 1,
                        Year = year,
                        Month = month,
                        TotalScore = p.TotalScore,
                        TotalKills = p.TotalKills,
                        TotalDeaths = p.TotalDeaths,
                        KDRatio = p.TotalDeaths > 0 ? Math.Round((double)p.TotalKills / p.TotalDeaths, 2) : p.TotalKills,
                        TotalPlayTimeMinutes = p.TotalPlayTimeMinutes
                    })
                    .ToList();

                await BulkInsertRankingsAsync(dbContext, rankings, serverGuid, c);
                await transaction.CommitAsync(c);

                logger.LogDebug("Recalculated {Count} ServerPlayerRankings for server {ServerGuid} {Year}-{Month}",
                    rankings.Count, serverGuid, year, monthString);
                return rankings.Count;
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync(c);
                logger.LogError(ex, "Failed to recalculate ServerPlayerRankings for server {ServerGuid} {Year}-{Month}",
                    serverGuid, year, monthString);
                throw;
            }
        }, ct);
    }

    private static async Task BulkInsertRankingsAsync(
        PlayerTrackerDbContext dbContext,
        List<ServerPlayerRanking> rankings,
        string serverGuid,
        CancellationToken ct)
    {
        if (rankings.Count == 0) return;

        const int batchSize = 500;
        for (var batch = 0; batch < rankings.Count; batch += batchSize)
        {
            var batchRankings = rankings.Skip(batch).Take(batchSize).ToList();
            var sql = new StringBuilder(@"
                INSERT INTO ""ServerPlayerRankings""
                (""KDRatio"", ""Month"", ""PlayerName"", ""Rank"", ""ServerGuid"", ""TotalDeaths"", ""TotalKills"", ""TotalPlayTimeMinutes"", ""TotalScore"", ""Year"")
                VALUES ");

            var parameters = new List<object>();
            for (var i = 0; i < batchRankings.Count; i++)
            {
                var r = batchRankings[i];
                if (i > 0) sql.Append(", ");
                var paramIndex = i * 10;
                sql.Append($"(@p{paramIndex}, @p{paramIndex + 1}, @p{paramIndex + 2}, @p{paramIndex + 3}, @p{paramIndex + 4}, @p{paramIndex + 5}, @p{paramIndex + 6}, @p{paramIndex + 7}, @p{paramIndex + 8}, @p{paramIndex + 9})");
                parameters.AddRange([r.KDRatio, r.Month, r.PlayerName, r.Rank, r.ServerGuid, r.TotalDeaths, r.TotalKills, r.TotalPlayTimeMinutes, r.TotalScore, r.Year]);
            }

            await dbContext.Database.ExecuteSqlRawAsync(sql.ToString(), parameters.ToArray(), ct);
        }
    }

    private class PlayerRankingRow
    {
        public string PlayerName { get; set; } = "";
        public int TotalScore { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public int TotalPlayTimeMinutes { get; set; }
    }
}
