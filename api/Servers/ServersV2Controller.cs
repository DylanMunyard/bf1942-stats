using api.Caching;
using api.Constants;
using api.Gamification.Models;
using api.PlayerStats;
using api.PlayerTracking;
using api.Servers.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.Servers;

[ApiController]
[Route("stats/v2/servers")]
public class ServersV2Controller(
    PlayerTrackerDbContext dbContext,
    ISqliteLeaderboardService sqliteLeaderboardService,
    ICacheService cacheService,
    ICacheKeyService cacheKeyService,
    ILogger<ServersV2Controller> logger) : ControllerBase
{
    // Get server leaderboards for a specific time period (SQLite leaderboards)
    [HttpGet("{serverName}/leaderboards")]
    public async Task<ActionResult<ServerLeaderboards>> GetServerLeaderboards(
        string serverName,
        [FromQuery] int days = ApiConstants.TimePeriods.DefaultDays,
        [FromQuery] int? minPlayersForWeighting = null)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest(ApiConstants.ValidationMessages.ServerNameEmpty);

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        logger.LogInformation("Getting v2 server leaderboards for '{ServerName}' with {Days} days", serverName, days);

        try
        {
            if (days <= 0)
            {
                return BadRequest("Days must be greater than 0");
            }

            var cacheKey = $"{cacheKeyService.GetServerLeaderboardsKey(serverName, days)}_v2_weight_{minPlayersForWeighting}";
            var cachedResult = await cacheService.GetAsync<ServerLeaderboards>(cacheKey);

            if (cachedResult != null)
            {
                return Ok(cachedResult);
            }

            var server = await dbContext.Servers
                .AsNoTracking()
                .FirstOrDefaultAsync(s => s.Name == serverName);

            if (server == null)
            {
                logger.LogWarning("Server not found in database: '{ServerName}'", serverName);
                return NotFound($"Server '{serverName}' not found");
            }

            var endPeriod = DateTime.UtcNow;
            var startPeriod = endPeriod.AddDays(-days);

            var leaderboards = new ServerLeaderboards
            {
                ServerGuid = server.Guid,
                ServerName = server.Name,
                Days = days,
                StartPeriod = startPeriod,
                EndPeriod = endPeriod
            };

            // SQLite leaderboards
            var mostActivePlayersTask = sqliteLeaderboardService.GetMostActivePlayersAsync(server.Guid, startPeriod, endPeriod, 10);
            var topScoresTask = sqliteLeaderboardService.GetTopScoresAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKDRatiosTask = sqliteLeaderboardService.GetTopKDRatiosAsync(server.Guid, startPeriod, endPeriod, 10);
            var topKillRatesTask = sqliteLeaderboardService.GetTopKillRatesAsync(server.Guid, startPeriod, endPeriod, 10);
            var topPlacementsTask = GetPlacementLeaderboardAsync(server.Guid, startPeriod, endPeriod, 10);

            await Task.WhenAll(mostActivePlayersTask, topScoresTask, topKDRatiosTask, topKillRatesTask, topPlacementsTask);

            leaderboards.MostActivePlayersByTime = await mostActivePlayersTask;
            leaderboards.TopScores = await topScoresTask;
            leaderboards.TopKDRatios = await topKDRatiosTask;
            leaderboards.TopKillRates = await topKillRatesTask;
            leaderboards.TopPlacements = await topPlacementsTask;

            if (minPlayersForWeighting.HasValue)
            {
                leaderboards.MinPlayersForWeighting = minPlayersForWeighting.Value;
                leaderboards.WeightedTopPlacements = await GetWeightedPlacementLeaderboardAsync(
                    server.Guid,
                    startPeriod,
                    endPeriod,
                    10,
                    minPlayersForWeighting.Value);
            }

            await cacheService.SetAsync(cacheKey, leaderboards, TimeSpan.FromMinutes(10));

            return Ok(leaderboards);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    /// <summary>
    /// Placement leaderboard uses existing ClickHouse achievements table for now.
    /// </summary>
    private async Task<List<PlacementLeaderboardEntry>> GetPlacementLeaderboardAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit = 10)
    {
        try
        {
            var startInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(startPeriod, DateTimeKind.Utc));
            var endInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endPeriod, DateTimeKind.Utc));

            var results = await dbContext.PlayerAchievements
                .AsNoTracking()
                .Where(pa => pa.AchievementType == AchievementTypes.Placement
                             && pa.ServerGuid == serverGuid
                             && pa.AchievedAt >= startInstant
                             && pa.AchievedAt < endInstant)
                .GroupBy(pa => pa.PlayerName)
                .Select(g => new
                {
                    PlayerName = g.Key,
                    FirstPlaces = g.Count(pa => pa.Tier == "gold"),
                    SecondPlaces = g.Count(pa => pa.Tier == "silver"),
                    ThirdPlaces = g.Count(pa => pa.Tier == "bronze")
                })
                .Where(x => x.FirstPlaces > 0 || x.SecondPlaces > 0 || x.ThirdPlaces > 0)
                .OrderByDescending(x => x.FirstPlaces)
                .ThenByDescending(x => x.SecondPlaces)
                .ThenByDescending(x => x.ThirdPlaces)
                .Take(limit)
                .ToListAsync();

            return results.Select((entry, index) => new PlacementLeaderboardEntry
            {
                Rank = index + 1,
                PlayerName = entry.PlayerName,
                FirstPlaces = entry.FirstPlaces,
                SecondPlaces = entry.SecondPlaces,
                ThirdPlaces = entry.ThirdPlaces
            }).ToList();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching v2 placement leaderboard for server {ServerGuid}", serverGuid);
            return new List<PlacementLeaderboardEntry>();
        }
    }

    private async Task<List<PlacementLeaderboardEntry>> GetWeightedPlacementLeaderboardAsync(
        string serverGuid,
        DateTime startPeriod,
        DateTime endPeriod,
        int limit,
        int minPlayerCount)
    {
        try
        {
            var startInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(startPeriod, DateTimeKind.Utc));
            var endInstant = Instant.FromDateTimeUtc(DateTime.SpecifyKind(endPeriod, DateTimeKind.Utc));

            var connection = dbContext.Database.GetDbConnection();
            if (connection.State != System.Data.ConnectionState.Open)
            {
                await connection.OpenAsync();
            }

            await using var command = connection.CreateCommand();
            command.CommandText = @"
SELECT
    PlayerName,
    SUM(CASE WHEN Tier = 'gold' THEN 1 ELSE 0 END) AS FirstPlaces,
    SUM(CASE WHEN Tier = 'silver' THEN 1 ELSE 0 END) AS SecondPlaces,
    SUM(CASE WHEN Tier = 'bronze' THEN 1 ELSE 0 END) AS ThirdPlaces
FROM PlayerAchievements
WHERE AchievementType = $achievementType
  AND ServerGuid = $serverGuid
  AND AchievedAt >= $startInstant
  AND AchievedAt < $endInstant
  AND COALESCE(CAST(json_extract(Metadata, '$.TotalPlayers') AS INTEGER), 0) >= $minPlayerCount
GROUP BY PlayerName
HAVING FirstPlaces > 0 OR SecondPlaces > 0 OR ThirdPlaces > 0
ORDER BY FirstPlaces DESC, SecondPlaces DESC, ThirdPlaces DESC
LIMIT $limit";

            var pAchievementType = command.CreateParameter();
            pAchievementType.ParameterName = "$achievementType";
            pAchievementType.Value = AchievementTypes.Placement;
            command.Parameters.Add(pAchievementType);

            var pServerGuid = command.CreateParameter();
            pServerGuid.ParameterName = "$serverGuid";
            pServerGuid.Value = serverGuid;
            command.Parameters.Add(pServerGuid);

            var pStartInstant = command.CreateParameter();
            pStartInstant.ParameterName = "$startInstant";
            pStartInstant.Value = startInstant.ToString();
            command.Parameters.Add(pStartInstant);

            var pEndInstant = command.CreateParameter();
            pEndInstant.ParameterName = "$endInstant";
            pEndInstant.Value = endInstant.ToString();
            command.Parameters.Add(pEndInstant);

            var pMinPlayerCount = command.CreateParameter();
            pMinPlayerCount.ParameterName = "$minPlayerCount";
            pMinPlayerCount.Value = minPlayerCount;
            command.Parameters.Add(pMinPlayerCount);

            var pLimit = command.CreateParameter();
            pLimit.ParameterName = "$limit";
            pLimit.Value = limit;
            command.Parameters.Add(pLimit);

            var entries = new List<PlacementLeaderboardEntry>();
            await using var reader = await command.ExecuteReaderAsync();
            var rank = 1;
            while (await reader.ReadAsync())
            {
                entries.Add(new PlacementLeaderboardEntry
                {
                    Rank = rank++,
                    PlayerName = reader.GetString(0),
                    FirstPlaces = reader.GetInt32(1),
                    SecondPlaces = reader.GetInt32(2),
                    ThirdPlaces = reader.GetInt32(3)
                });
            }

            return entries;
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching v2 weighted placement leaderboard for server {ServerGuid}", serverGuid);
            return new List<PlacementLeaderboardEntry>();
        }
    }
}
