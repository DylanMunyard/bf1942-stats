using System.Diagnostics;
using System.Linq;
using System.Text.Json;
using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Telemetry;
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
        using var activity = ActivitySources.Gamification.StartActivity("PlacementProcessor.ProcessPlacementsSinceAsync");
        activity?.SetTag("since_utc", sinceUtc.ToString("O"));
        
        var now = DateTime.UtcNow;
        var allAchievements = new List<Achievement>();
        const int batchSize = 2_000;
        int skip = 0;
        int totalProcessed = 0;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                using var batchActivity = ActivitySources.Gamification.StartActivity("PlacementProcessor.ProcessBatch");
                batchActivity?.SetTag("batch_size", batchSize);
                batchActivity?.SetTag("skip", skip);
                
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
                    batchActivity?.SetTag("rounds_found", 0);
                    break; // No more rounds to process
                }
                
                batchActivity?.SetTag("rounds_found", rounds.Count);

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
                
                using var sqlActivity = ActivitySources.Gamification.StartActivity("PlacementProcessor.GetTopSessions");
                sqlActivity?.SetTag("round_count", rounds.Count);
                
                var topSessionsWithObservations = await _dbContext.Database
                    .SqlQueryRaw<TopSessionResult>(fullSql, parameters)
                    .ToListAsync(cancellationToken);
                    
                sqlActivity?.SetTag("top_sessions_found", topSessionsWithObservations.Count);

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
                using var processingActivity = ActivitySources.Gamification.StartActivity("PlacementProcessor.ProcessRoundBatch");
                processingActivity?.SetTag("rounds_to_process", rounds.Count);
                processingActivity?.SetTag("total_top_sessions", topSessionsWithObservations.Count);
                
                var batchAchievements = ProcessRoundBatch(roundsWithTopPlayers, serverNamesByGuid, now);
                
                processingActivity?.SetTag("achievements_generated", batchAchievements.Count);
                batchActivity?.SetTag("achievements_generated", batchAchievements.Count);
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

            activity?.SetTag("total_achievements_generated", allAchievements.Count);
            activity?.SetTag("total_rounds_processed", totalProcessed);
            
            _logger.LogInformation("Generated {Count} placement achievements from {TotalRounds} rounds since {Since}", 
                allAchievements.Count, totalProcessed, sinceUtc);
            return allAchievements;
        }
        catch (Exception ex)
        {
            activity?.SetTag("error", ex.Message);
            activity?.SetStatus(ActivityStatusCode.Error, $"Placement processing failed: {ex.Message}");
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
        using var activity = ActivitySources.Gamification.StartActivity("PlacementProcessor.ProcessRoundBatch");
        
        var achievements = new List<Achievement>();
        int roundsProcessed = 0;
        int roundsSkipped = 0;

        foreach (var roundData in roundsWithTopPlayers)
        {
            using var roundActivity = ActivitySources.Gamification.StartActivity("PlacementProcessor.ProcessRound");
            var round = roundData.Round;
            var topPlayers = roundData.TopPlayers;
            
            roundActivity?.SetTag("round_id", round.RoundId);
            roundActivity?.SetTag("map_name", round.MapName);
            roundActivity?.SetTag("top_players_count", topPlayers.Count);

            if (topPlayers.Count == 0)
            {
                roundsSkipped++;
                roundActivity?.SetTag("skipped_reason", "no_top_players");
                continue;
            }

            // Build achievements for placements
            var roundAchievements = 0;
            for (int i = 0; i < topPlayers.Count && i < 3; i++)
            {
                using var placementActivity = ActivitySources.Gamification.StartActivity("PlacementProcessor.CreatePlacementAchievement");
                var placement = i + 1; // 1, 2, 3
                var player = topPlayers[i];
                
                placementActivity?.SetTag("placement", placement);
                placementActivity?.SetTag("player_name", player.PlayerName);
                placementActivity?.SetTag("player_score", player.TotalScore);
                placementActivity?.SetTag("player_kills", player.TotalKills);
                placementActivity?.SetTag("player_deaths", player.TotalDeaths);

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

                var achievement = new Achievement
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
                    Metadata = JsonSerializer.Serialize(metadata),
                    Version = processedAt
                };
                
                achievements.Add(achievement);
                roundAchievements++;
                
                placementActivity?.SetTag("achievement_tier", tier.ToString());
                placementActivity?.SetTag("achievement_name", achievementName);
            }
            
            roundsProcessed++;
            roundActivity?.SetTag("achievements_created", roundAchievements);
        }

        activity?.SetTag("rounds_processed", roundsProcessed);
        activity?.SetTag("rounds_skipped", roundsSkipped);
        activity?.SetTag("total_achievements", achievements.Count);
        activity?.SetTag("placement_1st", achievements.Count(a => a.AchievementId == "round_placement_1"));
        activity?.SetTag("placement_2nd", achievements.Count(a => a.AchievementId == "round_placement_2"));
        activity?.SetTag("placement_3rd", achievements.Count(a => a.AchievementId == "round_placement_3"));
        
        return achievements;
    }
}


