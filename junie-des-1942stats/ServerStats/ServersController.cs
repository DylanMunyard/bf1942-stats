using junie_des_1942stats.ServerStats.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using System;

namespace junie_des_1942stats.ServerStats;

[ApiController]
[Route("stats/[controller]")]
public class ServersController : ControllerBase
{
    private readonly ServerStatsService _serverStatsService;
    private readonly ILogger<ServersController> _logger;

    public ServersController(ServerStatsService serverStatsService, ILogger<ServersController> logger)
    {
        _serverStatsService = serverStatsService;
        _logger = logger;
    }

    // Get detailed server statistics with optional days parameter
    [HttpGet("{serverName}")]
    public async Task<ActionResult<ServerStatistics>> GetServerStats(
        string serverName,
        [FromQuery] int? days,
        [FromQuery] int? minPlayersForWeighting)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Looking up server statistics for server name: '{ServerName}'", serverName);

        var stats = await _serverStatsService.GetServerStatistics(
            serverName,
            days ?? 7, // Default to 7 days if not specified
            minPlayersForWeighting); // Pass through the weighting parameter

        if (string.IsNullOrEmpty(stats.ServerGuid))
        {
            _logger.LogWarning("Server not found in database: '{ServerName}'", serverName);
            return NotFound($"Server '{serverName}' not found");
        }

        return Ok(stats);
    }

    // Get server rankings with pagination
    [HttpGet("{serverName}/rankings")]
    public async Task<ActionResult<PagedResult<ServerRanking>>> GetServerRankings(
        string serverName,
        [FromQuery] int? year = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        [FromQuery] string? playerName = null,
        [FromQuery] int? minScore = null,
        [FromQuery] int? minKills = null,
        [FromQuery] int? minDeaths = null,
        [FromQuery] double? minKdRatio = null,
        [FromQuery] int? minPlayTimeMinutes = null,
        [FromQuery] string? orderBy = "TotalScore",
        [FromQuery] string? orderDirection = "desc")
    {
        try
        {
            // Use modern URL decoding that preserves + signs
            serverName = Uri.UnescapeDataString(serverName);

            var result = await _serverStatsService.GetServerRankings(
                serverName, year, page, pageSize, playerName,
                minScore, minKills, minDeaths, minKdRatio, minPlayTimeMinutes,
                orderBy, orderDirection);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }


    [HttpGet("{serverName}/insights")]
    public async Task<ActionResult<ServerInsights>> GetServerInsights(
        string serverName,
        [FromQuery] int? days)
    {
        if (string.IsNullOrWhiteSpace(serverName))
            return BadRequest("Server name cannot be empty");

        // Use modern URL decoding that preserves + signs
        serverName = Uri.UnescapeDataString(serverName);

        _logger.LogInformation("Looking up server insights for server name: '{ServerName}' with days: {Days}", serverName, days);

        try
        {
            var insights = await _serverStatsService.GetServerInsights(
                serverName,
                days ?? 7); // Default to 7 days if not specified

            if (string.IsNullOrEmpty(insights.ServerGuid))
            {
                _logger.LogWarning("Server not found: '{ServerName}'", serverName);
                return NotFound($"Server '{serverName}' not found");
            }

            return Ok(insights);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Get all servers with pagination and filtering
    [HttpGet]
    public async Task<ActionResult<PagedResult<ServerBasicInfo>>> GetAllServers(
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 50,
        [FromQuery] string sortBy = "ServerName",
        [FromQuery] string sortOrder = "asc",
        [FromQuery] string? serverName = null,
        [FromQuery] string? gameId = null,
        [FromQuery] string? country = null,
        [FromQuery] string? region = null,
        [FromQuery] bool? hasActivePlayers = null,
        [FromQuery] DateTime? lastActivityFrom = null,
        [FromQuery] DateTime? lastActivityTo = null,
        [FromQuery] int? minTotalPlayers = null,
        [FromQuery] int? maxTotalPlayers = null,
        [FromQuery] int? minActivePlayersLast24h = null,
        [FromQuery] int? maxActivePlayersLast24h = null)
    {
        // Validate parameters
        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 500)
            return BadRequest("Page size must be between 1 and 500");

        // Valid sort fields
        var validSortFields = new[]
        {
            "ServerName", "GameId", "Country", "Region", "TotalPlayersAllTime", "TotalActivePlayersLast24h", "LastActivity"
        };

        if (!validSortFields.Contains(sortBy, StringComparer.OrdinalIgnoreCase))
            return BadRequest($"Invalid sortBy field. Valid options: {string.Join(", ", validSortFields)}");

        if (!new[] { "asc", "desc" }.Contains(sortOrder.ToLower()))
            return BadRequest("Sort order must be 'asc' or 'desc'");

        // Validate filter parameters
        if (minTotalPlayers.HasValue && minTotalPlayers < 0)
            return BadRequest("Minimum total players cannot be negative");

        if (maxTotalPlayers.HasValue && maxTotalPlayers < 0)
            return BadRequest("Maximum total players cannot be negative");

        if (minTotalPlayers.HasValue && maxTotalPlayers.HasValue && minTotalPlayers > maxTotalPlayers)
            return BadRequest("Minimum total players cannot be greater than maximum total players");

        if (minActivePlayersLast24h.HasValue && minActivePlayersLast24h < 0)
            return BadRequest("Minimum active players last 24h cannot be negative");

        if (maxActivePlayersLast24h.HasValue && maxActivePlayersLast24h < 0)
            return BadRequest("Maximum active players last 24h cannot be negative");

        if (minActivePlayersLast24h.HasValue && maxActivePlayersLast24h.HasValue && minActivePlayersLast24h > maxActivePlayersLast24h)
            return BadRequest("Minimum active players last 24h cannot be greater than maximum active players last 24h");

        if (lastActivityFrom.HasValue && lastActivityTo.HasValue && lastActivityFrom > lastActivityTo)
            return BadRequest("LastActivityFrom cannot be greater than LastActivityTo");

        try
        {
            var filters = new ServerFilters
            {
                ServerName = serverName?.Trim(),
                GameId = gameId?.Trim(),
                Country = country?.Trim(),
                Region = region?.Trim(),
                HasActivePlayers = hasActivePlayers,
                LastActivityFrom = lastActivityFrom,
                LastActivityTo = lastActivityTo,
                MinTotalPlayers = minTotalPlayers,
                MaxTotalPlayers = maxTotalPlayers,
                MinActivePlayersLast24h = minActivePlayersLast24h,
                MaxActivePlayersLast24h = maxActivePlayersLast24h
            };

            var result = await _serverStatsService.GetAllServersWithPaging(page, pageSize, sortBy, sortOrder, filters);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // Search servers by name with pagination
    [HttpGet("search")]
    public async Task<ActionResult<PagedResult<ServerBasicInfo>>> SearchServers(
        [FromQuery] string query,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 20)
    {
        if (string.IsNullOrWhiteSpace(query))
            return BadRequest("Search query cannot be empty");

        if (page < 1)
            return BadRequest("Page number must be at least 1");

        if (pageSize < 1 || pageSize > 100)
            return BadRequest("Page size must be between 1 and 100");

        try
        {
            var filters = new ServerFilters
            {
                ServerName = query.Trim()
            };

            var result = await _serverStatsService.GetAllServersWithPaging(
                page, pageSize, "ServerName", "asc", filters);

            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
    }
}