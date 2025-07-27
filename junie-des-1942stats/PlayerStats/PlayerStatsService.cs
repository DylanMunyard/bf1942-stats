using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ClickHouse;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.PlayerStats;

public class PlayerStatsService(PlayerTrackerDbContext dbContext, PlayerInsightsService playerInsightsService, PlayerRoundsReadService playerRoundsReadService)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly PlayerInsightsService _playerInsightsService = playerInsightsService;
    private readonly PlayerRoundsReadService _playerRoundsReadService = playerRoundsReadService;

    // Define a threshold for considering a player "active" (e.g., 5 minutes)
    private readonly TimeSpan _activeThreshold = TimeSpan.FromMinutes(5);

    public async Task<PagedResult<PlayerBasicInfo>> GetAllPlayersWithPaging(
        int page, 
        int pageSize, 
        string sortBy, 
        string sortOrder,
        PlayerFilters? filters = null)
    {
        var baseQuery = _dbContext.Players.Where(p => !p.AiBot);

        // Apply filters at the database level first
        if (filters != null)
        {
            if (!string.IsNullOrEmpty(filters.PlayerName))
            {
                baseQuery = baseQuery.Where(p => EF.Functions.Like(p.Name, $"%{filters.PlayerName}%"));
            }

            if (filters.MinPlayTime.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.TotalPlayTimeMinutes >= filters.MinPlayTime.Value);
            }

            if (filters.MaxPlayTime.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.TotalPlayTimeMinutes <= filters.MaxPlayTime.Value);
            }

            if (filters.LastSeenFrom.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.LastSeen >= filters.LastSeenFrom.Value);
            }

            if (filters.LastSeenTo.HasValue)
            {
                baseQuery = baseQuery.Where(p => p.LastSeen <= filters.LastSeenTo.Value);
            }

            if (filters.IsActive.HasValue)
            {
                if (filters.IsActive.Value)
                {
                    // Only players with active sessions
                    baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive));
                }
                else
                {
                    // Only players without active sessions
                    baseQuery = baseQuery.Where(p => !p.Sessions.Any(s => s.IsActive));
                }
            }

            // Server-related filters - filter by players who have active sessions matching criteria
            if (!string.IsNullOrEmpty(filters.ServerName))
            {
                baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive && 
                    s.Server.Name.Contains(filters.ServerName)));
            }

            if (!string.IsNullOrEmpty(filters.GameId))
            {
                baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive && 
                    s.Server.GameId == filters.GameId));
            }

            if (!string.IsNullOrEmpty(filters.MapName))
            {
                baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive && 
                    s.MapName.Contains(filters.MapName)));
            }
        }

        // Now project to PlayerBasicInfo
        var query = baseQuery.Select(p => new PlayerBasicInfo
        {
            PlayerName = p.Name,
            TotalPlayTimeMinutes = p.TotalPlayTimeMinutes,
            LastSeen = p.LastSeen,
            IsActive = p.Sessions.Any(s => s.IsActive),
            CurrentServer = p.Sessions.Any(s => s.IsActive)
                ? p.Sessions.Where(s => s.IsActive)
                    .Select(s => new ServerInfo
                    {
                        ServerGuid = s.ServerGuid,
                        ServerName = s.Server.Name,
                        SessionKills = s.TotalKills,
                        SessionDeaths = s.TotalDeaths,
                        MapName = s.MapName,
                        GameId = s.Server.GameId,
                    })
                    .FirstOrDefault()
                : null
        });

        // Apply sorting
        var isDescending = sortOrder.ToLower() == "desc";
        
        query = sortBy.ToLower() switch
        {
            "playername" => isDescending 
                ? query.OrderByDescending(p => p.PlayerName)
                : query.OrderBy(p => p.PlayerName),
            "totalplaytimeminutes" => isDescending 
                ? query.OrderByDescending(p => p.TotalPlayTimeMinutes)
                : query.OrderBy(p => p.TotalPlayTimeMinutes),
            "lastseen" => isDescending 
                ? query.OrderByDescending(p => p.LastSeen)
                : query.OrderBy(p => p.LastSeen),
            "isactive" => isDescending 
                ? query.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.LastSeen)
                : query.OrderBy(p => p.IsActive).ThenByDescending(p => p.LastSeen),
            _ => query.OrderByDescending(p => p.IsActive).ThenByDescending(p => p.LastSeen)
        };

        // Get total count for pagination (after filters are applied)
        var totalCount = await query.CountAsync();

        // Apply pagination
        var players = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync();

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PagedResult<PlayerBasicInfo>
        {
            Items = players,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = totalPages
        };
    }

    public async Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName, bool showAllServers = false)
    {
        // First check if the player exists
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
            return new PlayerTimeStatistics();

        var now = DateTime.UtcNow;

        // Get aggregated stats from ClickHouse for better performance and accuracy
        var clickHouseStats = await GetPlayerStatsFromClickHouse(playerName);
        
        // Get session-based stats for fields not available in ClickHouse
        var sessionStats = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                FirstPlayed = g.Min(s => s.StartTime),
                LastPlayed = g.Max(s => s.LastSeenTime)
            })
            .FirstOrDefaultAsync();

        var aggregateStats = new
        {
            FirstPlayed = sessionStats?.FirstPlayed ?? DateTime.MinValue,
            LastPlayed = sessionStats?.LastPlayed ?? DateTime.MinValue,
            TotalKills = clickHouseStats?.TotalKills ?? 0,
            TotalDeaths = clickHouseStats?.TotalDeaths ?? 0,
            TotalPlayTimeMinutes = clickHouseStats?.TotalPlayTimeMinutes ?? 0
        };

        if (aggregateStats == null)
            return new PlayerTimeStatistics();

        // Get the most recent 10 sessions with server info
        var recentSessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .OrderByDescending(s => s.LastSeenTime)
            .Include(s => s.Server)
            .Take(10)
            .Select(s => new Session
            {
                SessionId = s.SessionId,
                ServerName = s.Server.Name,
                ServerGuid = s.ServerGuid,
                MapName = s.MapName,
                GameType = s.GameType,
                StartTime = s.StartTime,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                TotalScore = s.TotalScore,
                IsActive = s.IsActive
            })
            .ToListAsync();


        // Get the current active session if any
        var activeSession = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.IsActive)
            .Include(s => s.Server)
            .OrderByDescending(s => s.LastSeenTime)
            .FirstOrDefaultAsync();

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null &&
                        (now - activeSession.LastSeenTime) <= _activeThreshold;

        var insights = await GetPlayerInsights(playerName);
        
        List<KillMilestone> killMilestones;
        try
        {
            killMilestones = await _playerInsightsService.GetPlayerKillMilestonesAsync(playerName);
        }
        catch (Exception)
        {
            killMilestones = new List<KillMilestone>();
        }
        
        List<ServerInsight> serverInsights;
        try
        {
            serverInsights = await _playerInsightsService.GetPlayerServerInsightsAsync(playerName);
        }
        catch (Exception)
        {
            serverInsights = new List<ServerInsight>();
        }

        // Get server names for the insights
        foreach (var serverInsight in serverInsights)
        {
            var server = await _dbContext.Servers
                .FirstOrDefaultAsync(s => s.Guid == serverInsight.ServerGuid);
            if (server != null)
            {
                serverInsight.ServerName = server.Name;
                serverInsight.GameId = server.GameId;
            }
        }

        var stats = new PlayerTimeStatistics
        {
            TotalPlayTimeMinutes = aggregateStats.TotalPlayTimeMinutes,
            FirstPlayed = aggregateStats.FirstPlayed,
            LastPlayed = aggregateStats.LastPlayed,
            TotalKills = aggregateStats.TotalKills,
            TotalDeaths = aggregateStats.TotalDeaths,

            IsActive = isActive,
            CurrentServer = isActive && activeSession != null
                ? new ServerInfo
                {
                    ServerGuid = activeSession.ServerGuid,
                    ServerName = activeSession.Server.Name,
                    SessionKills = activeSession.TotalKills,
                    SessionDeaths = activeSession.TotalDeaths,
                    GameId = activeSession.Server.GameId,
                    MapName = activeSession.MapName,
                }
                : null,
            RecentSessions = recentSessions,
            Insights = insights,
            KillMilestones = killMilestones,
            Servers = serverInsights
        };

        return stats;
    }

    public async Task<PagedResult<SessionListItem>> GetPlayerSessions(
        string playerName,
        int page = 1,
        int pageSize = 100,
        string sortBy = "StartTime",
        string sortOrder = "desc",
        PlayerFilters? filters = null)
    {
        // Get player information
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
        {
            return new PagedResult<SessionListItem>
            {
                Items = new List<SessionListItem>(),
                Page = page,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0
            };
        }

        // Get active session if any
        var activeSession = await _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName && s.IsActive)
            .Include(s => s.Server)
            .FirstOrDefaultAsync();

        // Build base query for sessions
        var baseSessionQuery = _dbContext.PlayerSessions
            .Where(s => s.PlayerName == playerName);

        // Apply filters if provided
        if (filters != null)
        {
            if (filters.LastSeenFrom.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.LastSeenTime >= filters.LastSeenFrom.Value);
            }

            if (filters.LastSeenTo.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.LastSeenTime <= filters.LastSeenTo.Value);
            }

            if (filters.StartTimeFrom.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.StartTime >= filters.StartTimeFrom.Value);
            }

            if (filters.StartTimeTo.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.StartTime <= filters.StartTimeTo.Value);
            }

            if (filters.IsActive.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.IsActive == filters.IsActive.Value);
            }

            // Server-related filters
            if (!string.IsNullOrEmpty(filters.ServerName))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.Server.Name.Contains(filters.ServerName));
            }

            if (!string.IsNullOrEmpty(filters.ServerGuid))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.ServerGuid == filters.ServerGuid);
            }

            if (!string.IsNullOrEmpty(filters.GameId))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.Server.GameId == filters.GameId);
            }

            // Map and game type filters
            if (!string.IsNullOrEmpty(filters.MapName))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.MapName.Contains(filters.MapName));
            }

            if (!string.IsNullOrEmpty(filters.GameType))
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.GameType != null && s.GameType.Contains(filters.GameType));
            }

            // Duration filters
            if (filters.MinPlayTime.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => 
                    (s.LastSeenTime - s.StartTime).TotalMinutes >= filters.MinPlayTime.Value);
            }

            if (filters.MaxPlayTime.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => 
                    (s.LastSeenTime - s.StartTime).TotalMinutes <= filters.MaxPlayTime.Value);
            }

            // Score filters
            if (filters.MinScore.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalScore >= filters.MinScore.Value);
            }

            if (filters.MaxScore.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalScore <= filters.MaxScore.Value);
            }

            // Kills filters
            if (filters.MinKills.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalKills >= filters.MinKills.Value);
            }

            if (filters.MaxKills.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalKills <= filters.MaxKills.Value);
            }

            // Deaths filters
            if (filters.MinDeaths.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalDeaths >= filters.MinDeaths.Value);
            }

            if (filters.MaxDeaths.HasValue)
            {
                baseSessionQuery = baseSessionQuery.Where(s => s.TotalDeaths <= filters.MaxDeaths.Value);
            }
        }

        // Count total sessions for this player (for pagination metadata, after filters applied)
        var totalCount = await baseSessionQuery.CountAsync();

        // Get aggregate player stats (from filtered sessions)
        var aggregateStats = await baseSessionQuery
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                FirstPlayed = g.Min(s => s.StartTime),
                TotalKills = g.Sum(s => s.TotalKills),
                TotalDeaths = g.Sum(s => s.TotalDeaths)
            })
            .FirstOrDefaultAsync();

        // Apply sorting
        var isDescending = sortOrder.ToLower() == "desc";
        
        var sortedQuery = sortBy.ToLower() switch
        {
            "sessionid" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.SessionId)
                : baseSessionQuery.OrderBy(s => s.SessionId),
            "servername" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.Server.Name)
                : baseSessionQuery.OrderBy(s => s.Server.Name),
            "mapname" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.MapName)
                : baseSessionQuery.OrderBy(s => s.MapName),
            "gametype" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.GameType)
                : baseSessionQuery.OrderBy(s => s.GameType),
            "starttime" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.StartTime)
                : baseSessionQuery.OrderBy(s => s.StartTime),
            "endtime" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.LastSeenTime)
                : baseSessionQuery.OrderBy(s => s.LastSeenTime),
            "durationminutes" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => (s.LastSeenTime - s.StartTime).TotalMinutes)
                : baseSessionQuery.OrderBy(s => (s.LastSeenTime - s.StartTime).TotalMinutes),
            "score" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.TotalScore)
                : baseSessionQuery.OrderBy(s => s.TotalScore),
            "kills" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.TotalKills)
                : baseSessionQuery.OrderBy(s => s.TotalKills),
            "deaths" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.TotalDeaths)
                : baseSessionQuery.OrderBy(s => s.TotalDeaths),
            "isactive" => isDescending 
                ? baseSessionQuery.OrderByDescending(s => s.IsActive).ThenByDescending(s => s.StartTime)
                : baseSessionQuery.OrderBy(s => s.IsActive).ThenByDescending(s => s.StartTime),
            _ => baseSessionQuery.OrderByDescending(s => s.StartTime) // Default sorting
        };

        // Get the specified page of sessions (with filters and sorting applied)
        var sessions = await sortedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .Select(s => new SessionListItem
            {
                SessionId = s.SessionId,
                ServerName = s.Server.Name,
                ServerGuid = s.ServerGuid,
                MapName = s.MapName,
                GameType = s.GameType,
                StartTime = s.StartTime,
                EndTime = s.LastSeenTime,
                DurationMinutes = (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes),
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                IsActive = s.IsActive
            })
            .ToListAsync();

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null &&
                   (DateTime.UtcNow - activeSession.LastSeenTime) <= _activeThreshold;

        // Return paged result with metadata and player context
        return new PagedResult<SessionListItem>
        {
            Items = sessions,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = (int)Math.Ceiling(totalCount / (double)pageSize),
            PlayerInfo = new PlayerContextInfo
            {
                Name = player.Name,
                TotalPlayTimeMinutes = player.TotalPlayTimeMinutes,
                FirstSeen = aggregateStats?.FirstPlayed ?? player.FirstSeen,
                LastSeen = player.LastSeen,
                IsActive = isActive,
                TotalKills = aggregateStats?.TotalKills ?? 0,
                TotalDeaths = aggregateStats?.TotalDeaths ?? 0,
                CurrentServer = isActive && activeSession != null
                ? new ServerInfo
                {
                    ServerGuid = activeSession.ServerGuid,
                    ServerName = activeSession.Server.Name,
                    SessionKills = activeSession.TotalKills,
                    SessionDeaths = activeSession.TotalDeaths,
                    MapName = activeSession.MapName,
                    GameId = activeSession.Server.GameId
                }
                : null
            }
        };
    }

    public async Task<SessionDetail?> GetSession(string playerName, int sessionId)
    {
        var session = await _dbContext.PlayerSessions
            .Where(s => s.SessionId == sessionId && s.PlayerName == playerName)
            .Include(s => s.Player)
            .Include(s => s.Server)
            .Include(s => s.Observations)
            .FirstOrDefaultAsync();

        if (session == null)
        {
            return null;
        }

        var sessionDetail = new SessionDetail
        {
            SessionId = session.SessionId,
            PlayerName = session.PlayerName,
            ServerName = session.Server.Name,
            MapName = session.MapName,
            GameType = session.GameType,
            StartTime = session.StartTime,
            EndTime = session.IsActive ? null : session.LastSeenTime,
            TotalPlayTimeMinutes = (int)Math.Ceiling((session.LastSeenTime - session.StartTime).TotalMinutes),
            TotalKills = session.TotalKills,
            TotalDeaths = session.TotalDeaths,
            TotalScore = session.TotalScore,
            IsActive = session.IsActive,

            // Player details
            PlayerDetails = new PlayerDetailInfo
            {
                Name = session.Player.Name,
                TotalPlayTimeMinutes = session.Player.TotalPlayTimeMinutes,
                FirstSeen = session.Player.FirstSeen,
                LastSeen = session.Player.LastSeen,
                IsAiBot = session.Player.AiBot
            },

            // Server details
            ServerDetails = new ServerDetailInfo
            {
                Guid = session.Server.Guid,
                Name = session.Server.Name,
                Address = session.Server.Ip,
                Port = session.Server.Port,
                GameId = session.Server.GameId
            },

            // Observations over time
            Observations = session.Observations.Select(o => new ObservationInfo
            {
                Timestamp = o.Timestamp,
                Score = o.Score,
                Kills = o.Kills,
                Deaths = o.Deaths,
                Ping = o.Ping,
                Team = o.Team,
                TeamLabel = o.TeamLabel
            }).ToList(),

        };

        return sessionDetail;
    }

    public async Task<PlayerInsights> GetPlayerInsights(
        string playerName,
        DateTime? startDate = null,
        DateTime? endDate = null,
        int? daysToAnalyze = null)
    {
        // Calculate the time period
        var endPeriod = endDate ?? DateTime.UtcNow;
        DateTime startPeriod;

        if (startDate.HasValue)
        {
            startPeriod = startDate.Value;
        }
        else if (daysToAnalyze.HasValue)
        {
            startPeriod = endPeriod.AddDays(-daysToAnalyze.Value);
        }
        else
        {
            // Default to 1 week
            startPeriod = endPeriod.AddDays(-7);
        }

        // Check if the player exists
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
            return new PlayerInsights { PlayerName = playerName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Get player sessions within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.StartTime >= startPeriod && ps.LastSeenTime <= endPeriod)
            .Include(s => s.Server)
            .ToListAsync();

        var insights = new PlayerInsights
        {
            PlayerName = playerName,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // 1. Get server rankings with total players per server (aggregated across all months)
        // First get all servers where the player has rankings with their total scores
        var playerServerStats = await _dbContext.ServerPlayerRankings
            .Where(r => r.PlayerName == playerName)
            .GroupBy(r => new { r.ServerGuid, r.Server.Name })
            .Select(g => new 
            {
                g.Key.ServerGuid,
                g.Key.Name,
                TotalScore = g.Sum(x => x.TotalScore)
            })
            .ToListAsync();

        // Process each server in parallel for better performance
        var serverRankingTasks = playerServerStats.Select(async serverStat =>
        {
            // Get the player's total score for this server
            var playerScore = serverStat.TotalScore;
            
            // Count how many players have a higher score on this server
            var higherScoringPlayers = await _dbContext.ServerPlayerRankings
                .Where(r => r.ServerGuid == serverStat.ServerGuid)
                .GroupBy(r => r.PlayerName)
                .Select(g => new { TotalScore = g.Sum(x => x.TotalScore) })
                .CountAsync(s => s.TotalScore > playerScore);
                
            // Get total number of ranked players on this server
            var totalPlayers = await _dbContext.ServerPlayerRankings
                .Where(r => r.ServerGuid == serverStat.ServerGuid)
                .Select(r => r.PlayerName)
                .Distinct()
                .CountAsync();
                
            // Calculate average ping from the most recent observations
            var averagePing = await _dbContext.PlayerObservations
                .Include(o => o.Session)
                .ThenInclude(s => s.Player)
                .Where(o => o.Session.Player.Name == playerName && o.Session.ServerGuid == serverStat.ServerGuid)
                .OrderByDescending(o => o.Timestamp)
                .Take(50) // Sample size of 50 observations
                .AverageAsync(o => o.Ping);
                
            // The player's rank is the number of players with higher scores + 1
            var playerRank = higherScoringPlayers + 1;
            
            return new ServerRanking
            {
                ServerGuid = serverStat.ServerGuid,
                ServerName = serverStat.Name,
                Rank = playerRank,
                TotalScore = serverStat.TotalScore,
                TotalRankedPlayers = totalPlayers,
                AveragePing = Math.Round(averagePing)
            };
        });
        
        // Wait for all server rankings to be processed
        var serverRankings = (await Task.WhenAll(serverRankingTasks))
            .OrderBy(r => r.Rank) // Order by rank (best rank first)
            .ToList();


        insights.ServerRankings = serverRankings
            .OrderBy(r => r.Rank) // Order by rank (best rank first)
            .ToList();

        // 3. Calculate activity by hour (when they're usually online)
        // Initialize hourly activity tracker
        var hourlyActivity = new Dictionary<int, int>();
        for (int hour = 0; hour < 24; hour++)
        {
            hourlyActivity[hour] = 0;
        }

        // Process each session's time range and break into hourly chunks
        foreach (var session in sessions)
        {
            var sessionStart = session.StartTime;
            var sessionEnd = session.LastSeenTime;

            // Track activity by processing continuous blocks of time
            var currentTime = sessionStart;

            while (currentTime < sessionEnd)
            {
                int hour = currentTime.Hour;

                // Calculate how much time was spent in this hour
                // Either go to the end of the current hour or the end of the session, whichever comes first
                var hourEnd = new DateTime(
                    currentTime.Year,
                    currentTime.Month,
                    currentTime.Day,
                    hour,
                    59,
                    59,
                    999);

                if (hourEnd > sessionEnd)
                {
                    hourEnd = sessionEnd;
                }

                // Add the minutes spent in this hour
                int minutesInHour = (int)Math.Ceiling((hourEnd - currentTime).TotalMinutes);
                hourlyActivity[hour] += minutesInHour;

                // Move to the next hour
                currentTime = hourEnd.AddMilliseconds(1);
            }
        }

        insights.ActivityByHour = hourlyActivity
            .Select(kvp => new HourlyActivity { Hour = kvp.Key, MinutesActive = kvp.Value })
            .OrderByDescending(ha => ha.MinutesActive)
            .ToList();

        return insights;
    }

    private async Task<PlayerClickHouseStats?> GetPlayerStatsFromClickHouse(string playerName)
    {
        try
        {
            var result = await _playerRoundsReadService.GetPlayerStatsAsync(playerName);
            
            // Parse the tab-separated results
            // Expected format: player_name	total_rounds	total_kills	total_deaths	total_play_time_minutes	avg_score_per_round	kd_ratio
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            
            // Skip header row if it exists
            var dataLines = lines.Where(line => !line.StartsWith("player_name")).ToArray();
            
            if (dataLines.Length == 0)
                return null;
                
            var parts = dataLines[0].Split('\t');
            if (parts.Length >= 5)
            {
                return new PlayerClickHouseStats
                {
                    PlayerName = parts[0],
                    TotalRounds = int.Parse(parts[1]),
                    TotalKills = int.Parse(parts[2]),
                    TotalDeaths = int.Parse(parts[3]),
                    TotalPlayTimeMinutes = (int)Math.Round(double.Parse(parts[4]))
                };
            }
        }
        catch (Exception ex)
        {
            // Log error but don't fail the request
            // _logger?.LogError(ex, "Failed to get player stats from ClickHouse for player: {PlayerName}", playerName);
        }
        
        return null;
    }

    /// <summary>
    /// Get currently online players with their session information
    /// </summary>
    public async Task<OnlinePlayersResponse> GetOnlinePlayersAsync(
        string? gameId = null,
        string? playerNameFilter = null,
        string? serverNameFilter = null)
    {
        var now = DateTime.UtcNow;
        var activeThreshold = TimeSpan.FromMinutes(5); // Consider players active if seen within last 5 minutes
        var thresholdTime = now.Subtract(activeThreshold); // Calculate the cutoff time

        // Build the base query for active sessions with all filters
        var baseQuery = _dbContext.PlayerSessions
            .Where(s => s.IsActive && s.LastSeenTime >= thresholdTime);

        // Apply filters
        if (!string.IsNullOrEmpty(gameId))
        {
            baseQuery = baseQuery.Where(s => s.Server.GameId == gameId);
        }

        if (!string.IsNullOrEmpty(playerNameFilter))
        {
            baseQuery = baseQuery.Where(s => s.PlayerName.ToLower().Contains(playerNameFilter.ToLower()));
        }

        if (!string.IsNullOrEmpty(serverNameFilter))
        {
            baseQuery = baseQuery.Where(s => s.Server.Name.ToLower().Contains(serverNameFilter.ToLower()));
        }

        // Apply includes after all filters (exclude observations for now)
        var activeSessions = await baseQuery
            .Include(s => s.Server)
            .Include(s => s.Player)
            .ToListAsync();

        if (!activeSessions.Any())
        {
            return new OnlinePlayersResponse
            {
                Players = new List<OnlinePlayer>(),
                TotalOnline = 0,
                LastUpdated = now,
                GameBreakdown = new OnlineGameBreakdown()
            };
        }

        // Get latest observations for all active sessions in a single efficient query
        var sessionIds = activeSessions.Select(s => s.SessionId).ToList();
        var latestObservations = await _dbContext.PlayerObservations
            .Where(o => sessionIds.Contains(o.SessionId))
            .GroupBy(o => o.SessionId)
            .Select(g => g.OrderByDescending(o => o.Timestamp).First())
            .ToDictionaryAsync(o => o.SessionId, o => o);

        var onlinePlayers = new List<OnlinePlayer>();

        foreach (var session in activeSessions)
        {
            // Get the latest observation for this session from our dictionary
            latestObservations.TryGetValue(session.SessionId, out PlayerObservation? latestObservation);

            var sessionDurationMinutes = (int)Math.Ceiling((now - session.StartTime).TotalMinutes);

            var onlinePlayer = new OnlinePlayer
            {
                PlayerName = session.PlayerName,
                SessionDurationMinutes = sessionDurationMinutes,
                JoinedAt = session.StartTime,
                CurrentServer = new OnlinePlayerServer
                {
                    ServerGuid = session.ServerGuid,
                    ServerName = session.Server.Name,
                    GameId = session.Server.GameId,
                    MapName = session.MapName,
                    SessionKills = session.TotalKills,
                    SessionDeaths = session.TotalDeaths,
                    CurrentScore = session.TotalScore,
                    Ping = latestObservation?.Ping,
                    TeamName = latestObservation?.TeamLabel
                }
            };

            onlinePlayers.Add(onlinePlayer);
        }

        // Calculate game breakdown by grouping raw gameIds
        var gameBreakdown = new OnlineGameBreakdown
        {
            GameCounts = onlinePlayers
                .GroupBy(p => p.CurrentServer.GameId)
                .ToDictionary(g => g.Key, g => g.Count())
        };

        return new OnlinePlayersResponse
        {
            Players = onlinePlayers.OrderByDescending(p => p.SessionDurationMinutes).ToList(),
            TotalOnline = onlinePlayers.Count,
            LastUpdated = now,
            GameBreakdown = gameBreakdown
        };
    }

    /// <summary>
    /// Get currently online players with pagination, sorting, and their session information
    /// </summary>
    public async Task<PagedResult<OnlinePlayer>> GetOnlinePlayersWithPagingAsync(
        int page = 1,
        int pageSize = 50,
        string sortBy = "servername_kills",
        string sortOrder = "desc",
        string? gameId = null,
        string? playerNameFilter = null,
        string? serverNameFilter = null)
    {
        var now = DateTime.UtcNow;
        var activeThreshold = TimeSpan.FromMinutes(5); // Consider players active if seen within last 5 minutes
        var thresholdTime = now.Subtract(activeThreshold); // Calculate the cutoff time

        // Build the base query for active sessions with all filters
        var baseQuery = _dbContext.PlayerSessions
            .Where(s => s.IsActive && s.LastSeenTime >= thresholdTime);

        // Apply filters
        if (!string.IsNullOrEmpty(gameId))
        {
            baseQuery = baseQuery.Where(s => s.Server.GameId == gameId);
        }

        if (!string.IsNullOrEmpty(playerNameFilter))
        {
            baseQuery = baseQuery.Where(s => s.PlayerName.ToLower().Contains(playerNameFilter.ToLower()));
        }

        if (!string.IsNullOrEmpty(serverNameFilter))
        {
            baseQuery = baseQuery.Where(s => s.Server.Name.ToLower().Contains(serverNameFilter.ToLower()));
        }

        // Get total count for pagination (before sorting and paging)
        var totalCount = await baseQuery.CountAsync();

        if (totalCount == 0)
        {
            return new PagedResult<OnlinePlayer>
            {
                Items = new List<OnlinePlayer>(),
                Page = page,
                PageSize = pageSize,
                TotalItems = 0,
                TotalPages = 0
            };
        }

        // Apply includes after all filters
        var queryWithIncludes = baseQuery
            .Include(s => s.Server)
            .Include(s => s.Player);

        // Apply sorting - for SessionDurationMinutes we'll sort after loading data
        // Other fields can be sorted at the database level
        var isDescending = sortOrder.ToLower() == "desc";
        
        List<PlayerSession> activeSessions;
        if (sortBy.ToLower() == "sessiondurationminutes")
        {
            // Get all sessions, calculate duration, sort, then paginate
            var allSessions = await queryWithIncludes.ToListAsync();
            var sessionsWithDuration = allSessions.Select(s => new 
            { 
                Session = s, 
                Duration = (int)Math.Ceiling((now - s.StartTime).TotalMinutes) 
            });
            
            var sortedSessions = isDescending 
                ? sessionsWithDuration.OrderByDescending(x => x.Duration)
                : sessionsWithDuration.OrderBy(x => x.Duration);
                
            activeSessions = sortedSessions
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .Select(x => x.Session)
                .ToList();
        }
        else
        {
            // Apply sorting and pagination for other fields
            var orderedQuery = sortBy.ToLower() switch
            {
                "playername" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.PlayerName)
                    : queryWithIncludes.OrderBy(s => s.PlayerName),
                "joinedat" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.StartTime)
                    : queryWithIncludes.OrderBy(s => s.StartTime),
                "servername" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.Server.Name)
                    : queryWithIncludes.OrderBy(s => s.Server.Name),
                "gameid" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.Server.GameId)
                    : queryWithIncludes.OrderBy(s => s.Server.GameId),
                "kills" or "sessionkills" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.TotalKills)
                    : queryWithIncludes.OrderBy(s => s.TotalKills),
                "servername_kills" => isDescending 
                    ? queryWithIncludes.OrderByDescending(s => s.Server.Name).ThenByDescending(s => s.TotalKills)
                    : queryWithIncludes.OrderBy(s => s.Server.Name).ThenBy(s => s.TotalKills),
                _ => queryWithIncludes.OrderByDescending(s => s.StartTime) // Default sort by join time
            };

            activeSessions = await orderedQuery
                .Skip((page - 1) * pageSize)
                .Take(pageSize)
                .ToListAsync();
        }

        // Get latest observations for all active sessions in a single efficient query
        var sessionIds = activeSessions.Select(s => s.SessionId).ToList();
        var latestObservations = await _dbContext.PlayerObservations
            .Where(o => sessionIds.Contains(o.SessionId))
            .GroupBy(o => o.SessionId)
            .Select(g => g.OrderByDescending(o => o.Timestamp).First())
            .ToDictionaryAsync(o => o.SessionId, o => o);

        var onlinePlayers = new List<OnlinePlayer>();

        foreach (var session in activeSessions)
        {
            // Get the latest observation for this session from our dictionary
            latestObservations.TryGetValue(session.SessionId, out var latestObservation);

            var sessionDurationMinutes = (int)Math.Ceiling((now - session.StartTime).TotalMinutes);

            var onlinePlayer = new OnlinePlayer
            {
                PlayerName = session.PlayerName,
                SessionDurationMinutes = sessionDurationMinutes,
                JoinedAt = session.StartTime,
                CurrentServer = new OnlinePlayerServer
                {
                    ServerGuid = session.ServerGuid,
                    ServerName = session.Server.Name,
                    GameId = session.Server.GameId,
                    MapName = session.MapName,
                    SessionKills = session.TotalKills,
                    SessionDeaths = session.TotalDeaths,
                    CurrentScore = session.TotalScore,
                    Ping = latestObservation?.Ping,
                    TeamName = latestObservation?.TeamLabel
                }
            };

            onlinePlayers.Add(onlinePlayer);
        }

        var totalPages = (int)Math.Ceiling((double)totalCount / pageSize);

        return new PagedResult<OnlinePlayer>
        {
            Items = onlinePlayers,
            Page = page,
            PageSize = pageSize,
            TotalItems = totalCount,
            TotalPages = totalPages
        };
    }
}

public class PlayerClickHouseStats
{
    public string PlayerName { get; set; } = "";
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
}
