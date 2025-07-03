using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Caching;
using junie_des_1942stats.Services;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class LiveServersController : ControllerBase
{
    private readonly IBfListApiService _bfListApiService;
    private readonly ICacheService _cacheService;
    private readonly ILogger<LiveServersController> _logger;
    
    private static readonly string[] ValidGames = ["bf1942", "fh2"];
    private const int DefaultPageSize = 100;
    private const int MaxPageSize = 1000;
    private const int ServerListCacheSeconds = 12; // 12 seconds to allow for 15s refresh with buffer
    private const int SingleServerCacheSeconds = 8; // 8 seconds for individual server updates
    
    public LiveServersController(
        IBfListApiService bfListApiService,
        ICacheService cacheService,
        ILogger<LiveServersController> logger)
    {
        _bfListApiService = bfListApiService;
        _cacheService = cacheService;
        _logger = logger;
    }

    /// <summary>
    /// Get all servers for a specific game with intelligent caching
    /// </summary>
    /// <param name="game">Game type: bf1942 or fh2</param>
    /// <param name="perPage">Number of servers per page (default: 100, max: 1000)</param>
    /// <param name="cursor">Pagination cursor</param>
    /// <param name="after">After parameter for pagination</param>
    /// <returns>Server list with caching metadata</returns>
    [HttpGet("{game}/servers")]
    public async Task<ActionResult<ServerListResponse>> GetServers(
        string game,
        [FromQuery] int perPage = DefaultPageSize,
        [FromQuery] string? cursor = null,
        [FromQuery] string? after = null)
    {
        if (!ValidGames.Contains(game.ToLower()))
        {
            return BadRequest($"Invalid game type. Valid types: {string.Join(", ", ValidGames)}");
        }

        perPage = Math.Min(perPage, MaxPageSize);
        
        // Create cache key based on all parameters
        var cacheKey = $"servers:{game}:{perPage}:{cursor ?? ""}:{after ?? ""}";
        
        try
        {
            // Try to get from cache first
            var cachedResponse = await _cacheService.GetAsync<ServerListResponse>(cacheKey);
            if (cachedResponse != null)
            {
                _logger.LogDebug("Cache hit for game {Game}, perPage {PerPage}", game, perPage);
                cachedResponse.CacheHit = true;
                return Ok(cachedResponse);
            }

            // Cache miss - fetch from BFList API
            _logger.LogDebug("Cache miss for game {Game}, perPage {PerPage}", game, perPage);
            
            var servers = await _bfListApiService.FetchServersAsync(game, perPage, cursor, after);
            
            var response = new ServerListResponse
            {
                Servers = servers,
                LastUpdated = DateTime.UtcNow.ToString("O"),
                CacheHit = false
            };

            // Cache the response
            await _cacheService.SetAsync(cacheKey, response, TimeSpan.FromSeconds(ServerListCacheSeconds));
            
            return Ok(response);
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Failed to fetch servers from BFList API for game {Game}", game);
            return StatusCode(502, "Failed to fetch server data from upstream API");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error fetching servers for game {Game}", game);
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
        var cacheKey = $"server:{game}:{serverIdentifier}";
        
        try
        {
            // Try cache first
            var cachedServer = await _cacheService.GetAsync<ServerSummary>(cacheKey);
            if (cachedServer != null)
            {
                _logger.LogDebug("Cache hit for server {Game}:{ServerIdentifier}", game, serverIdentifier);
                return Ok(cachedServer);
            }

            // Cache miss - fetch from BFList API
            _logger.LogDebug("Cache miss for server {Game}:{ServerIdentifier}", game, serverIdentifier);
            
            var server = await _bfListApiService.FetchSingleServerAsync(game, serverIdentifier);
            if (server == null)
            {
                return NotFound($"Server {serverIdentifier} not found");
            }

            // Cache the response
            await _cacheService.SetAsync(cacheKey, server, TimeSpan.FromSeconds(SingleServerCacheSeconds));
            
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