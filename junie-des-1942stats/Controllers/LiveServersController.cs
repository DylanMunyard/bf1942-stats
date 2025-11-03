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
    private readonly PlayersOnlineHistoryService _playersOnlineHistoryService;

    private static readonly string[] ValidGames = ["bf1942", "fh2", "bfvietnam"];

    public LiveServersController(
        IBfListApiService bfListApiService,
        ILogger<LiveServersController> logger,
        PlayerTrackerDbContext dbContext,
        PlayersOnlineHistoryService playersOnlineHistoryService)
    {
        _bfListApiService = bfListApiService;
        _logger = logger;
        _dbContext = dbContext;
        _playersOnlineHistoryService = playersOnlineHistoryService;
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
                Servers = servers ?? [],
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

        // Get active player sessions efficiently (excluding bots) - ALL DATA NOW IN ONE QUERY!
        stepStopwatch.Restart();
        var activeSessions = await _dbContext.PlayerSessions
            .Where(ps => serverGuids.Contains(ps.ServerGuid)
                         && ps.IsActive
                         && ps.LastSeenTime >= oneMinuteAgo
                         && (!ps.Player.AiBot))
            .Include(ps => ps.Player)
            .ToListAsync();
        stepStopwatch.Stop();
        _logger.LogInformation("Step 2 - Active player sessions query completed in {ElapsedMs}ms. Found {SessionCount} sessions WITH ALL DATA",
            stepStopwatch.ElapsedMilliseconds, activeSessions.Count);

        // ELIMINATED: PlayerObservations query - no longer needed!
        _logger.LogInformation("Step 3 - SKIPPED PlayerObservations query - using denormalized data from PlayerSession!");

        // Get current rounds efficiently
        stepStopwatch.Restart();
        var currentRounds = await _dbContext.Rounds
            .Where(r => serverGuids.Contains(r.ServerGuid) && r.IsActive)
            .ToDictionaryAsync(r => r.ServerGuid, r => r);
        stepStopwatch.Stop();
        _logger.LogInformation("Step 4 - Current rounds query completed in {ElapsedMs}ms. Found {RoundCount} active rounds",
            stepStopwatch.ElapsedMilliseconds, currentRounds.Count);

        // Build response by combining the data - NOW MUCH SIMPLER!
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
                Players = serverSessions.Select(session => new PlayerInfo
                {
                    Name = session.PlayerName,
                    Score = session.TotalScore,
                    Kills = session.TotalKills,
                    Deaths = session.TotalDeaths,
                    Ping = session.CurrentPing,      // From denormalized field
                    Team = session.CurrentTeam,      // From denormalized field  
                    TeamLabel = session.CurrentTeamLabel, // From denormalized field
                    AiBot = session.Player?.AiBot ?? false
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
                LastSeenTime = server.LastSeenTime,
                DiscordUrl = server.DiscordUrl,
                ForumUrl = server.ForumUrl,
                GameId = server.GameId
            };
        }).ToList();
        stepStopwatch.Stop();
        _logger.LogInformation("Step 5 - Response building completed in {ElapsedMs}ms (MUCH FASTER - no observation lookups!)", stepStopwatch.ElapsedMilliseconds);

        stepStopwatch.Restart();
        var sortedSummaries = serverSummaries.OrderByDescending(s => s.NumPlayers).ToArray();
        stepStopwatch.Stop();
        totalStopwatch.Stop();

        _logger.LogInformation("Step 6 - Sorting completed in {ElapsedMs}ms", stepStopwatch.ElapsedMilliseconds);
        _logger.LogInformation("OPTIMIZED GetServersFromDatabaseAsync completed. Total time: {TotalMs}ms, returning {ServerCount} servers - ELIMINATED 7+ SECOND QUERY!",
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
                server.DiscordUrl = gameServer.DiscordUrl;
                server.ForumUrl = gameServer.ForumUrl;
                server.GameId = gameServer.GameId;
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
            var history = await _playersOnlineHistoryService.GetPlayersOnlineHistory(game.ToLower(), period.ToLower(), rollingWindowDays);
            return Ok(history);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching players online history for game {Game} with period {Period}", game, period);
            return StatusCode(500, "Internal server error");
        }
    }

}