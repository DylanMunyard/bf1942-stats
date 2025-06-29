using System.Data.SqlTypes;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Prometheus;
using junie_des_1942stats.ServerStats.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.ServerStats;

// Helper class for raw SQL query results
public class PingTimestampData
{
    public DateTime Timestamp { get; set; }
    public int Ping { get; set; }
}

public class ServerStatsService(PlayerTrackerDbContext dbContext, PrometheusService prometheusService, ILogger<ServerStatsService> logger)
{
    private readonly PlayerTrackerDbContext _dbContext = dbContext;
    private readonly PrometheusService _prometheusService = prometheusService;
    private readonly ILogger<ServerStatsService> _logger = logger;

    public async Task<ServerStatistics> GetServerStatistics(
        string serverName,
        int daysToAnalyze = 7)
    {
        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new ServerStatistics { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Create the statistics object
        var statistics = new ServerStatistics
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            Region = server.Region ?? string.Empty,
            Country = server.Country ?? string.Empty,
            Timezone = server.Timezone ?? string.Empty,
            ServerIp = server.Ip,
            ServerPort = server.Port,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Get sessions for this server within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.ServerGuid == server.Guid && ps.StartTime >= startPeriod && ps.LastSeenTime <= endPeriod)
            .Include(s => s.Player)
            .ToListAsync();

        // Get most active players by time played using raw SQL
        var mostActivePlayers = await _dbContext.Database.SqlQueryRaw<PlayerActivity>(@"
            SELECT 
                ps.PlayerName AS PlayerName,
                CAST(SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS INTEGER) AS MinutesPlayed,
                SUM(ps.TotalKills) AS TotalKills,
                SUM(ps.TotalDeaths) AS TotalDeaths
            FROM PlayerSessions ps
            WHERE ps.ServerGuid = {0}
              AND ps.StartTime >= {1}
              AND ps.LastSeenTime <= {2}
            GROUP BY ps.PlayerName
            ORDER BY MinutesPlayed DESC
            LIMIT 10",
            server.Guid, startPeriod, endPeriod).ToListAsync();

        statistics.MostActivePlayersByTime = mostActivePlayers;

        // Get top 10 scores in the period using LINQ to SQL
        var topScores = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == server.Guid && s.StartTime >= startPeriod && s.LastSeenTime <= endPeriod)
            .OrderByDescending(s => s.TotalScore)
            .Take(10)
            .Select(s => new TopScore
            {
                PlayerName = s.PlayerName,
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                MapName = s.MapName,
                Timestamp = s.LastSeenTime,
                SessionId = s.SessionId
            })
            .ToListAsync();

        statistics.TopScores = topScores;

        // Get the last 5 rounds (unique maps) showing when each map was last played
        // Use a fixed 5-hour window for recent map rotations (much faster than analyzing days of data)
        var recentRoundsStart = DateTime.UtcNow.AddHours(-5);
        var lastRounds = await _dbContext.Database.SqlQueryRaw<RoundInfo>(@"
            SELECT 
                ps.MapName,
                MAX(ps.StartTime) as StartTime,
                MAX(ps.LastSeenTime) as EndTime,
                CASE WHEN SUM(CASE WHEN ps.IsActive = 1 THEN 1 ELSE 0 END) > 0 THEN 1 ELSE 0 END as IsActive
            FROM PlayerSessions ps
            WHERE ps.ServerGuid = {0} 
              AND ps.StartTime >= {1}
            GROUP BY ps.MapName
            ORDER BY MAX(ps.StartTime) DESC
            LIMIT 5",
            server.Guid, recentRoundsStart).ToListAsync();

        statistics.LastRounds = lastRounds;

