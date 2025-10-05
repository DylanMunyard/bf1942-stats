using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using junie_des_1942stats.Telemetry;
using System.Diagnostics;
using System.Threading;

namespace junie_des_1942stats.PlayerStats;

public class PlayerStatsService(PlayerTrackerDbContext dbContext,
    PlayerInsightsService playerInsightsService,
    PlayerRoundsReadService playerRoundsReadService,
    ICacheService cacheService,
    ILogger<PlayerStatsService> logger,
    IConfiguration configuration)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly PlayerInsightsService _playerInsightsService = playerInsightsService;
    private readonly PlayerRoundsReadService _playerRoundsReadService = playerRoundsReadService;
    private readonly ICacheService _cacheService = cacheService;
    private readonly ILogger<PlayerStatsService> _logger = logger;
    private readonly IConfiguration _configuration = configuration;

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
                RoundId = s.RoundId,
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

        // Get server names for the insights using batch query
        if (serverInsights.Any())
        {
            var serverGuids = serverInsights.Select(si => si.ServerGuid).ToList();
            var servers = await _dbContext.Servers
                .Where(s => serverGuids.Contains(s.Guid))
                .Select(s => new { s.Guid, s.Name, s.GameId })
                .ToListAsync();

            var serverLookup = servers.ToDictionary(s => s.Guid, s => new { s.Name, s.GameId });

            foreach (var serverInsight in serverInsights)
            {
                if (serverLookup.TryGetValue(serverInsight.ServerGuid, out var server))
                {
                    serverInsight.ServerName = server.Name;
                    serverInsight.GameId = server.GameId;
                }
            }
        }


        // Get the current active session if any
        var activeSession = recentSessions
            .FirstOrDefault(ps => ps.IsActive);

        // Check if player is currently active (seen within the last 5 minutes)
        bool isActive = activeSession != null &&
                        (now - activeSession.LastSeenTime) <= _activeThreshold;

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
            RoundId = session.RoundId,
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

        var insights = new PlayerInsights
        {
            PlayerName = playerName,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // 1. Get server rankings and average ping
        var serverRankings = await GetServerRankingsWithPing(playerName);

        // Order by rank (best rank first) and assign to insights
        insights.ServerRankings = serverRankings
            .OrderBy(r => r.Rank)
            .ToList();

        // 2. Calculate activity by hour using native ClickHouse query
        var activityByHour = await GetActivityByHourFromClickHouse(playerName, startPeriod, endPeriod);
        insights.ActivityByHour = activityByHour;

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

    private async Task<List<ServerRanking>> GetServerRankingsWithPing(string playerName)
    {
        // First, get the player's server stats efficiently
        var playerServerStats = await _dbContext.ServerPlayerRankings
            .Where(r => r.PlayerName == playerName)
            .GroupBy(r => r.ServerGuid)
            .Select(g => new
            {
                ServerGuid = g.Key,
                TotalScore = g.Sum(x => x.TotalScore)
            })
            .ToListAsync();

        if (!playerServerStats.Any())
            return [];

        // Get server names separately
        var serverGuids = playerServerStats.Select(s => s.ServerGuid).ToList();
        var servers = await _dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        // Get ping data from ClickHouse with native sampling - much more efficient
        var pingData = await GetAveragePingFromClickHouse(playerName, serverGuids);

        // Calculate rankings using a more efficient approach - one query per server but much faster
        var results = new List<ServerRanking>();

        foreach (var serverStat in playerServerStats)
        {
            // Count players with higher scores + get total players in one query per server
            var rankingSql = @"
                SELECT 
                    (SELECT COUNT(*) + 1 
                     FROM (SELECT PlayerName, SUM(TotalScore) as Total 
                           FROM ServerPlayerRankings 
                           WHERE ServerGuid = @serverGuid 
                           GROUP BY PlayerName) 
                     WHERE Total > @playerScore) as PlayerRank,
                    (SELECT COUNT(DISTINCT PlayerName) 
                     FROM ServerPlayerRankings 
                     WHERE ServerGuid = @serverGuid) as TotalPlayers";

            var rankingResult = await _dbContext.Database
                .SqlQueryRaw<RankingResult>(rankingSql,
                    new Microsoft.Data.Sqlite.SqliteParameter("@serverGuid", serverStat.ServerGuid),
                    new Microsoft.Data.Sqlite.SqliteParameter("@playerScore", serverStat.TotalScore))
                .FirstAsync();

            results.Add(new ServerRanking
            {
                ServerGuid = serverStat.ServerGuid,
                ServerName = servers.GetValueOrDefault(serverStat.ServerGuid, "Unknown Server"),
                Rank = rankingResult.PlayerRank,
                TotalScore = serverStat.TotalScore,
                TotalRankedPlayers = rankingResult.TotalPlayers,
                AveragePing = Math.Round(pingData.GetValueOrDefault(serverStat.ServerGuid, 0.0), 2)
            });
        }

        return results;
    }

    private async Task<Dictionary<string, double>> GetAveragePingFromClickHouse(string playerName, List<string> serverGuids)
    {
        if (!serverGuids.Any())
            return new Dictionary<string, double>();

        try
        {
            // Escape single quotes to prevent SQL injection
            var escapedPlayerName = playerName.Replace("'", "''");
            var serverGuidsParam = string.Join("','", serverGuids.Select(g => g.Replace("'", "''")));

            // Use pre-aggregated player_rounds table instead of player_metrics
            var query = $@"
                SELECT
                    server_guid,
                    AVG(average_ping) as avg_ping,
                    COUNT(*) as sample_size
                FROM player_rounds
                WHERE player_name = '{escapedPlayerName}'
                    AND server_guid IN ('{serverGuidsParam}')
                    AND average_ping IS NOT NULL
                    AND round_start_time >= now() - INTERVAL 6 MONTH
                GROUP BY server_guid";

            var result = await _playerRoundsReadService.ExecuteQueryAsync(query);
            var pingResults = new List<ClickHousePingResult>();

            foreach (var line in result.Split('\n', StringSplitOptions.RemoveEmptyEntries))
            {
                var parts = line.Split('\t');
                if (parts.Length >= 2 &&
                    double.TryParse(parts[1], out var avgPing))
                {
                    pingResults.Add(new ClickHousePingResult
                    {
                        server_guid = parts[0],
                        average_ping = avgPing,
                        sample_size = parts.Length > 2 && int.TryParse(parts[2], out var size) ? size : 0
                    });
                }
            }

            return pingResults.ToDictionary(r => r.server_guid, r => r.average_ping);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get ping data from ClickHouse for player {PlayerName}", playerName);
            return new Dictionary<string, double>();
        }
    }

    private async Task<List<HourlyActivity>> GetActivityByHourFromClickHouse(string playerName, DateTime startPeriod, DateTime endPeriod)
    {
        try
        {
            // Use ClickHouse's native time functions to calculate activity by hour
            // This query aggregates session time directly in the database
            var query = $@"
WITH sessions_with_duration AS (
    SELECT 
        toHour(round_start_time) as start_hour,
        toHour(round_end_time) as end_hour,
        round_start_time as start_time,
        round_end_time as last_seen_time,
        CASE 
            WHEN toHour(round_start_time) = toHour(round_end_time) THEN 
                -- Session within same hour
                dateDiff('minute', round_start_time, round_end_time)
            ELSE 0
        END as same_hour_minutes
    FROM player_rounds 
    WHERE player_name = '{playerName.Replace("'", "''")}'
      AND round_start_time >= '{startPeriod:yyyy-MM-dd HH:mm:ss}'
      AND round_end_time <= '{endPeriod:yyyy-MM-dd HH:mm:ss}'
),
hour_breakdown AS (
    -- Generate all possible hours (0-23)
    SELECT number as hour
    FROM numbers(24)
),
session_hours AS (
    SELECT 
        h.hour,
        s.start_time,
        s.last_seen_time,
        CASE 
            WHEN s.start_hour = s.end_hour AND s.start_hour = h.hour THEN 
                -- Entire session in this hour
                s.same_hour_minutes
            WHEN s.start_hour <= h.hour AND h.hour <= s.end_hour THEN 
                CASE 
                    WHEN h.hour = s.start_hour THEN 
                        -- First hour of multi-hour session
                        dateDiff('minute', s.start_time, toStartOfHour(s.start_time) + INTERVAL 1 HOUR)
                    WHEN h.hour = s.end_hour THEN 
                        -- Last hour of multi-hour session  
                        dateDiff('minute', toStartOfHour(s.last_seen_time), s.last_seen_time)
                    ELSE 
                        -- Full hour in middle of session
                        60
                END
            ELSE 0
        END as minutes_in_hour
    FROM hour_breakdown h
    CROSS JOIN sessions_with_duration s
    WHERE s.start_hour <= h.hour AND h.hour <= s.end_hour
)
SELECT 
    hour,
    SUM(minutes_in_hour) as total_minutes
FROM session_hours
WHERE minutes_in_hour > 0
GROUP BY hour
ORDER BY total_minutes DESC
FORMAT TabSeparated";

            var result = await _playerInsightsService.ExecuteQueryAsync(query);
            var lines = result.Split('\n', StringSplitOptions.RemoveEmptyEntries);

            var hourlyActivity = new List<HourlyActivity>();

            foreach (var line in lines)
            {
                var parts = line.Split('\t');
                if (parts.Length == 2 &&
                    int.TryParse(parts[0], out var hour) &&
                    int.TryParse(parts[1], out var minutes))
                {
                    hourlyActivity.Add(new HourlyActivity
                    {
                        Hour = hour,
                        MinutesActive = minutes
                    });
                }
            }

            // Fill in missing hours with 0 minutes
            var existingHours = hourlyActivity.Select(h => h.Hour).ToHashSet();
            for (int hour = 0; hour < 24; hour++)
            {
                if (!existingHours.Contains(hour))
                {
                    hourlyActivity.Add(new HourlyActivity { Hour = hour, MinutesActive = 0 });
                }
            }

            return hourlyActivity.OrderByDescending(h => h.MinutesActive).ToList();
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get activity by hour from ClickHouse for player {PlayerName}, falling back to SQLite calculation", playerName);

            // Fallback to original SQLite-based calculation if ClickHouse fails
            return await GetActivityByHourFromSessions(playerName, startPeriod, endPeriod);
        }
    }

    private async Task<List<HourlyActivity>> GetActivityByHourFromSessions(string playerName, DateTime startPeriod, DateTime endPeriod)
    {
        // Fallback method using the original SQLite-based calculation
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName && ps.StartTime >= startPeriod && ps.LastSeenTime <= endPeriod)
            .ToListAsync();

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

        return hourlyActivity
            .Select(kvp => new HourlyActivity { Hour = kvp.Key, MinutesActive = kvp.Value })
            .OrderByDescending(ha => ha.MinutesActive)
            .ToList();
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
