using api.Caching;
using api.Constants;
using api.PlayerStats;
using api.Players.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.Players;

[ApiController]
[Route("stats/v2/players")]
public class PlayersV2Controller(
    ISqlitePlayerStatsService sqlitePlayerStatsService,
    ICacheService cacheService,
    ILogger<PlayersV2Controller> logger) : ControllerBase
{
    /// <summary>
    /// Gets player best scores from SQLite aggregates.
    /// </summary>
    [HttpGet("{playerName}/best-scores")]
    public async Task<ActionResult<PlayerBestScores>> GetPlayerBestScores(string playerName)
    {
        if (string.IsNullOrWhiteSpace(playerName))
            return BadRequest(ApiConstants.ValidationMessages.PlayerNameEmpty);

        // Use modern URL decoding that preserves + signs
        playerName = Uri.UnescapeDataString(playerName);

        try
        {
            var cacheKey = $"players:v2:best-scores:{playerName}";
            var cached = await cacheService.GetAsync<PlayerBestScores>(cacheKey);
            if (cached != null)
            {
                return Ok(cached);
            }

            var bestScores = await sqlitePlayerStatsService.GetPlayerBestScoresAsync(playerName);
            await cacheService.SetAsync(cacheKey, bestScores, TimeSpan.FromMinutes(10));

            return Ok(bestScores);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error fetching v2 best scores for player {PlayerName}", playerName);
            return StatusCode(500, "An internal server error occurred while retrieving best scores.");
        }
    }
}
