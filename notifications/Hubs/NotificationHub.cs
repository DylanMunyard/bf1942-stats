using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Services;
using junie_des_1942stats.Notifications.Telemetry;
using System.Security.Claims;
using System.Diagnostics;

namespace junie_des_1942stats.Notifications.Hubs;

[Authorize]
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
        using var activity = ActivitySources.SignalR.StartActivity("OnConnectedAsync");

        var userEmail = GetUserEmail();
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("connection.id", Context.ConnectionId);

        if (!string.IsNullOrEmpty(userEmail))
        {
            await _buddyNotificationService.AddUserConnection(userEmail, Context.ConnectionId);
            _logger.LogInformation("User {UserEmail} connected with connection {ConnectionId}", userEmail, Context.ConnectionId);
        }
        else
        {
            activity?.SetStatus(ActivityStatusCode.Error, "User connected without valid email");
            _logger.LogWarning("User connected without valid email: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnConnectedAsync();
    }

    public override async Task OnDisconnectedAsync(Exception? exception)
    {
        using var activity = ActivitySources.SignalR.StartActivity("OnDisconnectedAsync");

        var userEmail = GetUserEmail();
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("connection.id", Context.ConnectionId);
        activity?.SetTag("has_exception", exception != null);

        if (!string.IsNullOrEmpty(userEmail))
        {
            await _buddyNotificationService.RemoveUserConnection(userEmail, Context.ConnectionId);
            _logger.LogInformation("User {UserEmail} disconnected from connection {ConnectionId}", userEmail, Context.ConnectionId);
        }

        if (exception != null)
        {
            activity?.SetStatus(ActivityStatusCode.Error, exception.Message);
            _logger.LogError(exception, "User disconnected due to error: {ConnectionId}", Context.ConnectionId);
        }

        await base.OnDisconnectedAsync(exception);
    }

    private string? GetUserEmail()
    {
        return Context.User?.FindFirst("email")?.Value;
    }
}