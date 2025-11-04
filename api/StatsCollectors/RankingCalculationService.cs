using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace junie_des_1942stats.StatsCollectors;

public class RankingCalculationService(IServiceProvider services) : BackgroundService
{
    private readonly IServiceProvider _services = services;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        while (!stoppingToken.IsCancellationRequested)
        {
            await Task.Delay(TimeSpan.FromHours(1), stoppingToken);
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
        }
    }

    private async Task CalculateRankingsForAllServers(PlayerTrackerDbContext dbContext)
    {
        var now = DateTime.UtcNow;
        var currentYear = now.Year;
        var currentMonth = now.Month;
        var currentYearString = currentYear.ToString();
        // SQLite strftime('%m') returns month as '01', '02', ..., '12'
        var currentMonthString = currentMonth.ToString("00");

        // Get all active servers
        var servers = await dbContext.Servers.ToListAsync();

        foreach (var server in servers)
        {
            // Delete existing rankings for this server, year, and month
            var existingRankingsForMonth = dbContext.ServerPlayerRankings
                .Where(r => r.ServerGuid == server.Guid && r.Year == currentYear && r.Month == currentMonth);
            dbContext.ServerPlayerRankings.RemoveRange(existingRankingsForMonth);
            await dbContext.SaveChangesAsync(); // Save changes before adding new ones

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

            // Update rankings table
            int rank = 1;
            foreach (var playerScore in playerData)
            {
                dbContext.ServerPlayerRankings.Add(new ServerPlayerRanking
                {
                    ServerGuid = server.Guid,
                    PlayerName = playerScore.PlayerName,
                    Rank = rank,
                    Year = currentYear,
                    Month = currentMonth,
                    TotalScore = playerScore.TotalScore,
                    TotalKills = playerScore.TotalKills,
                    TotalDeaths = playerScore.TotalDeaths,
                    KDRatio = playerScore.TotalDeaths > 0
                        ? Math.Round((double)playerScore.TotalKills / playerScore.TotalDeaths, 2)
                        : playerScore.TotalKills,
                    TotalPlayTimeMinutes = playerScore.TotalPlayTimeMinutes
                });

                rank++;
            }

            await dbContext.SaveChangesAsync();
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