        // Call both Prometheus methods in parallel
        try
        {
            var playerHistoryTask = _prometheusService.GetServerPlayersHistory(serverName, server.GameId, daysToAnalyze);
            var playerCountChangeTask = _prometheusService.GetAveragePlayerCountChange(serverName, server.GameId, daysToAnalyze);

            await Task.WhenAll(playerHistoryTask, playerCountChangeTask);

            var playerHistory = await playerHistoryTask;
            var playerCountChange = await playerCountChangeTask;

            // Process player history metrics
            if (playerHistory != null &&
                playerHistory.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
                playerHistory.Data.Result.Count > 0)
            {
                statistics.PlayerCountMetrics = playerHistory.Data.Result[0].Values;
            }

            // Process player count change percentage
            if (playerCountChange != null &&
                playerCountChange.Status.Equals("success", StringComparison.OrdinalIgnoreCase) &&
                playerCountChange.Data.Result.Count > 0 &&
                playerCountChange.Data.Result[0].Value is not null)
            {
                var changeValue = playerCountChange.Data.Result[0].Value.Value;
                // Round to nearest whole number
                statistics.AveragePlayerCountChangePercent = (int)Math.Round(changeValue);
            }
        }
        catch (Exception ex)
        {
            // Log the error but continue with empty metrics
            _logger.LogError(ex, "Error fetching metrics from Prometheus");
            statistics.PlayerCountMetrics = [];
            statistics.AveragePlayerCountChangePercent = null;
        }

