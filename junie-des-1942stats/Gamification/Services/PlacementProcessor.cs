using System.Text.Json;
using junie_des_1942stats.Gamification.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class PlacementProcessor
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<PlacementProcessor> _logger;

    public PlacementProcessor(PlayerTrackerDbContext dbContext, ILogger<PlacementProcessor> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    /// <summary>
    /// Generate placement achievements (1st/2nd/3rd) for rounds completed since a timestamp.
    /// Excludes bot players. Uses the last observation per winning session to capture team info.
    /// </summary>
    public async Task<List<Achievement>> ProcessPlacementsSinceAsync(DateTime sinceUtc, CancellationToken cancellationToken = default)
    {
        var now = DateTime.UtcNow;
        var achievements = new List<Achievement>();

        try
        {
            // Query recently completed rounds
            var rounds = await _dbContext.Rounds.AsNoTracking()
                .Where(r => r.EndTime != null && r.EndTime >= sinceUtc)
                .OrderBy(r => r.EndTime)
                .ToListAsync(cancellationToken);

            if (rounds.Count == 0)
            {
                return achievements;
            }

            // Preload server names for encountered servers
            var serverGuids = rounds.Select(r => r.ServerGuid).Distinct().ToList();
            var serverNamesByGuid = await _dbContext.Servers.AsNoTracking()
                .Where(s => serverGuids.Contains(s.Guid))
                .ToDictionaryAsync(s => s.Guid, s => s.Name, cancellationToken);

            foreach (var round in rounds)
            {
                cancellationToken.ThrowIfCancellationRequested();

                // Get all sessions in this round (exclude bots)
                var sessions = await _dbContext.PlayerSessions.AsNoTracking()
                    .Include(s => s.Player)
                    .Where(s => s.RoundId == round.RoundId && !s.Player.AiBot)
                    .Select(s => new
                    {
                        s.SessionId,
                        s.PlayerName,
                        s.TotalScore,
                        s.TotalKills,
                        s.ServerGuid,
                        s.MapName,
                        s.LastSeenTime
                    })
                    .ToListAsync(cancellationToken);

                if (sessions.Count == 0)
                {
                    continue;
                }

                // Rank sessions by score desc, kills desc, then SessionId asc for consistency
                var topThree = sessions
                    .OrderByDescending(s => s.TotalScore)
                    .ThenByDescending(s => s.TotalKills)
                    .ThenBy(s => s.SessionId)
                    .Take(3)
                    .ToList();

                if (topThree.Count == 0)
                {
                    continue;
                }

                // Fetch last observations for winners to capture team info
                var winnerSessionIds = topThree.Select(s => s.SessionId).ToList();
                var winnerObservations = await _dbContext.PlayerObservations.AsNoTracking()
                    .Where(o => winnerSessionIds.Contains(o.SessionId))
                    .OrderByDescending(o => o.Timestamp)
                    .ToListAsync(cancellationToken);

                var lastObsBySession = winnerObservations
                    .GroupBy(o => o.SessionId)
                    .ToDictionary(g => g.Key, g => g.First());

                // Build achievements for placements
                for (int i = 0; i < topThree.Count; i++)
                {
                    var placement = i + 1; // 1, 2, 3
                    var s = topThree[i];
                    var lastObs = lastObsBySession.GetValueOrDefault(s.SessionId);

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

                    // Metadata includes team info and server name for richer queries later
                    var serverName = serverNamesByGuid.GetValueOrDefault(round.ServerGuid, "");
                    var metadata = new
                    {
                        team = lastObs?.Team,
                        team_label = lastObs?.TeamLabel,
                        server_name = serverName,
                        score = s.TotalScore,
                        kills = s.TotalKills
                    };

                    var achievedAt = round.EndTime ?? s.LastSeenTime;

                    achievements.Add(new Achievement
                    {
                        PlayerName = s.PlayerName,
                        AchievementType = AchievementTypes.Placement,
                        AchievementId = $"placement_{placement}_round_{round.RoundId}",
                        AchievementName = achievementName,
                        Tier = tier,
                        Value = (uint)placement,
                        AchievedAt = achievedAt ?? DateTime.UtcNow,
                        ProcessedAt = now,
                        ServerGuid = round.ServerGuid,
                        MapName = round.MapName,
                        RoundId = round.RoundId,
                        Metadata = JsonSerializer.Serialize(metadata)
                    });
                }
            }

            _logger.LogInformation("Generated {Count} placement achievements since {Since}", achievements.Count, sinceUtc);
            return achievements;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing placements since {Since}", sinceUtc);
            throw;
        }
    }
}


