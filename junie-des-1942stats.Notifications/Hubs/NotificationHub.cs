using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Services;
using System.Security.Claims;

namespace junie_des_1942stats.Notifications.Hubs;

public class NotificationHub : Hub
{
    private readonly IBuddyNotificationService _buddyNotificationService;
    private readonly ILogger<NotificationHub> _logger;

    public NotificationHub(
        IBuddyNotificationService buddyNotificationService,
        ILogger<NotificationHub> logger)
    {
        _buddyNotificationService = buddyNotificationService;
        _logger = logger;
    }

    public override async Task OnConnectedAsync()
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            await _buddyNotificationService.AddUserConnection(userId.Value, Context.ConnectionId);
            _logger.LogInformation("User {UserId} connected with connection {ConnectionId}", userId.Value, Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning("User connected without valid user ID: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userId = GetUserId();
        if (userId.HasValue)
        {
            await _buddyNotificationService.RemoveUserConnection(userId.Value, Context.ConnectionId);
            _logger.LogInformation("User {UserId} disconnected from connection {ConnectionId}", userId.Value, Context.ConnectionId);
        }

        if (exception != null)
        {
            _logger.LogError(exception, "User disconnected due to error: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private int? GetUserId()
    {
        var userIdClaim = Context.User?.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (int.TryParse(userIdClaim, out var userId))
        {
            return userId;
        }
        return null;
    }
}