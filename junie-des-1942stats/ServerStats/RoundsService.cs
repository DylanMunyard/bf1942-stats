using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ServerStats.Models;
using junie_des_1942stats.ClickHouse;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ServerStats;

public class RoundsService(PlayerTrackerDbContext dbContext, ILogger<RoundsService> logger, PlayerRoundsReadService? clickHouseReader = null)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly ILogger<RoundsService> _logger = logger;
    private readonly PlayerRoundsReadService? _clickHouseReader = clickHouseReader;

    public async Task<List<RoundInfo>> GetRecentRoundsAsync(string serverGuid, int limit)
    {
        var rounds = await _dbContext.Rounds
            .AsNoTracking()
            .Where(r => r.ServerGuid == serverGuid)
            .OrderByDescending(r => r.StartTime)
            .Take(limit)
            .Select(r => new RoundInfo
            {
                RoundId = r.RoundId,
                MapName = r.MapName,
                StartTime = r.StartTime,
                EndTime = r.EndTime ?? DateTime.UtcNow,
                IsActive = r.IsActive
            })
            .ToListAsync();

        return rounds;
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

        // Tournament filters
        if (!string.IsNullOrWhiteSpace(filters.TournamentId))
        {
            query = query.Where(r => r.TournamentId == filters.TournamentId);
        }

        if (filters.IsTournamentRound.HasValue)
        {
            query = query.Where(r => r.IsTournamentRound == filters.IsTournamentRound.Value);
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

        // Get tournament data for rounds that are part of tournaments
        var tournamentRoundIds = rounds.Where(r => !string.IsNullOrEmpty(r.TournamentId)).Select(r => r.RoundId).ToList();
        var tournamentData = new Dictionary<string, (string Name, int RoundNumber)>();
        
        if (tournamentRoundIds.Any())
        {
            var tournamentRounds = await _dbContext.TournamentRounds
                .Include(tr => tr.Tournament)
                .Where(tr => tournamentRoundIds.Contains(tr.RoundId))
                .ToListAsync();
                
            tournamentData = tournamentRounds.ToDictionary(
                tr => tr.RoundId,
                tr => (tr.Tournament.Name ?? "", tr.RoundNumber)
            );
        }

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
            TournamentId = round.TournamentId,
            IsTournamentRound = round.IsTournamentRound,
            TournamentName = tournamentData.TryGetValue(round.RoundId, out var tData) ? tData.Name : null,
            TournamentRoundNumber = tournamentData.TryGetValue(round.RoundId, out var tData2) ? tData2.RoundNumber : null,
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

    public async Task<PlayerStats.Models.PagedResult<TournamentWithRounds>> GetTournaments(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        TournamentFilters filters,
        bool includeRounds = true)
    {
        var query = _dbContext.Tournaments.AsNoTracking();

        // Apply filters
        if (!string.IsNullOrWhiteSpace(filters.ServerName))
        {
            query = query.Where(t => t.ServerName.Contains(filters.ServerName));
        }

        if (!string.IsNullOrWhiteSpace(filters.ServerGuid))
        {
            query = query.Where(t => t.ServerGuid == filters.ServerGuid);
        }

        if (!string.IsNullOrWhiteSpace(filters.MapName))
        {
            query = query.Where(t => t.MapName.Contains(filters.MapName));
        }

        if (!string.IsNullOrWhiteSpace(filters.GameType))
        {
            query = query.Where(t => t.GameType == filters.GameType);
        }

        if (!string.IsNullOrWhiteSpace(filters.TournamentType))
        {
            query = query.Where(t => t.TournamentType == filters.TournamentType);
        }

        if (filters.StartTimeFrom.HasValue)
        {
            query = query.Where(t => t.StartTime >= filters.StartTimeFrom.Value);
        }

        if (filters.StartTimeTo.HasValue)
        {
            query = query.Where(t => t.StartTime <= filters.StartTimeTo.Value);
        }

        if (filters.EndTimeFrom.HasValue)
        {
            query = query.Where(t => t.EndTime >= filters.EndTimeFrom.Value);
        }

        if (filters.EndTimeTo.HasValue)
        {
            query = query.Where(t => t.EndTime <= filters.EndTimeTo.Value);
        }

        if (filters.IsActive.HasValue)
        {
            query = query.Where(t => t.IsActive == filters.IsActive.Value);
        }

        if (filters.MinRounds.HasValue)
        {
            query = query.Where(t => t.TotalRounds >= filters.MinRounds.Value);
        }

        if (filters.MaxRounds.HasValue)
        {
            query = query.Where(t => t.TotalRounds <= filters.MaxRounds.Value);
        }

        // Apply sorting
        query = sortBy.ToLowerInvariant() switch
        {
            "tournamentid" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.TournamentId)
                : query.OrderByDescending(t => t.TournamentId),
            "servername" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.ServerName)
                : query.OrderByDescending(t => t.ServerName),
            "mapname" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.MapName)
                : query.OrderByDescending(t => t.MapName),
            "endtime" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.EndTime)
                : query.OrderByDescending(t => t.EndTime),
            "totalrounds" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.TotalRounds)
                : query.OrderByDescending(t => t.TotalRounds),
            "isactive" => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.IsActive)
                : query.OrderByDescending(t => t.IsActive),
            _ => sortOrder.ToLowerInvariant() == "asc"
                ? query.OrderBy(t => t.StartTime)
                : query.OrderByDescending(t => t.StartTime)
        };

        // Get total count
        var totalCount = await query.CountAsync();

        // Apply pagination and get tournaments
        var tournaments = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        // Convert to TournamentWithRounds
        var result = tournaments.Select(tournament => new TournamentWithRounds
        {
            TournamentId = tournament.TournamentId,
            ServerName = tournament.ServerName,
            ServerGuid = tournament.ServerGuid,
            MapName = tournament.MapName,
            GameType = tournament.GameType,
            StartTime = tournament.StartTime,
            EndTime = tournament.EndTime,
            IsActive = tournament.IsActive,
            TotalRounds = tournament.TotalRounds,
            ParticipantCount = tournament.ParticipantCount,
            TournamentType = tournament.TournamentType,
            Name = tournament.Name,
            Description = tournament.Description,
            Rounds = new List<RoundListItem>()
        }).ToList();

        // If rounds are requested, load them
        if (includeRounds && tournaments.Any())
        {
            var tournamentIds = tournaments.Select(t => t.TournamentId).ToList();

            var tournamentRounds = await _dbContext.TournamentRounds
                .Include(tr => tr.Round)
                .Where(tr => tournamentIds.Contains(tr.TournamentId))
                .OrderBy(tr => tr.TournamentId)
                .ThenBy(tr => tr.RoundNumber)
                .ToListAsync();

            var roundsByTournament = tournamentRounds
                .GroupBy(tr => tr.TournamentId)
                .ToDictionary(g => g.Key, g => g.ToList());

            foreach (var tournament in result)
            {
                if (roundsByTournament.TryGetValue(tournament.TournamentId, out var rounds))
                {
                    tournament.Rounds = rounds.Select(tr => new RoundListItem
                    {
                        RoundId = tr.Round.RoundId,
                        ServerName = tr.Round.ServerName,
                        ServerGuid = tr.Round.ServerGuid,
                        MapName = tr.Round.MapName,
                        GameType = tr.Round.GameType,
                        StartTime = tr.Round.StartTime,
                        EndTime = tr.Round.EndTime ?? DateTime.UtcNow,
                        DurationMinutes = tr.Round.DurationMinutes ?? 0,
                        ParticipantCount = tr.Round.ParticipantCount ?? 0,
                        IsActive = tr.Round.IsActive,
                        Team1Label = tr.Round.Team1Label,
                        Team2Label = tr.Round.Team2Label,
                        RoundTimeRemain = tr.Round.RoundTimeRemain,
                        TournamentId = tr.Round.TournamentId,
                        IsTournamentRound = tr.Round.IsTournamentRound,
                        TournamentName = tournament.Name,
                        TournamentRoundNumber = tr.RoundNumber
                    }).ToList();
                }
            }
        }

        return new PlayerStats.Models.PagedResult<TournamentWithRounds>
        {
            Items = result,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling((double)totalCount / pageSize)
        };
    }

    public async Task<TournamentWithRounds?> GetTournament(string tournamentId, bool includeRounds = true)
    {
        var tournament = await _dbContext.Tournaments
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.TournamentId == tournamentId);

        if (tournament == null) return null;

        var result = new TournamentWithRounds
        {
            TournamentId = tournament.TournamentId,
            ServerName = tournament.ServerName,
            ServerGuid = tournament.ServerGuid,
            MapName = tournament.MapName,
            GameType = tournament.GameType,
            StartTime = tournament.StartTime,
            EndTime = tournament.EndTime,
            IsActive = tournament.IsActive,
            TotalRounds = tournament.TotalRounds,
            ParticipantCount = tournament.ParticipantCount,
            TournamentType = tournament.TournamentType,
            Name = tournament.Name,
            Description = tournament.Description,
            Rounds = new List<RoundListItem>()
        };

        if (includeRounds)
        {
            var tournamentRounds = await _dbContext.TournamentRounds
                .Include(tr => tr.Round)
                .Where(tr => tr.TournamentId == tournamentId)
                .OrderBy(tr => tr.RoundNumber)
                .ToListAsync();

            result.Rounds = tournamentRounds.Select(tr => new RoundListItem
            {
                RoundId = tr.Round.RoundId,
                ServerName = tr.Round.ServerName,
                ServerGuid = tr.Round.ServerGuid,
                MapName = tr.Round.MapName,
                GameType = tr.Round.GameType,
                StartTime = tr.Round.StartTime,
                EndTime = tr.Round.EndTime ?? DateTime.UtcNow,
                DurationMinutes = tr.Round.DurationMinutes ?? 0,
                ParticipantCount = tr.Round.ParticipantCount ?? 0,
                IsActive = tr.Round.IsActive,
                Team1Label = tr.Round.Team1Label,
                Team2Label = tr.Round.Team2Label,
                RoundTimeRemain = tr.Round.RoundTimeRemain,
                TournamentId = tr.Round.TournamentId,
                IsTournamentRound = tr.Round.IsTournamentRound,
                TournamentName = tournament.Name,
                TournamentRoundNumber = tr.RoundNumber
            }).ToList();
        }

        return result;
    }
}


