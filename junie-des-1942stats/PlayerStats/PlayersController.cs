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
    
    // Get server activity by time period (today, this week, month, year, all time)
    [HttpGet("{playerName}/activity")]
    public async Task<ActionResult<ServerActivitySummary>> GetPlayerActivity(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
            
        var activity = await _playerStatsService.GetServerActivityByTimePeriod(playerName);
        
        if (activity.LastSeen == DateTime.MinValue)
            return NotFound($"Player '{playerName}' not found");
            
        return Ok(activity);
    }
    
    // Get server-specific stats
    [HttpGet("{playerName}/servers")]
    public async Task<ActionResult<List<ServerPlayTimeStats>>> GetPlayerServerStats(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
            
        var stats = await _playerStatsService.GetServerPlaytimeStats(playerName);
        
        if (!stats.Any())
            return NotFound($"Player '{playerName}' not found");
            
        return Ok(stats);
    }
    
    // Get weekly stats
    [HttpGet("{playerName}/weekly")]
    public async Task<ActionResult<List<WeeklyPlayTimeStats>>> GetPlayerWeeklyStats(
        string playerName,
        [FromQuery] int weeks = 10)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");
            
        if (weeks < 1 || weeks > 52)
            return BadRequest("Weeks parameter must be between 1 and 52");
            
        var stats = await _playerStatsService.GetWeeklyPlaytimeStats(playerName, weeks);
        
        if (!stats.Any())
            return NotFound($"Player '{playerName}' not found or no data for specified time period");
            
        return Ok(stats);
    }
}