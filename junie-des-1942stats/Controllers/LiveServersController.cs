using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Services;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.ClickHouse.Interfaces;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class LiveServersController : ControllerBase
{
    private readonly IBfListApiService _bfListApiService;
    private readonly ILogger<LiveServersController> _logger;
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly IClickHouseReader _clickHouseReader;

    private static readonly string[] ValidGames = ["bf1942", "fh2", "bfvietnam"];

    public LiveServersController(
        IBfListApiService bfListApiService,
        ILogger<LiveServersController> logger,
        PlayerTrackerDbContext dbContext,
        IClickHouseReader clickHouseReader)
    {
        _bfListApiService = bfListApiService;
        _logger = logger;
        _dbContext = dbContext;
        _clickHouseReader = clickHouseReader;
    }

    /// <summary>
    /// Get all servers for a specific game
    /// </summary>
    /// <param name="game">Game type: bf1942 or fh2</param>
    /// <returns>Server list</returns>
    [HttpGet("{game}/servers")]
    public async Task<ActionResult<ServerListResponse>> GetServers(string game)
    {
        if (!ValidGames.Contains(game.ToLower()))
        {
            return BadRequest($"Invalid game type. Valid types: {string.Join(", ", ValidGames)}");
        }

        try
        {
            var servers = await GetServersFromDatabaseAsync(game);

            var response = new ServerListResponse
            {
                Servers = servers,
                LastUpdated = DateTime.UtcNow.ToString("O")
            };

            return Ok(response);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching all servers for game {Game}", game);
            return StatusCode(500, "Internal server error");
        }
    }

    /// <summary>
    /// Get individual server data for real-time updates
    /// </summary>
    /// <param name="game">Game type: bf1942 or fh2</param>
    /// <param name="ip">Server IP address</param>
    /// <param name="port">Server port number</param>
    /// <returns>Individual server data</returns>
    [HttpGet("{game}/{ip}/{port}")]
    public async Task<ActionResult<ServerSummary>> GetServer(string game, string ip, int port)
    {
        if (!ValidGames.Contains(game.ToLower()))
        {
            return BadRequest($"Invalid game type. Valid types: {string.Join(", ", ValidGames)}");
        }

        if (!IsValidServerDetails(ip, port))
        {
            return BadRequest("Invalid server details. IP must be valid and port must be 1-65535");
        }

        try
        {
            var server = await GetSingleServerFromDatabaseAsync(game, ip, port);
            if (server == null)
            {
                return NotFound($"Server {ip}:{port} not found or not seen recently");
            }

            return Ok(server);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching server {Ip}:{Port} for game {Game}",
                ip, port, game);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<ServerSummary[]> GetServersFromDatabaseAsync(string game)
    {
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        // Get all servers with their related data in a single optimized query
        var serversWithData = await _dbContext.Servers
            .Where(s => s.Game.ToLower() == game.ToLower())
            .Where(s => _dbContext.PlayerSessions
                .Any(ps => ps.ServerGuid == s.Guid && ps.LastSeenTime >= fiveMinutesAgo))
            .Select(server => new {
                Server = server,
                ActiveSessions = _dbContext.PlayerSessions
                    .Where(ps => ps.ServerGuid == server.Guid 
                                 && ps.IsActive 
                                 && ps.LastSeenTime >= oneMinuteAgo)
                    .Select(ps => new {
                        ps.SessionId,
                        ps.PlayerName,
                        ps.TotalScore,
                        ps.TotalKills,
                        ps.TotalDeaths,
                        ps.Player.AiBot,
                        LatestObservation = _dbContext.PlayerObservations
                            .Where(po => po.SessionId == ps.SessionId)
                            .OrderByDescending(po => po.Timestamp)
                            .FirstOrDefault()
                    })
                    .ToList(),
                CurrentRound = _dbContext.Rounds
                    .Where(r => r.ServerGuid == server.Guid && r.IsActive)
                    .FirstOrDefault()
            })
            .ToListAsync();

        var serverSummaries = serversWithData.Select(data => new ServerSummary
        {
            Guid = data.Server.Guid,
            Name = data.Server.Name,
            Ip = data.Server.Ip,
            Port = data.Server.Port,
            NumPlayers = data.ActiveSessions.Count,
            MaxPlayers = data.Server.MaxPlayers ?? 64,
            MapName = data.Server.MapName ?? "",
            GameType = data.CurrentRound?.GameType ?? "",
            JoinLink = data.Server.JoinLink ?? "",
            RoundTimeRemain = data.CurrentRound?.RoundTimeRemain ?? 0,
            Tickets1 = data.CurrentRound?.Tickets1 ?? 0,
            Tickets2 = data.CurrentRound?.Tickets2 ?? 0,
            Players = data.ActiveSessions.Select(session => new PlayerInfo
            {
                Name = session.PlayerName,
                Score = session.LatestObservation?.Score ?? session.TotalScore,
                Kills = session.LatestObservation?.Kills ?? session.TotalKills,
                Deaths = session.LatestObservation?.Deaths ?? session.TotalDeaths,
                Ping = session.LatestObservation?.Ping ?? 0,
                Team = session.LatestObservation?.Team ?? 1,
                TeamLabel = session.LatestObservation?.TeamLabel ?? "",
                AiBot = session.AiBot ?? false
            }).ToArray(),
            Teams = BuildTeamsFromRound(data.CurrentRound),
            Country = data.Server.Country,
            Region = data.Server.Region,
            City = data.Server.City,
            Loc = data.Server.Loc,
            Timezone = data.Server.Timezone,
            Org = data.Server.Org,
            Postal = data.Server.Postal,
            GeoLookupDate = data.Server.GeoLookupDate
        }).ToList();

        return serverSummaries.OrderByDescending(s => s.NumPlayers).ToArray();
    }

    private async Task<ServerSummary?> GetSingleServerFromDatabaseAsync(string game, string ip, int port)
    {
        var fiveMinutesAgo = DateTime.UtcNow.AddMinutes(-5);
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        // Get the specific server with all related data in a single optimized query
        var serverWithData = await _dbContext.Servers
            .Where(s => s.Game.ToLower() == game.ToLower() && s.Ip == ip && s.Port == port)
            .Where(s => _dbContext.PlayerSessions
                .Any(ps => ps.ServerGuid == s.Guid && ps.LastSeenTime >= fiveMinutesAgo))
            .Select(server => new {
                Server = server,
                ActiveSessions = _dbContext.PlayerSessions
                    .Where(ps => ps.ServerGuid == server.Guid 
                                 && ps.IsActive 
                                 && ps.LastSeenTime >= oneMinuteAgo)
                    .Select(ps => new {
                        ps.SessionId,
                        ps.PlayerName,
                        ps.TotalScore,
                        ps.TotalKills,
                        ps.TotalDeaths,
                        ps.Player.AiBot,
                        LatestObservation = _dbContext.PlayerObservations
                            .Where(po => po.SessionId == ps.SessionId)
                            .OrderByDescending(po => po.Timestamp)
                            .FirstOrDefault()
                    })
                    .ToList(),
                CurrentRound = _dbContext.Rounds
                    .Where(r => r.ServerGuid == server.Guid && r.IsActive)
                    .FirstOrDefault()
            })
            .FirstOrDefaultAsync();

        if (serverWithData == null) return null;

        return new ServerSummary
        {
            Guid = serverWithData.Server.Guid,
            Name = serverWithData.Server.Name,
            Ip = serverWithData.Server.Ip,
            Port = serverWithData.Server.Port,
            NumPlayers = serverWithData.ActiveSessions.Count,
            MaxPlayers = serverWithData.Server.MaxPlayers ?? 64,
            MapName = serverWithData.Server.MapName ?? "",
            GameType = serverWithData.CurrentRound?.GameType ?? "",
            JoinLink = serverWithData.Server.JoinLink ?? "",
            RoundTimeRemain = serverWithData.CurrentRound?.RoundTimeRemain ?? 0,
            Tickets1 = serverWithData.CurrentRound?.Tickets1 ?? 0,
            Tickets2 = serverWithData.CurrentRound?.Tickets2 ?? 0,
            Players = serverWithData.ActiveSessions.Select(session => new PlayerInfo
            {
                Name = session.PlayerName,
                Score = session.LatestObservation?.Score ?? session.TotalScore,
                Kills = session.LatestObservation?.Kills ?? session.TotalKills,
                Deaths = session.LatestObservation?.Deaths ?? session.TotalDeaths,
                Ping = session.LatestObservation?.Ping ?? 0,
                Team = session.LatestObservation?.Team ?? 1,
                TeamLabel = session.LatestObservation?.TeamLabel ?? "",
                AiBot = session.AiBot ?? false
            }).ToArray(),
            Teams = BuildTeamsFromRound(serverWithData.CurrentRound),
            Country = serverWithData.Server.Country,
            Region = serverWithData.Server.Region,
            City = serverWithData.Server.City,
            Loc = serverWithData.Server.Loc,
            Timezone = serverWithData.Server.Timezone,
            Org = serverWithData.Server.Org,
            Postal = serverWithData.Server.Postal,
            GeoLookupDate = serverWithData.Server.GeoLookupDate
        };
    }

    private async Task<ServerSummary> BuildServerSummaryAsync(GameServer server)
    {
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        // Get active players from the last minute
        var activeSessions = await _dbContext.PlayerSessions
            .Where(ps => ps.ServerGuid == server.Guid 
                         && ps.IsActive 
                         && ps.LastSeenTime >= oneMinuteAgo)
            .Include(ps => ps.Player)
            .ToListAsync();

        // Get latest observations for each active session
        var sessionIds = activeSessions.Select(s => s.SessionId).ToList();
        var latestObservations = new Dictionary<int, PlayerObservation>();
        
        if (sessionIds.Any())
        {
            var observations = await _dbContext.PlayerObservations
                .Where(po => sessionIds.Contains(po.SessionId))
                .GroupBy(po => po.SessionId)
                .Select(g => g.OrderByDescending(po => po.Timestamp).First())
                .ToListAsync();
            
            latestObservations = observations.ToDictionary(o => o.SessionId);
        }

        // Build player info from sessions and observations
        var players = new List<PlayerInfo>();
        var teams = new List<TeamInfo>();
        var teamTickets = new Dictionary<int, int>();
        
        foreach (var session in activeSessions)
        {
            var observation = latestObservations.GetValueOrDefault(session.SessionId);
            
            var playerInfo = new PlayerInfo
            {
                Name = session.PlayerName,
                Score = observation?.Score ?? session.TotalScore,
                Kills = observation?.Kills ?? session.TotalKills,
                Deaths = observation?.Deaths ?? session.TotalDeaths,
                Ping = observation?.Ping ?? 0,
                Team = observation?.Team ?? 1,
                TeamLabel = observation?.TeamLabel ?? \"\",
                AiBot = session.Player?.AiBot ?? false
            };
            
            players.Add(playerInfo);
        }

        // Get team info from current round if available
        var currentRound = await _dbContext.Rounds
            .Where(r => r.ServerGuid == server.Guid && r.IsActive)
            .FirstOrDefaultAsync();

        if (currentRound != null)
        {
            if (!string.IsNullOrEmpty(currentRound.Team1Label))
            {
                teams.Add(new TeamInfo { Index = 1, Label = currentRound.Team1Label, Tickets = currentRound.Tickets1 ?? 0 });
            }
            if (!string.IsNullOrEmpty(currentRound.Team2Label))
            {
                teams.Add(new TeamInfo { Index = 2, Label = currentRound.Team2Label, Tickets = currentRound.Tickets2 ?? 0 });
            }
        }

        return new ServerSummary
        {
            Guid = server.Guid,
            Name = server.Name,
            Ip = server.Ip,
            Port = server.Port,
            NumPlayers = players.Count,
            MaxPlayers = server.MaxPlayers ?? 64,
            MapName = server.MapName ?? \"\",
            GameType = currentRound?.GameType ?? \"\",
            JoinLink = server.JoinLink ?? \"\",
            RoundTimeRemain = currentRound?.RoundTimeRemain ?? 0,
            Tickets1 = currentRound?.Tickets1 ?? 0,
            Tickets2 = currentRound?.Tickets2 ?? 0,
            Players = players.ToArray(),
            Teams = teams.ToArray(),
            Country = server.Country,
            Region = server.Region,
            City = server.City,
            Loc = server.Loc,
            Timezone = server.Timezone,
            Org = server.Org,
            Postal = server.Postal,
            GeoLookupDate = server.GeoLookupDate
        };
    }

    private static TeamInfo[] BuildTeamsFromRound(Round? currentRound)
    {
        if (currentRound == null) return [];

        var teams = new List<TeamInfo>();
        
        if (!string.IsNullOrEmpty(currentRound.Team1Label))
        {
            teams.Add(new TeamInfo { Index = 1, Label = currentRound.Team1Label, Tickets = currentRound.Tickets1 ?? 0 });
        }
        if (!string.IsNullOrEmpty(currentRound.Team2Label))
        {
            teams.Add(new TeamInfo { Index = 2, Label = currentRound.Team2Label, Tickets = currentRound.Tickets2 ?? 0 });
        }

        return teams.ToArray();
    }

    private static bool IsValidServerDetails(string ip, int port)
    {
        return !string.IsNullOrEmpty(ip) &&
               System.Net.IPAddress.TryParse(ip, out _) &&
               port > 0 && port <= 65535;
    }


    /// <summary>
    /// Get players online history for a specific game
    /// </summary>
    /// <param name="game">Game type: bf1942, fh2, or bfvietnam</param>
    /// <param name="period">Time period: 1d, 3d, or 7d (default: 7d)</param>
    /// <returns>Players online history data</returns>
    /// <summary>
    /// Get players online history for a specific game with trend analysis
    /// </summary>
    /// <param name="game">Game type: bf1942, fh2, or bfvietnam</param>
    /// <param name="period">Time period: 1d, 3d, 7d, 1month, 3months, thisyear, alltime (default: 7d)</param>
    /// <param name="rollingWindowDays">Rolling average window size in days (default: 7, min: 3, max: 30)</param>
    /// <returns>Players online history data with trend insights</returns>
    [HttpGet("{game}/players-online-history")]
    public async Task<ActionResult<PlayersOnlineHistoryResponse>> GetPlayersOnlineHistory(
        string game, 
        [FromQuery] string period = "7d",
        [FromQuery] int rollingWindowDays = 7)
    {
        if (!ValidGames.Contains(game.ToLower()))
        {
            return BadRequest($"Invalid game type. Valid types: {string.Join(", ", ValidGames)}");
        }

        var validPeriods = new[] { "1d", "3d", "7d", "1month", "3months", "thisyear", "alltime" };
        if (!validPeriods.Contains(period.ToLower()))
        {
            return BadRequest($"Invalid period. Valid periods: {string.Join(", ", validPeriods)}");
        }

        if (rollingWindowDays < 3 || rollingWindowDays > 30)
        {
            return BadRequest("Rolling window must be between 3 and 30 days");
        }

        try
        {
            var history = await GetPlayersOnlineHistoryFromClickHouse(game.ToLower(), period.ToLower(), rollingWindowDays);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching players online history for game {Game} with period {Period}", game, period);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<PlayersOnlineHistoryResponse> GetPlayersOnlineHistoryFromClickHouse(string game, string period, int rollingWindowDays)
    {
        var (days, timeInterval, useAllTime) = period switch
        {
            "1d" => (1, "INTERVAL 5 MINUTE", false),
            "3d" => (3, "INTERVAL 30 MINUTE", false),
            "7d" => (7, "INTERVAL 1 HOUR", false),
            "1month" => (30, "INTERVAL 4 HOUR", false),
            "3months" => (90, "INTERVAL 12 HOUR", false),
            "thisyear" => (DateTime.Now.DayOfYear, "INTERVAL 1 DAY", false),
            "alltime" => (0, "INTERVAL 1 DAY", true),
            _ => (7, "INTERVAL 1 HOUR", false)
        };

        var timeCondition = useAllTime 
            ? "" 
            : $"AND timestamp >= now() - INTERVAL {days} DAY";

        var query = $@"
WITH server_bucket_counts AS (
    SELECT 
        toDateTime(toUnixTimestamp(timestamp) - (toUnixTimestamp(timestamp) % {GetIntervalSeconds(timeInterval)})) as time_bucket,
        server_guid,
        AVG(players_online) as avg_players_online
    FROM server_online_counts
    WHERE game = '{game.Replace("'", "''")}'
        {timeCondition}
        AND timestamp < now()
    GROUP BY time_bucket, server_guid
)
SELECT 
    time_bucket,
    ROUND(SUM(avg_players_online)) as total_players
FROM server_bucket_counts
GROUP BY time_bucket
ORDER BY time_bucket
FORMAT TabSeparated";

        var result = await _clickHouseReader.ExecuteQueryAsync(query);
        var dataPoints = new List<PlayersOnlineDataPoint>();

        foreach (var line in result?.Split('\n', StringSplitOptions.RemoveEmptyEntries) ?? [])
        {
            var parts = line.Split('\t');
            if (parts.Length >= 2 &&
                DateTime.TryParse(parts[0], out var timestamp) &&
                int.TryParse(parts[1], out var totalPlayers))
            {
                dataPoints.Add(new PlayersOnlineDataPoint
                {
                    Timestamp = timestamp,
                    TotalPlayers = totalPlayers
                });
            }
        }

        var insights = CalculatePlayerTrendsInsights(dataPoints.ToArray(), period, rollingWindowDays);

        return new PlayersOnlineHistoryResponse
        {
            DataPoints = dataPoints.ToArray(),
            Insights = insights,
            Period = period,
            Game = game,
            LastUpdated = DateTime.UtcNow.ToString("O")
        };
    }

    private static int GetIntervalSeconds(string interval)
    {
        return interval switch
        {
            "INTERVAL 5 MINUTE" => 300,    // 5 minutes
            "INTERVAL 30 MINUTE" => 1800,  // 30 minutes
            "INTERVAL 1 HOUR" => 3600,     // 1 hour
            "INTERVAL 4 HOUR" => 14400,    // 4 hours
            "INTERVAL 12 HOUR" => 43200,   // 12 hours
            "INTERVAL 1 DAY" => 86400,     // 1 day
            _ => 3600                      // Default to 1 hour
        };
    }

    private static PlayerTrendsInsights? CalculatePlayerTrendsInsights(PlayersOnlineDataPoint[] dataPoints, string period, int rollingWindowDays)
    {
        if (dataPoints.Length == 0) return null;

        var totalPlayers = dataPoints.Sum(dp => dp.TotalPlayers);
        var overallAverage = (double)totalPlayers / dataPoints.Length;

        var peakDataPoint = dataPoints.OrderByDescending(dp => dp.TotalPlayers).First();
        var lowestDataPoint = dataPoints.OrderBy(dp => dp.TotalPlayers).First();

        // Calculate percentage change from start to end
        var startValue = dataPoints.First().TotalPlayers;
        var endValue = dataPoints.Last().TotalPlayers;
        var percentageChange = startValue == 0 ? 0 : ((double)(endValue - startValue) / startValue) * 100;

        // Determine trend direction
        var trendDirection = percentageChange switch
        {
            > 5 => "increasing",
            < -5 => "decreasing",
            _ => "stable"
        };

        // Calculate rolling average for longer periods (1month+)
        var rollingAverage = CalculateRollingAverage(dataPoints, period, rollingWindowDays);

        var calculationMethod = GetCalculationMethodDescription(period);
        
        return new PlayerTrendsInsights
        {
            OverallAverage = Math.Round(overallAverage, 2),
            RollingAverage = rollingAverage,
            TrendDirection = trendDirection,
            PercentageChange = Math.Round(percentageChange, 2),
            PeakPlayers = peakDataPoint.TotalPlayers,
            PeakTimestamp = peakDataPoint.Timestamp,
            LowestPlayers = lowestDataPoint.TotalPlayers,
            LowestTimestamp = lowestDataPoint.Timestamp,
            CalculationMethod = calculationMethod
        };
    }

    private static RollingAverageDataPoint[] CalculateRollingAverage(PlayersOnlineDataPoint[] dataPoints, string period, int rollingWindowDays)
    {
        Console.WriteLine($"CalculateRollingAverage called with period='{period}', rollingWindowDays={rollingWindowDays}, dataPoints.Length={dataPoints.Length}");
        
        // Only calculate rolling average for periods of 1month or longer
        if (period is "1d" or "3d" or "7d")
        {
            Console.WriteLine($"Early return: period='{period}' is too short for rolling average calculation");
            return [];
        }

        if (dataPoints.Length < 2)
        {
            Console.WriteLine($"Early return: insufficient data points ({dataPoints.Length})");
            return [];
        }

        Console.WriteLine($"Proceeding with rolling average calculation for period='{period}'");

        var rollingPoints = new List<RollingAverageDataPoint>();
        var rollingWindowTicks = TimeSpan.FromDays(rollingWindowDays).Ticks;

        for (int i = 0; i < dataPoints.Length; i++)
        {
            var currentTimestamp = dataPoints[i].Timestamp;
            var windowStart = currentTimestamp.AddTicks(-rollingWindowTicks);
            
            // Find all data points within the rolling window
            var windowData = dataPoints
                .Where(dp => dp.Timestamp >= windowStart && dp.Timestamp <= currentTimestamp)
                .ToArray();

            if (windowData.Length > 0)
            {
                var average = windowData.Average(dp => dp.TotalPlayers);

                rollingPoints.Add(new RollingAverageDataPoint
                {
                    Timestamp = currentTimestamp,
                    Average = Math.Round(average, 2)
                });
            }
        }

        Console.WriteLine($"Calculated {rollingPoints.Count} rolling average points");
        return rollingPoints.ToArray();
    }

    private static string GetCalculationMethodDescription(string period)
    {
        return period switch
        {
            "1d" => "Data sampled every 5 minutes. Shows real-time player activity with minimal smoothing.",
            "3d" => "Data averaged over 30-minute intervals. Minor smoothing applied to reduce noise.",
            "7d" => "Data averaged over 1-hour intervals. Hourly averages provide clear trend visibility.",
            "1month" => "Data averaged over 4-hour intervals. Each point represents the average player count during a 4-hour period, which may appear lower than peak activity you'd see in shorter timeframes.",
            "3months" => "Data averaged over 12-hour intervals. Each point represents the average player count during a 12-hour period, significantly smoothing out daily peaks and valleys.",
            "thisyear" => "Data averaged over 24-hour intervals. Each point represents the average daily player count, which smooths out all intraday activity patterns.",
            "alltime" => "Data averaged over 24-hour intervals. Each point represents the average daily player count across the entire historical period.",
            _ => "Data is time-bucketed and averaged to provide trend analysis while managing data volume."
        };
    }
}