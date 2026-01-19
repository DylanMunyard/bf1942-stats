using api.Players.Models;
using api.PlayerTracking;
using api.PlayerStats;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Players;

public class PlayerStatsService(PlayerTrackerDbContext dbContext,
    ISqlitePlayerStatsService sqlitePlayerStatsService,
    ILogger<PlayerStatsService> logger) : IPlayerStatsService
{
    // Define a threshold for considering a player "active" (e.g., 5 minutes)
    private readonly TimeSpan _activeThreshold = TimeSpan.FromMinutes(1);

    public async Task<PagedResult<PlayerBasicInfo>> GetAllPlayersWithPaging(
        int page,
        int pageSize,
        string sortBy,
        string sortOrder,
        PlayerFilters? filters = null)
    {
        var baseQuery = dbContext.Players.Where(p => !p.AiBot);

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

            if (!string.IsNullOrEmpty(filters.Game))
            {
                baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive &&
                    s.Server.Game == filters.Game));
            }

            if (!string.IsNullOrEmpty(filters.MapName))
            {
                baseQuery = baseQuery.Where(p => p.Sessions.Any(s => s.IsActive &&
                    s.MapName.Contains(filters.MapName)));
            }
        }

        // Apply sorting at database level
        var isDescending = sortOrder.ToLower() == "desc";

        var query = sortBy.ToLower() switch
        {
            "playername" => isDescending
                ? baseQuery.OrderByDescending(p => p.Name)
                : baseQuery.OrderBy(p => p.Name),
            "totalplaytimeminutes" => isDescending
                ? baseQuery.OrderByDescending(p => p.TotalPlayTimeMinutes)
                : baseQuery.OrderBy(p => p.TotalPlayTimeMinutes),
            "lastseen" => isDescending
                ? baseQuery.OrderByDescending(p => p.LastSeen)
                : baseQuery.OrderBy(p => p.LastSeen),
            "isactive" => isDescending
                ? baseQuery.OrderByDescending(p => p.Sessions.Any(s => s.IsActive)).ThenByDescending(p => p.LastSeen)
                : baseQuery.OrderBy(p => p.Sessions.Any(s => s.IsActive)).ThenByDescending(p => p.LastSeen),
            _ => baseQuery.OrderByDescending(p => p.Sessions.Any(s => s.IsActive)).ThenByDescending(p => p.LastSeen)
        };

        // Now project to PlayerBasicInfo
        var projectedQuery = query.Select(p => new PlayerBasicInfo
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

        // Get total count for pagination (after filters are applied)
        var totalCount = await projectedQuery.CountAsync();

        // Apply pagination
        var players = await projectedQuery
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
        var player = await dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
            return new PlayerTimeStatistics();

        var now = DateTime.UtcNow;

        // Execute queries - SQLite paths run sequentially to avoid DbContext threading issues

        // 1. First run all DbContext queries sequentially
        var sessionStats = await dbContext.PlayerSessions
            .Where(ps => ps.PlayerName == playerName)
            .GroupBy(ps => ps.PlayerName)
            .Select(g => new
            {
                FirstPlayed = g.Min(s => s.StartTime),
                LastPlayed = g.Max(s => s.LastSeenTime)
            })
            .FirstOrDefaultAsync();

        var recentSessions = await dbContext.PlayerSessions
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

        // 2. Get player stats (SQLite)
        PlayerLifetimeStats? lifetimeStats = null;
        try
        {
            lifetimeStats = await sqlitePlayerStatsService.GetPlayerStatsAsync(playerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get player stats for player: {PlayerName}", playerName);
        }

        // 3. Get server insights (SQLite)
        List<ServerInsight> serverInsights;
        try
        {
            serverInsights = await sqlitePlayerStatsService.GetPlayerServerInsightsAsync(playerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get server insights for player: {PlayerName}", playerName);
            serverInsights = [];
        }

        // 4. Get best scores (SQLite)
        PlayerBestScores bestScores;
        try
        {
            bestScores = await sqlitePlayerStatsService.GetPlayerBestScoresAsync(playerName);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get best scores for player: {PlayerName}", playerName);
            bestScores = new PlayerBestScores();
        }

        // 5. Get insights and recent stats trends
        var insights = await GetPlayerInsights(playerName);
        var recentStats = await GetRecentStatsTrends(playerName);

        var aggregateStats = new
        {
            FirstPlayed = sessionStats?.FirstPlayed ?? DateTime.MinValue,
            LastPlayed = sessionStats?.LastPlayed ?? DateTime.MinValue,
            TotalKills = lifetimeStats?.TotalKills ?? 0,
            TotalDeaths = lifetimeStats?.TotalDeaths ?? 0,
            TotalPlayTimeMinutes = lifetimeStats != null
                ? (int)Math.Round(lifetimeStats.TotalPlayTimeMinutes)
                : 0
        };

        // Get server names for the insights using batch query
        if (serverInsights.Any())
        {
            var serverGuids = serverInsights.Select(si => si.ServerGuid).ToList();
            var servers = await dbContext.Servers
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
            Servers = serverInsights,
            RecentStats = recentStats,
            BestScores = bestScores
        };

        return stats;
    }

    public async Task<SessionDetail?> GetSession(string playerName, int sessionId)
    {
        var session = await dbContext.PlayerSessions
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
        var player = await dbContext.Players
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

        // 2. Calculate activity by hour from sessions
        var activityByHour = await GetActivityByHourFromSessions(playerName, startPeriod, endPeriod);
        insights.ActivityByHour = activityByHour;

        return insights;
    }

    private async Task<List<ServerRanking>> GetServerRankingsWithPing(string playerName)
    {
        // First, get the player's server stats efficiently
        var playerServerStats = await dbContext.ServerPlayerRankings
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
        var servers = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .ToDictionaryAsync(s => s.Guid, s => s.Name);

        // Get ping data from SQLite PlayerSessions
        var pingData = await GetAveragePingFromSessions(playerName, serverGuids);

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

            var rankingResult = await dbContext.Database
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

    private async Task<Dictionary<string, double>> GetAveragePingFromSessions(string playerName, List<string> serverGuids)
    {
        if (!serverGuids.Any())
            return new Dictionary<string, double>();

        try
        {
            var sixMonthsAgo = DateTime.UtcNow.AddMonths(-6);

            var pingData = await dbContext.PlayerSessions
                .Where(ps => ps.PlayerName == playerName &&
                            serverGuids.Contains(ps.ServerGuid) &&
                            ps.AveragePing > 0 &&
                            ps.AveragePing < 1000 &&
                            ps.StartTime >= sixMonthsAgo)
                .GroupBy(ps => ps.ServerGuid)
                .Select(g => new
                {
                    ServerGuid = g.Key,
                    AvgPing = g.Average(ps => ps.AveragePing)
                })
                .ToListAsync();

            return pingData.ToDictionary(p => p.ServerGuid, p => p.AvgPing ?? 0);
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to get ping data from SQLite for player {PlayerName}", playerName);
            return new Dictionary<string, double>();
        }
    }

    private async Task<List<HourlyActivity>> GetActivityByHourFromSessions(string playerName, DateTime startPeriod, DateTime endPeriod)
    {
        // Fallback method using the original SQLite-based calculation
        var sessions = await dbContext.PlayerSessions
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

    private async Task<RecentStats> GetRecentStatsTrends(string playerName)
    {
        // Calculate trends over the last 90 days
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-90);

        // Use raw SQL to aggregate data directly in the database to avoid loading all sessions into memory
        var sql = @"
            SELECT
                DATE(StartTime) as Date,
                SUM(TotalKills) as TotalKills,
                SUM(TotalDeaths) as TotalDeaths,
                CAST(SUM((julianday(LastSeenTime) - julianday(StartTime)) * 1440) AS REAL) as TotalMinutes,
                COUNT(*) as SessionCount
            FROM PlayerSessions
            WHERE PlayerName = {0}
                AND StartTime >= {1}
                AND LastSeenTime <= {2}
            GROUP BY DATE(StartTime)
            ORDER BY DATE(StartTime)";

        var dailyStats = await dbContext.Database
            .SqlQueryRaw<DailyStatsResult>(sql, playerName, startDate, endDate)
            .ToListAsync();

        // Calculate total rounds analyzed
        var totalRoundsAnalyzed = dailyStats.Sum(d => d.SessionCount);

        if (!dailyStats.Any())
        {
            return new RecentStats
            {
                AnalysisPeriodStart = startDate,
                AnalysisPeriodEnd = endDate,
                TotalRoundsAnalyzed = 0,
                KdRatioTrend = new List<TrendDataPoint>(),
                KillRateTrend = new List<TrendDataPoint>()
            };
        }

        // Create trend data points with calculations done in memory (only for aggregated daily data)
        var kdRatioTrend = dailyStats
            .Select(d => new TrendDataPoint
            {
                Timestamp = d.Date,
                Value = d.TotalDeaths > 0 ? (double)d.TotalKills / d.TotalDeaths : d.TotalKills
            })
            .ToList();

        var killRateTrend = dailyStats
            .Select(d => new TrendDataPoint
            {
                Timestamp = d.Date,
                Value = d.TotalMinutes > 0 ? (double)d.TotalKills / d.TotalMinutes : 0
            })
            .ToList();

        return new RecentStats
        {
            AnalysisPeriodStart = startDate,
            AnalysisPeriodEnd = endDate,
            TotalRoundsAnalyzed = totalRoundsAnalyzed,
            KdRatioTrend = kdRatioTrend,
            KillRateTrend = killRateTrend
        };
    }

    private class DailyStatsResult
    {
        public DateTime Date { get; set; }
        public int TotalKills { get; set; }
        public int TotalDeaths { get; set; }
        public double TotalMinutes { get; set; }
        public int SessionCount { get; set; }
    }


}
