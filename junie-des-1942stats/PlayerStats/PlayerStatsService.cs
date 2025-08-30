using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Telemetry;
using System.Diagnostics;

namespace junie_des_1942stats.PlayerStats;

public class PlayerStatsService(PlayerTrackerDbContext dbContext,
    PlayerInsightsService playerInsightsService,
    PlayerRoundsReadService playerRoundsReadService,
    ICacheService cacheService,
    ILogger<PlayerStatsService> logger)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly PlayerInsightsService _playerInsightsService = playerInsightsService;
    private readonly PlayerRoundsReadService _playerRoundsReadService = playerRoundsReadService;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILogger<PlayerStatsService> _logger = logger;

    // Define a threshold for considering a player "active" (e.g., 5 minutes)
    private readonly TimeSpan _activeThreshold = TimeSpan.FromMinutes(1);

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

    private async Task<List<MonthlyServerRanking>> GetPlayerHistoricalRankingsAsync(string playerName, string serverGuid)
    {
        return await _dbContext.ServerPlayerRankings
            .Where(r => r.PlayerName == playerName && r.ServerGuid == serverGuid)
            .OrderByDescending(r => r.Year)
            .ThenByDescending(r => r.Month)
            .Take(12)
            .Select(r => new MonthlyServerRanking
            {
                Year = r.Year,
                Month = r.Month,
                Rank = r.Rank,
                TotalScore = r.TotalScore,
                TotalKills = r.TotalKills,
                TotalDeaths = r.TotalDeaths,
                KDRatio = r.KDRatio,
                TotalPlayTimeMinutes = r.TotalPlayTimeMinutes
            })
            .ToListAsync();
    }

    public async Task<PlayerTimeStatistics> GetPlayerStatistics(string playerName)
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
                LastSeenTime = s.LastSeenTime,
                TotalKills = s.TotalKills,
                TotalDeaths = s.TotalDeaths,
                TotalScore = s.TotalScore,
                IsActive = s.IsActive,
                GameId = s.Server.GameId
            })
            .ToListAsync();


        // Get the current active session if any
        var activeSession = recentSessions
            .FirstOrDefault(ps => ps.IsActive);

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null &&
                        (now - activeSession.LastSeenTime) <= _activeThreshold;

        var insights = await GetPlayerInsights(playerName);

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
                    ServerName = activeSession.ServerName,
                    SessionKills = activeSession.TotalKills,
                    SessionDeaths = activeSession.TotalDeaths,
                    GameId = activeSession.GameId,
                    MapName = activeSession.MapName,
                }
                : null,
            RecentSessions = recentSessions,
            Insights = insights,
            Servers = serverInsights
        };

        // Calculate time series trend stats over last 6 months
        stats.RecentStats = await CalculateTimeSeriesTrendAsync(playerName);

        // Get best scores for different time periods
        try
        {
            stats.BestScores = await _playerRoundsReadService.GetPlayerBestScoresAsync(playerName);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get best scores for player: {PlayerName}", playerName);
            stats.BestScores = new PlayerBestScores(); // Return empty object instead of null
        }

        return stats;
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

            // Get historical rankings for this server
            var historicalRankings = await GetPlayerHistoricalRankingsAsync(playerName, serverStat.ServerGuid);

            return new ServerRanking
            {
                ServerGuid = serverStat.ServerGuid,
                ServerName = serverStat.Name,
                Rank = playerRank,
                TotalScore = serverStat.TotalScore,
                TotalRankedPlayers = totalPlayers,
                AveragePing = Math.Round(averagePing),
                HistoricalRankings = historicalRankings
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
        catch (Exception)
        {
            // Log error but don't fail the request
            // _logger?.LogError(ex, "Failed to get player stats from ClickHouse for player: {PlayerName}", playerName);
        }

        return null;
    }

    private async Task<RecentStats?> CalculateTimeSeriesTrendAsync(string playerName)
    {
        try
        {
            _logger.LogDebug("Calculating time series trend data for player: {PlayerName}", playerName);

            // Look back 6 months for trend analysis
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

            // Get time series data from ClickHouse
            var timeSeriesResult = await _playerRoundsReadService.GetPlayerTimeSeriesTrendAsync(playerName, sixMonthsAgo);

            _logger.LogDebug("ClickHouse time series result for {PlayerName}: {Result}", playerName,
                timeSeriesResult?.Substring(0, Math.Min(200, timeSeriesResult?.Length ?? 0)));

            // Parse time series data
            var trendPoints = ParseTimeSeriesData(timeSeriesResult ?? "");
            if (!trendPoints.Any())
            {
                _logger.LogWarning("No time series data found for player: {PlayerName}. Raw result: {RawResult}",
                    playerName, timeSeriesResult);
                return null;
            }

            // Calculate total rounds analyzed from the time series query
            var totalRounds = await GetPlayerRoundCountAsync(playerName, sixMonthsAgo);

            var recentStats = new RecentStats
            {
                AnalysisPeriodStart = sixMonthsAgo,
                AnalysisPeriodEnd = DateTime.UtcNow,
                TotalRoundsAnalyzed = totalRounds,
                KdRatioTrend = trendPoints.Select(tp => new TrendDataPoint
                {
                    Timestamp = tp.Timestamp,
                    Value = tp.KdRatio
                }).ToList(),
                KillRateTrend = trendPoints.Select(tp => new TrendDataPoint
                {
                    Timestamp = tp.Timestamp,
                    Value = tp.KillRate
                }).ToList()
            };

            return recentStats;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to calculate time series trend for player: {PlayerName}", playerName);
            return null;
        }
    }

    private async Task<int> GetPlayerRoundCountAsync(string playerName, DateTime fromDate)
    {
        try
        {
            var countResult = await _playerRoundsReadService.ExecuteQueryAsync($@"
                SELECT COUNT(*) as round_count
                FROM player_rounds
                WHERE player_name = '{playerName.Replace("'", "''")}'
                  AND round_end_time >= '{fromDate:yyyy-MM-dd HH:mm:ss}'
                FORMAT TabSeparated");

            var lines = countResult.Split('\n', StringSplitOptions.RemoveEmptyEntries);
            if (lines.Length > 0 && int.TryParse(lines[0], out var count))
            {
                return count;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get round count for player: {PlayerName}", playerName);
        }

        return 0;
    }

    private List<TimeSeriesPoint> ParseTimeSeriesData(string result)
    {
        var points = new List<TimeSeriesPoint>();

        if (string.IsNullOrWhiteSpace(result))
        {
            _logger.LogWarning("Empty or null time series result from ClickHouse");
            return points;
        }

        var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        _logger.LogDebug("Parsing {LineCount} lines of time series data", lines.Length);

        // Skip header row
        var dataLines = lines.Skip(1);

        foreach (var line in dataLines)
        {
            try
            {
                var parts = line.Split('\t');
                if (parts.Length >= 3)
                {
                    points.Add(new TimeSeriesPoint
                    {
                        Timestamp = DateTime.Parse(parts[0]),
                        KdRatio = double.Parse(parts[1]),
                        KillRate = double.Parse(parts[2])
                    });
                }
                else
                {
                    _logger.LogWarning("Invalid time series line format (expected 3+ parts, got {PartCount}): {Line}", parts.Length, line);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to parse time series line: {Line}", line);
            }
        }

        _logger.LogDebug("Successfully parsed {PointCount} time series points", points.Count);
        return points;
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

public class TimeSeriesPoint
{
    public DateTime Timestamp { get; set; }
    public double KdRatio { get; set; }
    public double KillRate { get; set; }
}
