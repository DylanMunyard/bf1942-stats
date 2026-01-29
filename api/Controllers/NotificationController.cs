using api.PlayerTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace api.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class NotificationController(
    PlayerTrackerDbContext dbContext,
    ILogger<NotificationController> logger) : ControllerBase
{

    [HttpGet("users-with-buddy")]
    public async Task<ActionResult<IEnumerable<string>>> GetUsersWithBuddy(string buddyPlayerName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(buddyPlayerName))
            {
                return BadRequest("buddyPlayerName is required");
            }

            logger.LogDebug("Getting users who have {BuddyName} as a buddy", buddyPlayerName);

            var userEmails = await dbContext.UserBuddies
                .Where(ub => ub.BuddyPlayerName == buddyPlayerName)
                .Select(ub => ub.User.Email)
                .ToListAsync();

            logger.LogInformation("Found {Count} users with {BuddyName} as a buddy", userEmails.Count, buddyPlayerName);

            return Ok(userEmails);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
            return StatusCode(500, "Internal server error");
        }
    }

    [HttpGet("users-with-favourite-server")]
    public async Task<ActionResult<IEnumerable<string>>> GetUsersWithFavouriteServer(string serverGuid)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(serverGuid))
            {
                return BadRequest("serverGuid is required");
            }

            logger.LogDebug("Getting users who have server {ServerGuid} as a favourite", serverGuid);

            var userEmails = await dbContext.UserFavoriteServers
                .Where(ufs => ufs.ServerGuid == serverGuid)
                .Select(ufs => ufs.User.Email)
                .ToListAsync();

            logger.LogInformation("Found {Count} users with server {ServerGuid} as a favourite", userEmails.Count, serverGuid);

            return Ok(userEmails);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting users with favourite server {ServerGuid}", serverGuid);
            return StatusCode(500, "Internal server error");
        }
    }
}