        return statistics;
    }

    public async Task<PagedResult<ServerRanking>> GetServerRankings(string serverName, int? year = null, int page = 1, int pageSize = 100,
        string? playerName = null, int? minScore = null, int? minKills = null, int? minDeaths = null, 
        double? minKdRatio = null, int? minPlayTimeMinutes = null, string? orderBy = "TotalScore", string? orderDirection = "desc")
    {
        if (page < 1)
            throw new ArgumentException("Page number must be at least 1");
        
        if (pageSize < 1 || pageSize > 100)
            throw new ArgumentException("Page size must be between 1 and 100");

        // Validate orderBy parameter
        var validOrderByColumns = new[] { "TotalScore", "TotalKills", "TotalDeaths", "KDRatio", "TotalPlayTimeMinutes" };
        if (!string.IsNullOrEmpty(orderBy) && !validOrderByColumns.Contains(orderBy, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException($"Invalid orderBy column. Valid columns are: {string.Join(", ", validOrderByColumns)}");

        // Validate orderDirection parameter
        var validDirections = new[] { "asc", "desc" };
        if (!string.IsNullOrEmpty(orderDirection) && !validDirections.Contains(orderDirection, StringComparer.OrdinalIgnoreCase))
            throw new ArgumentException("Order direction must be 'asc' or 'desc'");

        // Set defaults and normalize case
        orderBy = orderBy ?? "TotalScore";
        orderDirection = orderDirection ?? "desc";
        var isDescending = orderDirection.Equals("desc", StringComparison.OrdinalIgnoreCase);

        IQueryable<ServerPlayerRanking> baseQuery = _dbContext.ServerPlayerRankings
            .Where(sr => sr.Server.Name == serverName);

        // If year is provided, filter by year and month (use all months for the year)
        if (year.HasValue)
        {
            baseQuery = baseQuery.Where(sr => sr.Year == year.Value);
        }

        // Apply player name filter (case insensitive)
        if (!string.IsNullOrWhiteSpace(playerName))
        {
            baseQuery = baseQuery.Where(sr => sr.PlayerName.ToLower().Contains(playerName.ToLower()));
        }

        // Get the aggregated data first, then apply numeric filters
        var playerStatsQuery = baseQuery
            .GroupBy(sr => sr.PlayerName)
            .Select(g => new
            {
                PlayerName = g.Key,
                TotalScore = g.Sum(r => r.TotalScore),
                TotalKills = g.Sum(r => r.TotalKills),
                TotalDeaths = g.Sum(r => r.TotalDeaths),
                TotalPlayTimeMinutes = g.Sum(r => r.TotalPlayTimeMinutes)
            });

        // Apply numeric filters
        if (minScore.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalScore >= minScore.Value);
        }

        if (minKills.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalKills >= minKills.Value);
        }

        if (minDeaths.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalDeaths >= minDeaths.Value);
        }

        if (minKdRatio.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => 
                x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths >= minKdRatio.Value : x.TotalKills >= minKdRatio.Value);
        }

        if (minPlayTimeMinutes.HasValue)
        {
            playerStatsQuery = playerStatsQuery.Where(x => x.TotalPlayTimeMinutes >= minPlayTimeMinutes.Value);
        }

        // Get the total count of filtered players for pagination
        var totalItems = await playerStatsQuery.CountAsync();

        // Apply dynamic ordering and pagination
        var orderedQuery = orderBy.ToLowerInvariant() switch
        {
            "totalscore" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalScore) : playerStatsQuery.OrderBy(x => x.TotalScore),
            "totalkills" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalKills) : playerStatsQuery.OrderBy(x => x.TotalKills),
            "totaldeaths" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalDeaths) : playerStatsQuery.OrderBy(x => x.TotalDeaths),
            "totalplaytimeminutes" => isDescending ? playerStatsQuery.OrderByDescending(x => x.TotalPlayTimeMinutes) : playerStatsQuery.OrderBy(x => x.TotalPlayTimeMinutes),
            "kdratio" => isDescending 
                ? playerStatsQuery.OrderByDescending(x => x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths : x.TotalKills)
                : playerStatsQuery.OrderBy(x => x.TotalDeaths > 0 ? (double)x.TotalKills / x.TotalDeaths : x.TotalKills),
            _ => playerStatsQuery.OrderByDescending(x => x.TotalScore) // Default fallback
        };

        var finalQuery = orderedQuery
            .Skip((page - 1) * pageSize)
            .Take(pageSize);

        // Execute the query and materialize results
        var playerStats = await finalQuery.ToListAsync();

        // Now rank just the paged results in memory
        var items = playerStats
            .Select((x, index) => new ServerRanking
            {
                Rank = ((page - 1) * pageSize) + index + 1, // Calculate global rank based on page position
                ServerGuid = baseQuery.First().ServerGuid, // All records are for the same server
                ServerName = serverName, // Use the provided server name
                PlayerName = x.PlayerName,
                TotalScore = x.TotalScore,
                TotalKills = x.TotalKills,
                TotalDeaths = x.TotalDeaths,
                KDRatio = x.TotalDeaths > 0 ? Math.Round((double)x.TotalKills / x.TotalDeaths, 2) : x.TotalKills,
                TotalPlayTimeMinutes = x.TotalPlayTimeMinutes
            })
            .ToList();

        // Create minimal server context info
        var serverContext = new ServerContextInfo
        {
            ServerGuid = items.FirstOrDefault()?.ServerGuid,
            ServerName = serverName
        };

        return new PagedResult<ServerRanking>
        {
            CurrentPage = page,
            TotalPages = (int)Math.Ceiling(totalItems / (double)pageSize),
            Items = items,
            TotalItems = totalItems,
            ServerContext = serverContext
        };
    }

    public async Task<MapStatistics> GetMapStatistics(string serverName, string mapName, int daysToAnalyze = 7)
    {
        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new MapStatistics
            {
                ServerName = serverName,
                MapName = mapName,
                StartPeriod = startPeriod,
                EndPeriod = endPeriod
            };

        // Create the statistics object
        var statistics = new MapStatistics
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            MapName = mapName,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Get sessions for this server and map within the time period
        var sessions = await _dbContext.PlayerSessions
            .Where(ps => ps.ServerGuid == server.Guid &&
                         ps.MapName == mapName &&
                         ps.StartTime >= startPeriod &&
                         ps.LastSeenTime <= endPeriod)
            .Include(s => s.Player)
            .ToListAsync();

        // If no sessions match this map, return empty stats
        if (!sessions.Any())
        {
            return statistics;
        }

        // Calculate map statistics
        statistics.PlayerCount = sessions.Select(s => s.PlayerName).Distinct().Count();
        statistics.TotalMinutesPlayed =
            sessions.Sum(s => (int)Math.Ceiling((s.LastSeenTime - s.StartTime).TotalMinutes));
        statistics.TotalSessions = sessions.Count;

        // Get most active players by time played using raw SQL
        var mostActivePlayers = await _dbContext.Database.SqlQueryRaw<PlayerActivity>(@"
            SELECT 
                ps.PlayerName AS PlayerName,
                CAST(SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 1440) AS INTEGER) AS MinutesPlayed,
                SUM(ps.TotalKills) AS TotalKills,
                SUM(ps.TotalDeaths) AS TotalDeaths
            FROM PlayerSessions ps
            WHERE ps.ServerGuid = {0}
              AND ps.StartTime >= {1}
              AND ps.LastSeenTime <= {2}
            GROUP BY ps.PlayerName
            ORDER BY MinutesPlayed DESC
            LIMIT 10",
            server.Guid, startPeriod, endPeriod).ToListAsync();

        statistics.MostActivePlayersByTime = mostActivePlayers;

        // Get top 10 scores in the period using LINQ to SQL
        var topScores = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == server.Guid && s.StartTime >= startPeriod && s.LastSeenTime <= endPeriod)
            .OrderByDescending(s => s.TotalScore)
            .Take(10)
            .Select(s => new TopScore
            {
                PlayerName = s.PlayerName,
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                MapName = s.MapName,
                Timestamp = s.LastSeenTime,
                SessionId = s.SessionId
            })
            .ToListAsync();

        statistics.TopScores = topScores;

        return statistics;
    }

    public async Task<SessionRoundReport?> GetRoundReport(string serverGuid, string mapName, DateTime startTime)
    {
        // Find a representative session for this round
        var representativeSession = await _dbContext.PlayerSessions
            .Include(s => s.Server)
            .Where(s => s.ServerGuid == serverGuid && 
                       s.MapName == mapName && 
                       s.StartTime <= startTime &&
                       s.LastSeenTime >= startTime)
            .FirstOrDefaultAsync();

        if (representativeSession == null)
            return null;

        return await GetRoundReportInternal(serverGuid, mapName, startTime, representativeSession);
    }

    private async Task<SessionRoundReport?> GetRoundReportInternal(string serverGuid, string mapName, DateTime referenceTime, PlayerSession representativeSession)
    {
        // Find the previous session on the same server with a different map (to determine the actual round start)
        var previousMapSession = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == serverGuid &&
                        s.MapName != mapName &&
                        s.StartTime < referenceTime)
            .OrderByDescending(s => s.StartTime)
            .FirstOrDefaultAsync();

        var actualRoundStart = previousMapSession != null
            ? previousMapSession.LastSeenTime // Round starts when the previous map's session ended
            : referenceTime.AddMinutes(-30); // Fallback to 30min buffer

        // Find the next session on the same server with a different map (to determine the actual round end)
        var nextMapSession = await _dbContext.PlayerSessions
            .Where(s => s.ServerGuid == serverGuid &&
                        s.MapName != mapName &&
                        s.StartTime > referenceTime)
            .OrderBy(s => s.StartTime)
            .FirstOrDefaultAsync();

        var actualRoundEnd = nextMapSession != null
            ? nextMapSession.StartTime // Round ends when the next map's session starts
            : referenceTime.AddMinutes(30); // Fallback to 30min buffer

        // Get all sessions in the round (same server and map, within the calculated round boundaries)
        var roundSessions = await _dbContext.PlayerSessions
            .Include(s => s.Server)
            .Where(s => s.ServerGuid == serverGuid &&
                       s.MapName == mapName &&
                       s.StartTime <= actualRoundEnd &&
                       s.LastSeenTime >= actualRoundStart)
            .OrderBy(s => s.StartTime)
            .ToListAsync();

        if (!roundSessions.Any())
            return null;

        // Get all observations for the round with player names
        var roundObservations = await _dbContext.PlayerObservations
            .Include(o => o.Session)
            .Where(o => roundSessions.Select(s => s.SessionId).Contains(o.SessionId))
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

        // Create leaderboard snapshots starting from actual round start
        var leaderboardSnapshots = new List<LeaderboardSnapshot>();
        var currentTime = actualRoundStart; // Start from earliest session time
        
        while (currentTime <= actualRoundEnd)
        {
            // Get the latest score for each player at this time
            var playerScores = roundObservations
                .Where(o => o.Timestamp <= currentTime)
                .GroupBy(o => o.PlayerName)
                .Select(g => {
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
                .Select((x, i) => new LeaderboardEntry
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
                
            leaderboardSnapshots.Add(new LeaderboardSnapshot
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

        return new SessionRoundReport
        {
            Session = new SessionInfo
            {
                SessionId = representativeSession.SessionId,
                PlayerName = representativeSession.PlayerName,
                ServerName = representativeSession.Server.Name,
                ServerGuid = representativeSession.ServerGuid,
                GameId = representativeSession.Server.GameId,
                Kills = representativeSession.TotalKills,
                Deaths = representativeSession.TotalDeaths,
                Score = representativeSession.TotalScore,
                ServerIp = representativeSession.Server.Ip,
                ServerPort = representativeSession.Server.Port
            },
            Round = new RoundReportInfo
            {
                MapName = mapName,
                GameType = representativeSession.GameType ?? "",
                StartTime = actualRoundStart,
                EndTime = actualRoundEnd,
                TotalParticipants = roundSessions.Count,
                IsActive = roundSessions.Any(s => s.IsActive)
            },
            LeaderboardSnapshots = leaderboardSnapshots
        };
    }

    public async Task<ServerInsights> GetServerInsights(string serverName, int daysToAnalyze = 7)
    {
        if (daysToAnalyze > 31)
        {
            throw new ArgumentException("The analysis period cannot exceed 31 days for this insight.");
        }
        
        // Calculate the time period
        var endPeriod = DateTime.UtcNow;
        var startPeriod = endPeriod.AddDays(-daysToAnalyze);

        // Get the server by name
        var server = await _dbContext.Servers
            .AsNoTracking()
            .FirstOrDefaultAsync(s => s.Name == serverName);

        if (server == null)
            return new ServerInsights { ServerName = serverName, StartPeriod = startPeriod, EndPeriod = endPeriod };

        // Create the insights object
        var insights = new ServerInsights
        {
            ServerGuid = server.Guid,
            ServerName = server.Name,
            StartPeriod = startPeriod,
            EndPeriod = endPeriod
        };

        // Use a more efficient raw SQL query that avoids the expensive JOIN
        // Instead, use a subquery to get session IDs first, then filter observations
        var observations = await _dbContext.Database.SqlQueryRaw<PingTimestampData>(@"
            SELECT po.Timestamp, po.Ping
            FROM PlayerObservations po
            WHERE po.SessionId IN (
                SELECT ps.SessionId 
                FROM PlayerSessions ps 
                WHERE ps.ServerGuid = {0}
                AND ps.StartTime <= {2}
                AND ps.LastSeenTime >= {1}
            )
            AND po.Timestamp >= {1} 
            AND po.Timestamp <= {2}",
            server.Guid, startPeriod, endPeriod).ToListAsync();

        var hourlyPings = observations
            .GroupBy(o => o.Timestamp.Hour)
            .Select(g =>
            {
                var orderedPings = g.Select(x => x.Ping).OrderBy(p => p).ToList();
                var count = orderedPings.Count;
                if (count == 0) return null;

                var avg = orderedPings.Average();
                
                var median = (count % 2 == 0)
                    ? (orderedPings[count / 2 - 1] + orderedPings[count / 2]) / 2.0
                    : orderedPings[count / 2];

                var p95Index = (int)Math.Ceiling(0.95 * count) - 1;
                var p95 = orderedPings[p95Index < 0 ? 0 : p95Index];

                return new PingDataPoint
                {
                    Hour = g.Key,
                    AveragePing = Math.Round(avg, 2),
                    MedianPing = Math.Round(median, 2),
                    P95Ping = p95
                };
            })
            .Where(x => x != null)
            .Select(x => x!)
            .OrderBy(x => x.Hour)
            .ToList();


        insights.PingByHour = new PingByHourInsight
        {
            Data = hourlyPings
        };

        return insights;
    }
}