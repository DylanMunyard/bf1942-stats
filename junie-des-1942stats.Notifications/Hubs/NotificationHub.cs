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
        var userEmail = GetUserEmail();
        if (!string.IsNullOrEmpty(userEmail))
        {
            await _buddyNotificationService.AddUserConnection(userEmail, Context.ConnectionId);
            _logger.LogInformation("User {UserEmail} connected with connection {ConnectionId}", userEmail, Context.ConnectionId);
        }
        else
        {
            _logger.LogWarning("User connected without valid email: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        var userEmail = GetUserEmail();
        if (!string.IsNullOrEmpty(userEmail))
        {
            await _buddyNotificationService.RemoveUserConnection(userEmail, Context.ConnectionId);
            _logger.LogInformation("User {UserEmail} disconnected from connection {ConnectionId}", userEmail, Context.ConnectionId);
        }

        if (exception != null)
        {
            _logger.LogError(exception, "User disconnected due to error: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string? GetUserEmail()
    {
        return Context.User?.FindFirst("email")?.Value;
    }
}