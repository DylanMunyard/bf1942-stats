using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.PlayerTracking;
using Microsoft.AspNetCore.Mvc;

namespace junie_des_1942stats.PlayerStats;

[ApiController]
[Route("stats/[controller]")]
public class PlayersController : ControllerBase
{
    private readonly PlayerStatsService _playerStatsService;

    public PlayersController(PlayerStatsService playerStatsService)
    {
        _playerStatsService = playerStatsService;
    }
    
    // Get all players with basic info
    [HttpGet]
    public async Task<ActionResult<List<PlayerBasicInfo>>> GetAllPlayers()
    {
        var players = await _playerStatsService.GetAllPlayersBasicInfo();
        return Ok(players);
    }
    
    // Get detailed player statistics
    [HttpGet("{playerName}")]
    public async Task<ActionResult<PlayerTimeStatistics>> GetPlayerStats(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
            
        var stats = await _playerStatsService.GetPlayerStatistics(playerName);
        
        if (stats.TotalSessions == 0)
            return NotFound($"Player '{playerName}' not found");
            
        return Ok(stats);
    }
}