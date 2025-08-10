using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Services;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class LiveServersController : ControllerBase
{
    private readonly IBfListApiService _bfListApiService;
    private readonly ILogger<LiveServersController> _logger;

    private static readonly string[] ValidGames = ["bf1942", "fh2", "bfvietnam"];

    public LiveServersController(
        IBfListApiService bfListApiService,
        ILogger<LiveServersController> logger)
    {
        _bfListApiService = bfListApiService;
        _logger = logger;
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
            var servers = await _bfListApiService.FetchAllServerSummariesAsync(game);

            var response = new ServerListResponse
            {
                Servers = servers,
                LastUpdated = DateTime.UtcNow.ToString("O"),
                CacheHit = false // This is now handled internally by the service
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

            return Ok(server);
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
}