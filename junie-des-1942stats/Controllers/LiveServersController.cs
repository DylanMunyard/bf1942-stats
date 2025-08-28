using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Services;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.PlayerTracking;
using Microsoft.EntityFrameworkCore;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class LiveServersController : ControllerBase
{
    private readonly IBfListApiService _bfListApiService;
    private readonly ILogger<LiveServersController> _logger;
    private readonly PlayerTrackerDbContext _dbContext;

    private static readonly string[] ValidGames = ["bf1942", "fh2", "bfvietnam"];

    public LiveServersController(
        IBfListApiService bfListApiService,
        ILogger<LiveServersController> logger,
        PlayerTrackerDbContext dbContext)
    {
        _bfListApiService = bfListApiService;
        _logger = logger;
        _dbContext = dbContext;
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
            var servers = await _bfListApiService.FetchAllServerSummariesWithCacheStatusAsync(game);

            // Enrich servers with geo location data from database
            var enrichedServers = await EnrichServersWithGeoLocationAsync(servers);

            var response = new ServerListResponse
            {
                Servers = enrichedServers,
                LastUpdated = DateTime.UtcNow.ToString("O")
            };

            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch all servers from BFList API for game {Game}", game);
            return StatusCode(502, "Failed to fetch server data from upstream API");
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
}