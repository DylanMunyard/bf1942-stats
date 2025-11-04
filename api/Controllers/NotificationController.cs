using junie_des_1942stats.PlayerTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class NotificationController : ControllerBase
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<NotificationController> _logger;

    public NotificationController(PlayerTrackerDbContext dbContext, ILogger<NotificationController> logger)
    {
        _dbContext = dbContext;
        _logger = logger;
    }

    [HttpGet("users-with-buddy")]
    public async Task<ActionResult<IEnumerable<string>>> GetUsersWithBuddy(string buddyPlayerName)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(buddyPlayerName))
            {
                return BadRequest("buddyPlayerName is required");
            }

            _logger.LogInformation("Getting users who have {BuddyName} as a buddy", buddyPlayerName);

            var userEmails = await _dbContext.UserBuddies
                .Where(ub => ub.BuddyPlayerName == buddyPlayerName)
                .Select(ub => ub.User.Email)
                .ToListAsync();

            _logger.LogInformation("Found {Count} users with {BuddyName} as a buddy", userEmails.Count, buddyPlayerName);

            return Ok(userEmails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
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

            _logger.LogInformation("Getting users who have server {ServerGuid} as a favourite", serverGuid);

            var userEmails = await _dbContext.UserFavoriteServers
                .Where(ufs => ufs.ServerGuid == serverGuid)
                .Select(ufs => ufs.User.Email)
                .ToListAsync();

            _logger.LogInformation("Found {Count} users with server {ServerGuid} as a favourite", userEmails.Count, serverGuid);

            return Ok(userEmails);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with favourite server {ServerGuid}", serverGuid);
            return StatusCode(500, "Internal server error");
        }
    }
}