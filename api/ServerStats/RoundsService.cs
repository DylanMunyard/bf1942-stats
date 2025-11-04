using api.PlayerTracking;
using api.ServerStats.Models;
using api.ClickHouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.ServerStats;

public class RoundsService(PlayerTrackerDbContext dbContext, ILogger<RoundsService> logger, PlayerRoundsReadService? clickHouseReader = null)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly ILogger<RoundsService> _logger = logger;
    private readonly PlayerRoundsReadService? _clickHouseReader = clickHouseReader;

    public async Task<List<RoundInfo>> GetRecentRoundsAsync(string serverGuid, int limit)
    {
        var rounds = await _dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.ServerGuid == serverGuid && (r.ParticipantCount ?? 0) > 0)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .Select(r => new
            {
                r.RoundId,
                r.MapName,
                r.StartTime,
                r.EndTime,
                r.IsActive,
                r.ParticipantCount,
                r.Tickets1,
                r.Tickets2,
                r.Team1Label,
                r.Team2Label
            })
            .ToListAsync();

        var roundIds = rounds.Select(r => r.RoundId).ToList();

        var topPlayers = await _dbContext.PlayerSessions
            .AsNoTracking()
            .Where(ps => ps.RoundId != null && roundIds.Contains(ps.RoundId) && !ps.Player.AiBot)
            .GroupBy(ps => ps.RoundId)
            .Select(g => new
            {
                RoundId = g.Key!,
                TopPlayer = g.OrderByDescending(s => s.TotalScore)
                    .Select(s => new { s.PlayerName, s.TotalScore })
                    .FirstOrDefault()
            })
            .ToDictionaryAsync(x => x.RoundId, x => x.TopPlayer);

        return rounds.Select(r => new RoundInfo
        {
            RoundId = r.RoundId,
            MapName = r.MapName,
            StartTime = r.StartTime,
            EndTime = r.EndTime ?? DateTime.UtcNow,
            IsActive = r.IsActive,
            ParticipantCount = r.ParticipantCount ?? 0,
            WinningTeamLabel = DetermineWinningTeam(r.Tickets1, r.Tickets2, r.Team1Label, r.Team2Label),
            WinningTeamScore = GetWinningScore(r.Tickets1, r.Tickets2),
            LosingTeamScore = GetLosingScore(r.Tickets1, r.Tickets2),
            TopPlayerName = topPlayers.TryGetValue(r.RoundId, out var topPlayer) ? topPlayer?.PlayerName : null,
            TopPlayerScore = topPlayers.TryGetValue(r.RoundId, out var tp) ? tp?.TotalScore : null
        }).ToList();
    }

    private static string? DetermineWinningTeam(int? tickets1, int? tickets2, string? team1Label, string? team2Label)
    {
        if (!tickets1.HasValue || !tickets2.HasValue)
            return null;

        return tickets1.Value > tickets2.Value ? team1Label : team2Label;
    }

    private static int? GetWinningScore(int? tickets1, int? tickets2)
    {
        if (!tickets1.HasValue || !tickets2.HasValue)
            return null;

        return Math.Max(tickets1.Value, tickets2.Value);
    }

    private static int? GetLosingScore(int? tickets1, int? tickets2)
    {
        if (!tickets1.HasValue || !tickets2.HasValue)
            return null;

        return Math.Min(tickets1.Value, tickets2.Value);
    }

    public async Task<PlayerStats.Models.PagedResult<RoundWithPlayers>> GetRounds(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        RoundFilters filters,
        bool includePlayers = true,
        bool onlySpecifiedPlayers = false)
    {
        var query = _dbContext.Rounds.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.ServerName))
        {
            query = query.Where(r => r.ServerName.Contains(filters.ServerName));
        }

        if (!string.IsNullOrWhiteSpace(filters.ServerGuid))
        {
            query = query.Where(r => r.ServerGuid == filters.ServerGuid);
        }

        if (!string.IsNullOrWhiteSpace(filters.MapName))
        {
            query = query.Where(r => r.MapName.Contains(filters.MapName));
        }

        if (!string.IsNullOrWhiteSpace(filters.GameType))
        {
            query = query.Where(r => r.GameType == filters.GameType);
        }

        if (!string.IsNullOrWhiteSpace(filters.GameId))
        {
            query = query.Where(r => r.ServerGuid.Contains(filters.GameId));
        }

        if (filters.StartTimeFrom.HasValue)
        {
            query = query.Where(r => r.StartTime >= filters.StartTimeFrom.Value);
        }

        if (filters.StartTimeTo.HasValue)
        {
            query = query.Where(r => r.StartTime <= filters.StartTimeTo.Value);
        }

        if (filters.EndTimeFrom.HasValue)
        {
            query = query.Where(r => r.EndTime >= filters.EndTimeFrom.Value);
        }

        if (filters.EndTimeTo.HasValue)
        {
            query = query.Where(r => r.EndTime <= filters.EndTimeTo.Value);
        }

        if (filters.MinDuration.HasValue)
        {
            query = query.Where(r => r.DurationMinutes >= filters.MinDuration.Value);
        }

        if (filters.MaxDuration.HasValue)
        {
            query = query.Where(r => r.DurationMinutes <= filters.MaxDuration.Value);
        }

        if (filters.MinParticipants.HasValue)
        {
            query = query.Where(r => r.ParticipantCount >= filters.MinParticipants.Value);
        }

        if (filters.MaxParticipants.HasValue)
        {
            query = query.Where(r => r.ParticipantCount <= filters.MaxParticipants.Value);
        }

        if (filters.IsActive.HasValue)
        {
            query = query.Where(r => r.IsActive == filters.IsActive.Value);
        }

        // Filter by player names: require that ALL specified players are present (AND semantics)
        if (filters.PlayerNames != null && filters.PlayerNames.Any())
        {
            var names = filters.PlayerNames
                .Where(n => !string.IsNullOrWhiteSpace(n))
                .Select(n => n.Trim())
                .Distinct()
                .ToList();

            if (names.Count > 0)
            {
                _logger.LogInformation("Filtering rounds by ALL player names: {PlayerNames}", string.Join(", ", names));

                // Subquery: find roundIds that contain all requested player names
                var matchingRoundIds = _dbContext.PlayerSessions
                    .AsNoTracking()
                    .Where(ps => ps.RoundId != null && names.Contains(ps.PlayerName))
                    .GroupBy(ps => ps.RoundId!)
                    .Where(g => g.Select(ps => ps.PlayerName).Distinct().Count() == names.Count)
                    .Select(g => g.Key);

                query = query.Where(r => r.RoundId != null && matchingRoundIds.Contains(r.RoundId));
            }
        }

        // Apply sorting
        query = sortBy.ToLowerInvariant() switch
        {
            "roundid" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.RoundId)
                : query.OrderByDescending(r => r.RoundId),
            "servername" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.ServerName)
                : query.OrderByDescending(r => r.ServerName),
            "mapname" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.MapName)
                : query.OrderByDescending(r => r.MapName),
            "gametype" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.GameType)
                : query.OrderByDescending(r => r.GameType),
            "endtime" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.EndTime)
                : query.OrderByDescending(r => r.EndTime),
            "durationminutes" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.DurationMinutes)
                : query.OrderByDescending(r => r.DurationMinutes),
            "participantcount" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.ParticipantCount)
                : query.OrderByDescending(r => r.ParticipantCount),
            "isactive" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.IsActive)
                : query.OrderByDescending(r => r.IsActive),
            _ => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(r => r.StartTime)
                : query.OrderByDescending(r => r.StartTime)
        };

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination and get rounds
        var rounds = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Convert to RoundWithPlayers
        var result = rounds.Select(round => new RoundWithPlayers
        {
            RoundId = round.RoundId,
            ServerName = round.ServerName,
            ServerGuid = round.ServerGuid,
            MapName = round.MapName,
            GameType = round.GameType,
            StartTime = round.StartTime,
            EndTime = round.EndTime ?? DateTime.UtcNow,
            DurationMinutes = round.DurationMinutes ?? 0,
            ParticipantCount = round.ParticipantCount ?? 0,
            IsActive = round.IsActive,
            Team1Label = round.Team1Label,
            Team2Label = round.Team2Label,
            Players = new List<PlayerStats.Models.SessionListItem>()
        }).ToList();

        // If players are requested, load them all in a single query
        if (includePlayers && rounds.Any())
        {
            var roundIds = rounds.Select(r => r.RoundId).Where(id => !string.IsNullOrEmpty(id)).ToList();

            if (roundIds.Any())
            {
                var playerQuery = _dbContext.PlayerSessions
                    .AsNoTracking()
                    .Where(ps => ps.RoundId != null && roundIds.Contains(ps.RoundId));

                // Restrict the players list to specified names only if requested
                if (onlySpecifiedPlayers && filters.PlayerNames != null && filters.PlayerNames.Any())
                {
                    var names = filters.PlayerNames;
                    playerQuery = playerQuery.Where(ps => names.Contains(ps.PlayerName));
                }

                var allPlayers = await playerQuery
                    .OrderBy(ps => ps.RoundId)
                    .ThenBy(ps => ps.PlayerName)
                    .Select(ps => new PlayerStats.Models.SessionListItem
                    {
                        SessionId = ps.SessionId,
                        RoundId = ps.RoundId!,
                        PlayerName = ps.PlayerName,
                        StartTime = ps.StartTime,
                        EndTime = ps.IsActive ? ps.LastSeenTime : ps.LastSeenTime,
                        DurationMinutes = (int)(ps.LastSeenTime - ps.StartTime).TotalMinutes,
                        Score = ps.TotalScore,
                        Kills = ps.TotalKills,
                        Deaths = ps.TotalDeaths,
                        IsActive = ps.IsActive
                    })
                    .ToListAsync();

                // Group players by RoundId and assign to rounds
                var playersByRound = allPlayers.GroupBy(p => p.RoundId!).ToDictionary(g => g.Key, g => g.ToList());

                foreach (var round in result)
                {
                    if (playersByRound.TryGetValue(round.RoundId, out var players))
                    {
                        round.Players = players;
                    }
                }
            }
        }

        return new PlayerStats.Models.PagedResult<RoundWithPlayers>
        {
            Items = result,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<SessionRoundReport?> GetRoundReport(string roundId, Gamification.Services.ClickHouseGamificationService gamificationService)
    {
        // First, get just the round data we need
        var roundData = await _dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.RoundId == roundId)
            .Select(r => new
            {
                r.RoundId,
                r.MapName,
                r.GameType,
                r.StartTime,
                r.EndTime,
                r.IsActive,
                r.ParticipantCount,
                r.ServerName,
                r.Tickets1,
                r.Tickets2,
                r.Team1Label,
                r.Team2Label,
                SessionIds = r.Sessions.Select(s => s.SessionId).ToList()
            })
            .FirstOrDefaultAsync();

        if (roundData == null)
        {
            // Try to resolve ClickHouse RoundId to SQLite RoundId
            var resolvedRoundId = await ResolveClickHouseRoundIdAsync(roundId);
            if (!string.IsNullOrEmpty(resolvedRoundId) && resolvedRoundId != roundId)
            {
                // Retry with resolved RoundId
                roundData = await _dbContext.Rounds
                    .AsNoTracking()
                    .Where(r => r.RoundId == resolvedRoundId)
                    .Select(r => new
                    {
                        r.RoundId,
                        r.MapName,
                        r.GameType,
                        r.StartTime,
                        r.EndTime,
                        r.IsActive,
                        r.ParticipantCount,
                        r.ServerName,
                        r.Tickets1,
                        r.Tickets2,
                        r.Team1Label,
                        r.Team2Label,
                        SessionIds = r.Sessions.Select(s => s.SessionId).ToList()
                    })
                    .FirstOrDefaultAsync();
            }
        }

        if (roundData == null)
            return null;

        // Get all observations for the round with player names
        var roundObservations = await _dbContext.PlayerObservations
            .Include(o => o.Session)
            .Where(o => roundData.SessionIds.Contains(o.SessionId))
            .OrderBy(o => o.Timestamp)
            .Select(o => new
            {
                o.Timestamp,
                o.Score,
                o.Kills,
                o.Deaths,
                o.Ping,
                o.Team,
                o.TeamLabel,
                PlayerName = o.Session.PlayerName
            })
            .ToListAsync();

        // Create leaderboard snapshots starting from round start
        var leaderboardSnapshots = new List<ServerStats.Models.LeaderboardSnapshot>();
        var currentTime = roundData.StartTime;
        var endTime = roundData.EndTime ?? DateTime.UtcNow;

        while (currentTime <= endTime)
        {
            // Get the latest score for each player at this time
            var playerScores = roundObservations
                .Where(o => o.Timestamp <= currentTime)
                .GroupBy(o => o.PlayerName)
                .Select(g =>
                {
                    var obs = g.OrderByDescending(x => x.Timestamp).First();
                    return new
                    {
                        PlayerName = g.Key,
                        Score = obs.Score,
                        Kills = obs.Kills,
                        Deaths = obs.Deaths,
                        Ping = obs.Ping,
                        Team = obs.Team,
                        TeamLabel = obs.TeamLabel,
                        LastSeen = obs.Timestamp
                    };
                })
                .Where(x => x.LastSeen >= currentTime.AddMinutes(-1)) // Only include players seen in last minute
                .OrderByDescending(x => x.Score)
                .Select((x, i) => new ServerStats.Models.LeaderboardEntry
                {
                    Rank = i + 1,
                    PlayerName = x.PlayerName,
                    Score = x.Score,
                    Kills = x.Kills,
                    Deaths = x.Deaths,
                    Ping = x.Ping,
                    Team = x.Team,
                    TeamLabel = x.TeamLabel
                })
                .ToList();

            leaderboardSnapshots.Add(new ServerStats.Models.LeaderboardSnapshot
            {
                Timestamp = currentTime,
                Entries = playerScores
            });

            currentTime = currentTime.AddMinutes(1);
        }

        // Filter out empty snapshots
        leaderboardSnapshots = leaderboardSnapshots
            .Where(snapshot => snapshot.Entries.Any())
            .ToList();

        // Get achievements for this round using the dedicated method
        List<Gamification.Models.Achievement> achievements = new();
        try
        {
            achievements = await gamificationService.GetRoundAchievementsAsync(roundId);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get achievements for round {RoundId}", roundId);
        }

        return new SessionRoundReport
        {
            Session = new ServerStats.Models.SessionInfo(), // Empty since UI doesn't use it
            Round = new ServerStats.Models.RoundReportInfo
            {
                MapName = roundData.MapName,
                GameType = roundData.GameType,
                ServerName = roundData.ServerName,
                StartTime = roundData.StartTime,
                EndTime = roundData.EndTime ?? DateTime.UtcNow,
                TotalParticipants = roundData.ParticipantCount ?? roundData.SessionIds.Count,
                IsActive = roundData.IsActive,
                Tickets1 = roundData.Tickets1,
                Tickets2 = roundData.Tickets2,
                Team1Label = roundData.Team1Label,
                Team2Label = roundData.Team2Label
            },
            LeaderboardSnapshots = leaderboardSnapshots,
            Achievements = achievements
        };
    }

    /// <summary>
    /// Attempts to resolve a ClickHouse RoundId to a SQLite RoundId by looking up round details from ClickHouse
    /// and finding the corresponding SQLite Round
    /// </summary>
    private async Task<string?> ResolveClickHouseRoundIdAsync(string clickHouseRoundId)
    {
        if (_clickHouseReader == null)
        {
            _logger.LogDebug("ClickHouse reader not available for RoundId resolution: {RoundId}", clickHouseRoundId);
            return null;
        }

        try
        {
            // Query ClickHouse for round details
            var query = $@"
SELECT 
    server_guid,
    map_name,
    round_start_time,
    round_end_time
FROM player_rounds
WHERE round_id = '{clickHouseRoundId.Replace("'", "''")}'
LIMIT 1
FORMAT TabSeparated";

            var result = await _clickHouseReader.ExecuteQueryAsync(query);
            if (string.IsNullOrWhiteSpace(result) || result.Trim().Length == 0)
            {
                _logger.LogDebug("No data found in ClickHouse for RoundId: {RoundId}", clickHouseRoundId);
                return null;
            }

            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length == 0)
            {
                return null;
            }

            var parts = lines[0].Split('\t');
            if (parts.Length < 4)
            {
                _logger.LogWarning("Invalid data format from ClickHouse for RoundId: {RoundId}", clickHouseRoundId);
                return null;
            }

            var serverGuid = parts[0];
            var mapName = parts[1];
            var roundStartTime = DateTime.TryParse(parts[2], out var startTime) ? startTime : DateTime.MinValue;
            var roundEndTime = DateTime.TryParse(parts[3], out var endTime) ? endTime : DateTime.MinValue;

            if (startTime == DateTime.MinValue)
            {
                _logger.LogWarning("Invalid start time from ClickHouse for RoundId: {RoundId}", clickHouseRoundId);
                return null;
            }

            // Find the corresponding SQLite Round by server, map, and time range
            // Allow some tolerance for timing differences (±10 minutes)
            var timeTolerance = TimeSpan.FromMinutes(10);
            var searchStartTime = startTime - timeTolerance;
            var searchEndTime = startTime + timeTolerance;

            var sqliteRound = (await _dbContext.Rounds
                .AsNoTracking()
                .Where(r => r.ServerGuid == serverGuid
                           && r.MapName == mapName
                           && r.StartTime >= searchStartTime
                           && r.StartTime <= searchEndTime)
                .ToListAsync()) // Load data from database first
                .OrderBy(r => Math.Abs((r.StartTime - startTime).Ticks)) // Then sort in memory
                .FirstOrDefault();

            if (sqliteRound != null)
            {
                _logger.LogDebug("Resolved ClickHouse RoundId {ClickHouseRoundId} to SQLite RoundId {SQLiteRoundId}",
                    clickHouseRoundId, sqliteRound.RoundId);
                return sqliteRound.RoundId;
            }

            _logger.LogDebug("Could not resolve ClickHouse RoundId {ClickHouseRoundId} to SQLite RoundId", clickHouseRoundId);
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error resolving ClickHouse RoundId {ClickHouseRoundId} to SQLite RoundId", clickHouseRoundId);
            return null;
        }
    }
}


