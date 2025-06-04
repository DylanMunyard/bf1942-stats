using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace junie_des_1942stats.StatsCollectors;

public class RankingCalculationService : BackgroundService
{
    private readonly IServiceProvider _services;

    public RankingCalculationService(IServiceProvider services)
    {
        _services = services;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await Task.Yield(); // Ensure the service yields control quickly after starting
        while (!stoppingToken.IsCancellationRequested)
        {
            try
            {
                using var scope = _services.CreateScope();
                var dbContext = scope.ServiceProvider.GetRequiredService<PlayerTrackerDbContext>();

                await CalculateRankingsForAllServers(dbContext);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error collecting metrics: {ex.Message}");
            }

            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
        }
    }

    private async Task CalculateRankingsForAllServers(PlayerTrackerDbContext dbContext)
    {
        // Get all active servers
        var servers = await dbContext.Servers.ToListAsync();

        foreach (var server in servers)
        {
            // Use a raw SQL query to get the data with proper time calculation using julianday
            var playerData = await dbContext.Database.SqlQueryRaw<PlayerRankingData>(@"
            SELECT 
                ps.PlayerName,
                MAX(ps.TotalScore) AS HighestScore,
                SUM(ps.TotalKills) AS TotalKills,
                SUM(ps.TotalDeaths) AS TotalDeaths,
                CAST(SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS INTEGER) AS TotalPlayTimeMinutes
            FROM PlayerSessions ps
            WHERE ps.ServerGuid = {0}
            GROUP BY ps.PlayerName
            ORDER BY MAX(ps.TotalScore) DESC",
                server.Guid).ToListAsync();

            // Update rankings table
            int rank = 1;
            foreach (var playerScore in playerData)
            {
                var existingRanking = await dbContext.ServerPlayerRankings
                    .FirstOrDefaultAsync(r => r.ServerGuid == server.Guid && r.PlayerName == playerScore.PlayerName);

                if (existingRanking != null)
                {
                    existingRanking.Rank = rank;
                    existingRanking.HighestScore = playerScore.HighestScore;
                    existingRanking.TotalKills = playerScore.TotalKills;
                    existingRanking.TotalDeaths = playerScore.TotalDeaths;
                    existingRanking.KDRatio = playerScore.TotalDeaths > 0
                        ? Math.Round((double)playerScore.TotalKills / playerScore.TotalDeaths, 2)
                        : playerScore.TotalKills;
                    existingRanking.TotalPlayTimeMinutes = playerScore.TotalPlayTimeMinutes;
                    existingRanking.LastUpdated = DateTime.UtcNow;
                }
                else
                {
                    dbContext.ServerPlayerRankings.Add(new ServerPlayerRanking
                    {
                        ServerGuid = server.Guid,
                        PlayerName = playerScore.PlayerName,
                        Rank = rank,
                        HighestScore = playerScore.HighestScore,
                        TotalKills = playerScore.TotalKills,
                        TotalDeaths = playerScore.TotalDeaths,
                        KDRatio = playerScore.TotalDeaths > 0
                            ? Math.Round((double)playerScore.TotalKills / playerScore.TotalDeaths, 2)
                            : playerScore.TotalKills,
                        TotalPlayTimeMinutes = playerScore.TotalPlayTimeMinutes,
                        LastUpdated = DateTime.UtcNow
                    });
                }

                rank++;
            }

            await dbContext.SaveChangesAsync();
        }
    }
}

// Add this class to your file to support the raw SQL query results
public class PlayerRankingData
{
    public string PlayerName { get; set; }
    public int HighestScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}