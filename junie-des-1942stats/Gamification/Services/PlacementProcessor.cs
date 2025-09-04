using System.Linq;
using System.Text.Json;
using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class TopSessionResult
{
    public int SessionId { get; set; }
    public string RoundId { get; set; } = string.Empty;
    public string PlayerName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public DateTime LastSeenTime { get; set; }
    public int? Team { get; set; }
    public string? TeamLabel { get; set; }
}

public class TopPlayerData
{
    public int SessionId { get; set; }
    public string PlayerName { get; set; } = string.Empty;
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public DateTime LastSeenTime { get; set; }
    public LatestObservationData? LatestObservation { get; set; }
}

public class LatestObservationData
{
    public int Team { get; set; }
    public string? TeamLabel { get; set; }
}

public record RoundData(
    string RoundId,
    string ServerGuid,
    string MapName,
    DateTime? EndTime,
    int? ParticipantCount
);

public record AchievementMetadata(
    int? Team,
    string? TeamLabel,
    string ServerName,
    int Score,
    int Kills,
    int Deaths,
    int? TotalPlayers
);

public class RoundWithTopPlayers
{
    public RoundData Round { get; set; } = null!;
    public List<TopPlayerData> TopPlayers { get; set; } = new();
}

public class PlacementProcessor
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ClickHouseGamificationService _clickHouseService;
    private readonly ILogger<PlacementProcessor> _logger;

    public PlacementProcessor(PlayerTrackerDbContext dbContext, ClickHouseGamificationService clickHouseService, ILogger<PlacementProcessor> logger)
    {
        _dbContext = dbContext;
        _clickHouseService = clickHouseService;
        _logger = logger;
    }

    /// <summary>
    /// Generate placement achievements (1st/2nd/3rd) for rounds completed since a timestamp.
    /// Excludes bot players. Uses the last observation per winning session to capture team info.
    /// Processes in batches for efficiency with large datasets.
    /// </summary>
    public async Task<List<Achievement>> ProcessPlacementsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var allAchievements = new List<Achievement>();
        const int batchSize = 2_000;
        int skip = 0;
        int totalProcessed = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                // First, get batch of rounds projected to RoundData
                var rounds = await _dbContext.Rounds.AsNoTracking()
                    .Where(r => r.EndTime != null && r.EndTime >= sinceUtc)
                    .OrderBy(r => r.EndTime)
                    .Skip(skip)
                    .Take(batchSize)
                    .Select(r => new RoundData(
                        r.RoundId,
                        r.ServerGuid,
                        r.MapName,
                        r.EndTime,
                        r.ParticipantCount
                    ))
                    .ToListAsync(cancellationToken);

                if (rounds.Count == 0)
                {
                    break; // No more rounds to process
                }

                // Get all round IDs for this batch
                var roundIds = rounds.Select(r => r.RoundId).Where(id => id != null).Cast<string>().ToList();

                // Use raw SQL to efficiently get top 3 sessions per round with their observations
                // This avoids the N+1 problem while keeping data transfer minimal
                var sql = @"
                    WITH RankedSessions AS (
                        SELECT 
                            ps.SessionId,
                            ps.RoundId,
                            ps.PlayerName,
                            ps.TotalScore,
                            ps.TotalKills,
                            ps.TotalDeaths,
                            ps.LastSeenTime,
                            ROW_NUMBER() OVER (
                                PARTITION BY ps.RoundId 
                                ORDER BY ps.TotalScore DESC, ps.TotalKills DESC, ps.SessionId ASC
                            ) as rn
                        FROM PlayerSessions ps
                        INNER JOIN Players p ON ps.PlayerName = p.Name
                        WHERE ps.RoundId IN ({0}) AND p.AiBot = 0
                    ),
                    TopSessions AS (
                        SELECT * FROM RankedSessions WHERE rn <= 3
                    ),
                    LatestObservations AS (
                        SELECT 
                            po.SessionId,
                            po.Team,
                            po.TeamLabel,
                            ROW_NUMBER() OVER (
                                PARTITION BY po.SessionId 
                                ORDER BY po.Timestamp DESC
                            ) as obs_rn
                        FROM PlayerObservations po
                        WHERE po.SessionId IN (SELECT SessionId FROM TopSessions)
                    )
                    SELECT 
                        ts.SessionId,
                        ts.RoundId,
                        ts.PlayerName,
                        ts.TotalScore,
                        ts.TotalKills,
                        ts.TotalDeaths,
                        ts.LastSeenTime,
                        lo.Team,
                        lo.TeamLabel
                    FROM TopSessions ts
                    LEFT JOIN LatestObservations lo ON ts.SessionId = lo.SessionId AND lo.obs_rn = 1
                    ORDER BY ts.RoundId, ts.rn";

                var roundIdParams = string.Join(",", roundIds.Select((_, i) => $"@p{i}"));
                var fullSql = sql.Replace("{0}", roundIdParams);
                
                var parameters = roundIds.Select((id, i) => new Microsoft.Data.Sqlite.SqliteParameter($"@p{i}", id)).ToArray();
                
                var topSessionsWithObservations = await _dbContext.Database
                    .SqlQueryRaw<TopSessionResult>(fullSql, parameters)
                    .ToListAsync(cancellationToken);

                // Group results by round
                var topPlayersWithObservationsByRound = topSessionsWithObservations
                    .GroupBy(s => s.RoundId)
                    .ToDictionary(
                        g => g.Key,
                        g => g.Select(s => new TopPlayerData
                        {
                            SessionId = s.SessionId,
                            PlayerName = s.PlayerName,
                            TotalScore = s.TotalScore,
                            TotalKills = s.TotalKills,
                            TotalDeaths = s.TotalDeaths,
                            LastSeenTime = s.LastSeenTime,
                            LatestObservation = s.Team != null ? new LatestObservationData { Team = s.Team.Value, TeamLabel = s.TeamLabel } : null
                        }).ToList()
                    );

                // Create the structure expected by ProcessRoundBatch
                var roundsWithTopPlayers = rounds.Select(r => new RoundWithTopPlayers
                {
                    Round = r,
                    TopPlayers = topPlayersWithObservationsByRound.GetValueOrDefault(r.RoundId, new List<TopPlayerData>())
                }).ToList();

                // Get server names for this batch
                var serverGuids = rounds.Select(r => r.ServerGuid).Distinct().ToList();
                var serverNamesByGuid = await _dbContext.Servers.AsNoTracking()
                    .Where(s => serverGuids.Contains(s.Guid))
                    .ToDictionaryAsync(s => s.Guid, s => s.Name, cancellationToken);

                // Process achievements for this batch
                var batchAchievements = ProcessRoundBatch(roundsWithTopPlayers, serverNamesByGuid, now);
                allAchievements.AddRange(batchAchievements);

                totalProcessed += rounds.Count;
                skip += batchSize;

                _logger.LogDebug("Processed batch of {BatchCount} rounds, total processed: {TotalProcessed}, achievements generated: {AchievementCount}", 
                    rounds.Count, totalProcessed, batchAchievements.Count);

                // If we got fewer rounds than batch size, we're done
                if (rounds.Count < batchSize)
                {
                    break;
                }
            }

            _logger.LogInformation("Generated {Count} placement achievements from {TotalRounds} rounds since {Since}", 
                allAchievements.Count, totalProcessed, sinceUtc);
            return allAchievements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing placements since {Since}", sinceUtc);
            throw;
        }
    }

    /// <summary>
    /// Process a batch of rounds with their top players into achievements
    /// </summary>
    private List<Achievement> ProcessRoundBatch(
        IEnumerable<RoundWithTopPlayers> roundsWithTopPlayers, 
        Dictionary<string, string> serverNamesByGuid, 
        DateTime processedAt)
    {
        var achievements = new List<Achievement>();

        foreach (var roundData in roundsWithTopPlayers)
        {
            var round = roundData.Round;
            var topPlayers = roundData.TopPlayers;

            if (topPlayers.Count == 0)
            {
                continue;
            }

            // Build achievements for placements
            for (int i = 0; i < topPlayers.Count && i < 3; i++)
            {
                var placement = i + 1; // 1, 2, 3
                var player = topPlayers[i];

                var tier = placement switch
                {
                    1 => BadgeTiers.Gold,
                    2 => BadgeTiers.Silver,
                    3 => BadgeTiers.Bronze,
                    _ => BadgeTiers.Bronze
                };

                var achievementName = placement switch
                {
                    1 => "1st Place",
                    2 => "2nd Place",
                    3 => "3rd Place",
                    _ => "Placement"
                };

                // Create strongly typed metadata
                var serverName = serverNamesByGuid.GetValueOrDefault(round.ServerGuid, "");
                var metadata = new AchievementMetadata(
                    player.LatestObservation?.Team,
                    player.LatestObservation?.TeamLabel,
                    serverName,
                    player.TotalScore,
                    player.TotalKills,
                    player.TotalDeaths,
                    round.ParticipantCount
                );

                var achievedAt = round.EndTime ?? player.LastSeenTime;

                achievements.Add(new Achievement
                {
                    PlayerName = player.PlayerName,
                    AchievementType = AchievementTypes.Placement,
                    AchievementId = $"round_placement_{placement}",
                    AchievementName = achievementName,
                    Tier = tier,
                    Value = (uint)placement,
                    AchievedAt = achievedAt,
                    ProcessedAt = processedAt,
                    ServerGuid = round.ServerGuid,
                    MapName = round.MapName,
                    RoundId = round.RoundId,
                    Metadata = JsonSerializer.Serialize(metadata)
                });
            }
        }

        return achievements;
    }
}


