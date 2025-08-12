using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.Neo4j.Interfaces;
using junie_des_1942stats.Neo4j.Models;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("api/[controller]")]
public class Neo4jController : ControllerBase
{
    private readonly INeo4jService _neo4jService;
    private readonly ILogger<Neo4jController> _logger;

    public Neo4jController(INeo4jService neo4jService, ILogger<Neo4jController> logger)
    {
        _neo4jService = neo4jService;
        _logger = logger;
    }

    [HttpPost("test-connection")]
    public async Task<IActionResult> TestConnection()
    {
        try
        {
            await _neo4jService.TestConnectionAsync();
            return Ok(new { message = "Neo4j connection successful" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Neo4j connection test failed");
            return StatusCode(500, new { error = "Connection failed", details = ex.Message });
        }
    }

    [HttpPost("initialize")]
    public async Task<IActionResult> Initialize()
    {
        try
        {
            await _neo4jService.InitializeConstraintsAsync();
            return Ok(new { message = "Neo4j constraints initialized" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Neo4j initialization failed");
            return StatusCode(500, new { error = "Initialization failed", details = ex.Message });
        }
    }

    [HttpPost("sync-data")]
    public async Task<IActionResult> SyncLastMonthData()
    {
        try
        {
            await _neo4jService.SyncLastMonthDataAsync();
            return Ok(new { message = "Data sync completed successfully" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Data sync failed");
            return StatusCode(500, new { error = "Data sync failed", details = ex.Message });
        }
    }

    [HttpDelete("clear-data")]
    public async Task<IActionResult> ClearData()
    {
        try
        {
            await _neo4jService.ClearAllDataAsync();
            return Ok(new { message = "All Neo4j data cleared" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Clear data failed");
            return StatusCode(500, new { error = "Clear data failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/server-communities")]
    public async Task<ActionResult<List<PlayerCommunityResult>>> GetServerCommunities()
    {
        try
        {
            var communities = await _neo4jService.GetServerCommunitiesAsync();
            return Ok(communities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get server communities");
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/similar-players")]
    public async Task<ActionResult<List<PlayerSimilarityResult>>> GetSimilarPlayers()
    {
        try
        {
            var similarities = await _neo4jService.GetSimilarPlayersAsync();
            return Ok(similarities);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get similar players");
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/cross-border-battles")]
    public async Task<ActionResult<List<GeographicBattleResult>>> GetCrossBorderBattles()
    {
        try
        {
            var battles = await _neo4jService.GetCrossBorderBattlesAsync();
            return Ok(battles);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get cross-border battles");
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/map-competitiveness")]
    public async Task<ActionResult<List<MapMetaResult>>> GetMapCompetitiveness()
    {
        try
        {
            var mapMeta = await _neo4jService.GetMapCompetitivenessAsync();
            return Ok(mapMeta);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get map competitiveness");
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/player/{playerName}/network")]
    public async Task<ActionResult<Dictionary<string, object>>> GetPlayerNetwork(string playerName)
    {
        try
        {
            var networkStats = await _neo4jService.GetPlayerNetworkStatsAsync(playerName);
            return Ok(networkStats);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player network for {PlayerName}", playerName);
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }

    [HttpGet("analytics/player/{playerName}/recommendations")]
    public async Task<ActionResult<List<string>>> GetPlayerRecommendations(string playerName)
    {
        try
        {
            var recommendations = await _neo4jService.GetPlayerRecommendationsAsync(playerName);
            return Ok(recommendations);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get player recommendations for {PlayerName}", playerName);
            return StatusCode(500, new { error = "Query failed", details = ex.Message });
        }
    }
}