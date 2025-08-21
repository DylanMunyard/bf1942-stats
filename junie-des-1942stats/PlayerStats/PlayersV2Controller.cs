using junie_des_1942stats.PlayerStats.Models;
using junie_des_1942stats.ClickHouse;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.PlayerStats;

/// <summary>
/// V2 Player Stats API Controller focused on progression metrics and delta analysis
/// </summary>
[ApiController]
[Route("stats/v2/[controller]")]
public class PlayersV2Controller : ControllerBase
{
    private readonly PlayerProgressionService _progressionService;
    private readonly ILogger<PlayersV2Controller> _logger;

    public PlayersV2Controller(PlayerProgressionService progressionService, ILogger<PlayersV2Controller> logger)
    {
        _progressionService = progressionService;
        _logger = logger;
    }

    /// <summary>
    /// Get comprehensive progression details for a player
    /// Focuses on deltas, trends, and progression analysis
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Detailed progression analysis with deltas and trends</returns>
    [HttpGet("{playerName}/progression")]
    public async Task<ActionResult<PlayerProgressionDetails>> GetPlayerProgression(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            _logger.LogDebug("Getting progression details for player: {PlayerName}", playerName);
            
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            
            if (progression == null)
            {
                return NotFound($"No progression data found for player '{playerName}'");
            }

            return Ok(progression);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving progression for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving player progression data");
        }
    }

    /// <summary>
    /// Get overall progression summary (key metrics with deltas)
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Overall progression metrics with deltas</returns>
    [HttpGet("{playerName}/progression/summary")]
    public async Task<ActionResult<OverallProgression>> GetProgressionSummary(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            return Ok(progression.OverallProgression);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving progression summary for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving progression summary");
        }
    }

    /// <summary>
    /// Get map-specific progression analysis
    /// Shows how player performance varies and improves across different maps
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <param name="minRounds">Minimum rounds played on a map to include it (default: 5)</param>
    /// <returns>List of map progression details</returns>
    [HttpGet("{playerName}/progression/maps")]
    public async Task<ActionResult<List<MapProgression>>> GetMapProgression(
        string playerName, 
        [FromQuery] int minRounds = 5)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        if (minRounds < 1 || minRounds > 100)
            return BadRequest("Minimum rounds must be between 1 and 100");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            var mapProgressions = progression.MapProgressions
                .Where(m => m.TotalRoundsPlayed >= minRounds)
                .OrderByDescending(m => m.TotalRoundsPlayed)
                .ToList();

            if (!mapProgressions.Any())
            {
                return NotFound($"No map progression data found for player '{playerName}' with minimum {minRounds} rounds");
            }

            return Ok(mapProgressions);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving map progression for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving map progression data");
        }
    }

    /// <summary>
    /// Get performance trajectory analysis
    /// Shows how player performance trends over time with statistical analysis
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Performance trajectory with trend analysis</returns>
    [HttpGet("{playerName}/progression/trajectory")]
    public async Task<ActionResult<PerformanceTrajectory>> GetPerformanceTrajectory(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            return Ok(progression.PerformanceTrajectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving performance trajectory for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving performance trajectory");
        }
    }

    /// <summary>
    /// Get recent activity analysis
    /// Shows playing patterns, activity levels, and recent engagement
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Recent activity analysis</returns>
    [HttpGet("{playerName}/progression/activity")]
    public async Task<ActionResult<RecentActivity>> GetRecentActivity(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            return Ok(progression.RecentActivity);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving recent activity for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving recent activity data");
        }
    }

    /// <summary>
    /// Get comparative metrics analysis
    /// Shows how player performs relative to server averages, global averages, and peer groups
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Comparative performance analysis</returns>
    [HttpGet("{playerName}/progression/comparative")]
    public async Task<ActionResult<ComparativeMetrics>> GetComparativeMetrics(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            return Ok(progression.ComparativeMetrics);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving comparative metrics for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving comparative metrics");
        }
    }

    /// <summary>
    /// Get server-specific ranking progression
    /// Shows ranking changes and trends across different servers
    /// </summary>
    /// <param name="playerName">The player name to analyze</param>
    /// <returns>Server ranking progression details</returns>
    [HttpGet("{playerName}/progression/rankings")]
    public async Task<ActionResult<List<ServerRankingProgression>>> GetServerRankingProgression(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest("Player name cannot be empty");

        try
        {
            var progression = await _progressionService.GetPlayerProgressionAsync(playerName);
            
            if (!progression.ServerRankings.Any())
            {
                return NotFound($"No server ranking data found for player '{playerName}'");
            }

            return Ok(progression.ServerRankings);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving server rankings for player {PlayerName}", playerName);
            return StatusCode(500, "An error occurred while retrieving server ranking data");
        }
    }

    /// <summary>
    /// Get progression insights for multiple players (comparison focused)
    /// Useful for showing how multiple players are progressing relative to each other
    /// </summary>
    /// <param name="playerNames">Comma-separated list of player names</param>
    /// <param name="metric">The metric to compare (killrate, kdratio, score)</param>
    /// <returns>Comparison of progression metrics across players</returns>
    [HttpGet("compare/progression")]
    public async Task<ActionResult<List<PlayerProgressionSummary>>> ComparePlayerProgression(
        [FromQuery] string playerNames,
        [FromQuery] string metric = "killrate")
    {
        if (string.IsNullOrWhiteSpace(playerNames))
            return BadRequest("Player names cannot be empty");

        var validMetrics = new[] { "killrate", "kdratio", "score" };
        if (!validMetrics.Contains(metric.ToLower()))
            return BadRequest($"Invalid metric. Valid options: {string.Join(", ", validMetrics)}");

        var names = playerNames.Split(',', StringSplitOptions.RemoveEmptyEntries)
            .Select(name => name.Trim())
            .Where(name => !string.IsNullOrWhiteSpace(name))
            .Take(10) // Limit to 10 players for performance
            .ToList();

        if (!names.Any())
            return BadRequest("No valid player names provided");

        try
        {
            var comparisons = new List<PlayerProgressionSummary>();

            foreach (var name in names)
            {
                try
                {
                    var progression = await _progressionService.GetPlayerProgressionAsync(name);
                    var summary = new PlayerProgressionSummary
                    {
                        PlayerName = name,
                        OverallProgression = progression.OverallProgression,
                        RecentActivity = progression.RecentActivity,
                        PerformanceRating = progression.ComparativeMetrics.GlobalComparison.KillRateRating // Default to kill rate
                    };
                    
                    comparisons.Add(summary);
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to get progression for player {PlayerName}", name);
                    // Continue with other players
                }
            }

            if (!comparisons.Any())
            {
                return NotFound("No progression data found for any of the specified players");
            }

            // Sort by the requested metric
            comparisons = metric.ToLower() switch
            {
                "kdratio" => comparisons.OrderByDescending(c => c.OverallProgression.CurrentKDRatio).ToList(),
                "score" => comparisons.OrderByDescending(c => c.OverallProgression.CurrentScorePerMinute).ToList(),
                _ => comparisons.OrderByDescending(c => c.OverallProgression.CurrentKillRate).ToList()
            };

            return Ok(comparisons);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error comparing player progressions for players: {PlayerNames}", playerNames);
            return StatusCode(500, "An error occurred while comparing player progressions");
        }
    }
}

/// <summary>
/// Simplified progression summary for comparison purposes
/// </summary>
public class PlayerProgressionSummary
{
    public string PlayerName { get; set; } = "";
    public OverallProgression OverallProgression { get; set; } = new();
    public RecentActivity RecentActivity { get; set; } = new();
    public PlayerPerformanceRating PerformanceRating { get; set; }
}