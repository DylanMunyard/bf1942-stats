using junie_des_1942stats.Bflist;
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
    /// <param name="showAll">If true, show all servers including offline ones. If false (default), show only online servers.</param>
    /// <returns>Server list</returns>
    [HttpGet("{game}/servers")]
    public async Task<ActionResult<ServerListResponse>> GetServers(string game, [FromQuery] bool showAll = false)
    {
        if (!ValidGames.Contains(game.ToLower()))
        {
            return BadRequest($"Invalid game type. Valid types: {string.Join(", ", ValidGames)}");
        }

        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();

        try
        {
            _logger.LogInformation("API REQUEST - Starting GetServers for game {Game}, showAll: {ShowAll}", game, showAll);
            
            stepStopwatch.Restart();
            var servers = await GetServersFromDatabaseAsync(game, showAll);
            stepStopwatch.Stop();
            _logger.LogInformation("API TIMING - Database operations completed in {DatabaseMs}ms. Retrieved {ServerCount} servers", 
                stepStopwatch.ElapsedMilliseconds, servers?.Length ?? 0);

            stepStopwatch.Restart();
            var response = new ServerListResponse
            {
                Servers = servers,
                LastUpdated = DateTime.UtcNow.ToString("O")
            };
            stepStopwatch.Stop();
            
            totalStopwatch.Stop();
            _logger.LogInformation("API TIMING - Response object creation took {ResponseMs}ms", stepStopwatch.ElapsedMilliseconds);
            _logger.LogInformation("API COMPLETE - Total GetServers time: {TotalMs}ms for game {Game}", 
                totalStopwatch.ElapsedMilliseconds, game);

            return Ok(response);
        }
        catch (Exception ex)
        {
            totalStopwatch.Stop();
            _logger.LogError(ex, "API ERROR - Unexpected error fetching all servers for game {Game} after {ElapsedMs}ms", game, totalStopwatch.ElapsedMilliseconds);
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

        var serverIdentifier = $"{ip}:{port}";

        try
        {
            var server = await _bfListApiService.FetchSingleServerSummaryAsync(game, serverIdentifier);
            if (server == null)
            {
                return NotFound($"Server {serverIdentifier} not found");
            }

            // Enrich server with geo location data from database
            var enrichedServers = await EnrichServersWithGeoLocationAsync(new[] { server });
            var enrichedServer = enrichedServers.FirstOrDefault();

            return Ok(enrichedServer ?? server);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch server {ServerIdentifier} from BFList API for game {Game}",
                serverIdentifier, game);
            return StatusCode(502, "Failed to fetch server data from upstream API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching server {ServerIdentifier} for game {Game}",
                serverIdentifier, game);
            return StatusCode(500, "Internal server error");
        }
    }

    private async Task<ServerSummary[]> GetServersFromDatabaseAsync(string game, bool showAll = false)
    {
        var totalStopwatch = System.Diagnostics.Stopwatch.StartNew();
        var stepStopwatch = System.Diagnostics.Stopwatch.StartNew();
        
        _logger.LogInformation("Starting GetServersFromDatabaseAsync for game {Game}, showAll: {ShowAll}", game, showAll);
        
        var oneMinuteAgo = DateTime.UtcNow.AddMinutes(-1);
        
        // Get servers filtering only by online status
        stepStopwatch.Restart();
        var serverQuery = _dbContext.Servers
            .Where(s => s.Game.ToLower() == game.ToLower());

        // Filter by online status unless showing all servers
        if (!showAll)
        {
            serverQuery = serverQuery.Where(s => s.IsOnline);
        }

        var servers = await serverQuery.ToListAsync();
        stepStopwatch.Stop();
        _logger.LogInformation("Step 1 - Servers query completed in {ElapsedMs}ms. Found {ServerCount} servers", 
            stepStopwatch.ElapsedMilliseconds, servers.Count);
        
        if (servers.Count == 0)
        {
            totalStopwatch.Stop();
            _logger.LogInformation("No servers found, returning empty array. Total time: {TotalMs}ms", totalStopwatch.ElapsedMilliseconds);
            return Array.Empty<ServerSummary>();
        }

        var serverGuids = servers.Select(s => s.Guid).ToList();
        _logger.LogDebug("Processing {ServerCount} servers with GUIDs: {ServerGuids}", 
            servers.Count, string.Join(", ", serverGuids.Take(5)) + (serverGuids.Count > 5 ? "..." : ""));

        // Get active player sessions efficiently (excluding bots)
        stepStopwatch.Restart();
        var activeSessions = await _dbContext.PlayerSessions
            .Where(ps => serverGuids.Contains(ps.ServerGuid) 
                         && ps.IsActive 
                         && ps.LastSeenTime >= oneMinuteAgo
                         && (!ps.Player.AiBot))
            .Include(ps => ps.Player)
            .ToListAsync();
        stepStopwatch.Stop();
        _logger.LogInformation("Step 2 - Active player sessions query completed in {ElapsedMs}ms. Found {SessionCount} sessions", 
            stepStopwatch.ElapsedMilliseconds, activeSessions.Count);

        var sessionIds = activeSessions.Select(ps => ps.SessionId).ToList();
        _logger.LogDebug("Found {SessionCount} active sessions", sessionIds.Count);

        // Get latest player observations in a single query
        Dictionary<int, PlayerObservation> latestObservations = new();
        if (sessionIds.Count > 0)
        {
            stepStopwatch.Restart();
            var observations = await _dbContext.PlayerObservations
                .Where(po => sessionIds.Contains(po.SessionId))
                .OrderByDescending(po => po.Timestamp)
                .ToListAsync();
            stepStopwatch.Stop();
            _logger.LogInformation("Step 3 - Player observations query completed in {ElapsedMs}ms. Found {ObservationCount} observations", 
                stepStopwatch.ElapsedMilliseconds, observations.Count);
                
            // Group by SessionId and take the latest (first due to ordering)
            stepStopwatch.Restart();
            latestObservations = observations
                .GroupBy(po => po.SessionId)
                .ToDictionary(g => g.Key, g => g.First());
            stepStopwatch.Stop();
            _logger.LogInformation("Step 3a - Observations grouping completed in {ElapsedMs}ms. Grouped into {GroupCount} latest observations", 
                stepStopwatch.ElapsedMilliseconds, latestObservations.Count);
        }
        else
        {
            _logger.LogInformation("Step 3 - Skipped player observations query (no active sessions)");
        }

        // Get current rounds efficiently
        stepStopwatch.Restart();
        var currentRounds = await _dbContext.Rounds
            .Where(r => serverGuids.Contains(r.ServerGuid) && r.IsActive)
            .ToDictionaryAsync(r => r.ServerGuid, r => r);
        stepStopwatch.Stop();
        _logger.LogInformation("Step 4 - Current rounds query completed in {ElapsedMs}ms. Found {RoundCount} active rounds", 
            stepStopwatch.ElapsedMilliseconds, currentRounds.Count);

        // Build response by combining the data
        stepStopwatch.Restart();
        var serverSummaries = servers.Select(server =>
        {
            var serverSessions = activeSessions.Where(ps => ps.ServerGuid == server.Guid).ToList();
            currentRounds.TryGetValue(server.Guid, out var currentRound);

            return new ServerSummary
            {
                Guid = server.Guid,
                Name = server.Name,
                Ip = server.Ip,
                Port = server.Port,
                NumPlayers = serverSessions.Count,
                MaxPlayers = server.MaxPlayers ?? 64,
                MapName = server.MapName ?? "",
                GameType = currentRound?.GameType ?? "",
                JoinLink = server.JoinLink ?? "",
                RoundTimeRemain = currentRound?.RoundTimeRemain ?? 0,
                Tickets1 = currentRound?.Tickets1 ?? 0,
                Tickets2 = currentRound?.Tickets2 ?? 0,
                Players = serverSessions.Select(session =>
                {
                    latestObservations.TryGetValue(session.SessionId, out var latestObs);
                    
                    return new PlayerInfo
                    {
                        Name = session.PlayerName,
                        Score = latestObs?.Score ?? session.TotalScore,
                        Kills = latestObs?.Kills ?? session.TotalKills,
                        Deaths = latestObs?.Deaths ?? session.TotalDeaths,
                        Ping = latestObs?.Ping ?? 0,
                        Team = latestObs?.Team ?? 1,
                        TeamLabel = latestObs?.TeamLabel ?? "",
                        AiBot = session.Player?.AiBot ?? false
                    };
                }).ToArray(),
                Teams = BuildTeamsFromRound(currentRound),
                Country = server.Country,
                Region = server.Region,
                City = server.City,
                Loc = server.Loc,
                Timezone = server.Timezone,
                Org = server.Org,
                Postal = server.Postal,
                GeoLookupDate = server.GeoLookupDate,
                IsOnline = server.IsOnline,
                LastSeenTime = server.LastSeenTime
            };
        }).ToList();
        stepStopwatch.Stop();
        _logger.LogInformation("Step 5 - Response building completed in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);

        stepStopwatch.Restart();
        var sortedSummaries = serverSummaries.OrderByDescending(s => s.NumPlayers).ToArray();
        stepStopwatch.Stop();
        totalStopwatch.Stop();
        
        _logger.LogInformation("Step 6 - Sorting completed in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);
        _logger.LogInformation("GetServersFromDatabaseAsync completed. Total time: {TotalMs}ms, returning {ServerCount} servers", 
            totalStopwatch.ElapsedMilliseconds, sortedSummaries.Length);

        return sortedSummaries;
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

    private async Task<ServerSummary[]> EnrichServersWithGeoLocationAsync(ServerSummary[] servers)
    {
        if (servers.Length == 0) return servers;

        // Create lookup table for server geo data by GUID
        var serverGuids = servers.Select(s => s.Guid).ToArray();
        var geoData = await _dbContext.Servers
            .Where(gs => serverGuids.Contains(gs.Guid))
            .ToDictionaryAsync(gs => gs.Guid, gs => gs);

        // Enrich servers with geo location data
        foreach (var server in servers)
        {
            if (geoData.TryGetValue(server.Guid, out var gameServer))
            {
                server.Country = gameServer.Country;
                server.Region = gameServer.Region;
                server.City = gameServer.City;
                server.Loc = gameServer.Loc;
                server.Timezone = gameServer.Timezone;
                server.Org = gameServer.Org;
                server.Postal = gameServer.Postal;
                server.GeoLookupDate = gameServer.GeoLookupDate;
            }
        }

        return servers;
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