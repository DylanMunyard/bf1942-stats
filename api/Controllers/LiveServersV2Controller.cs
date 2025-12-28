using api.Bflist.Models;
using api.GameTrends;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/v2/liveservers")]
public class LiveServersV2Controller(
    ISqliteGameTrendsService sqliteGameTrendsService,
    ILogger<LiveServersV2Controller> logger) : ControllerBase
{
    private static readonly string[] ValidGames = ["bf1942", "fh2", "bfvietnam"];

    /// <summary>
    /// Get players online history for a specific game using SQLite aggregates.
    /// </summary>
    /// <param name="game">Game type: bf1942, fh2, or bfvietnam</param>
    /// <param name="period">Time period: 1d, 3d, 7d, 1month, 3months, thisyear, alltime (default: 7d)</param>
    /// <param name="rollingWindowDays">Rolling average window size in days (default: 7, min: 3, max: 30)</param>
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
            var history = await sqliteGameTrendsService.GetPlayersOnlineHistoryAsync(
                game.ToLower(),
                period.ToLower(),
                rollingWindowDays);
            return Ok(history);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Unexpected error fetching v2 players online history for game {Game} with period {Period}", game, period);
            return StatusCode(500, "Internal server error");
        }
    }
}
