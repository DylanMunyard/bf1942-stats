using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using notifications.Models;
using notifications.Hubs;
using notifications.Services;
using notifications.Telemetry;
using System.Diagnostics;

namespace notifications.Handlers;

public class PlayerOnlineNotificationHandler
{
    private readonly IBuddyNotificationService _buddyNotificationService;
    private readonly IBuddyApiService _buddyApiService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<PlayerOnlineNotificationHandler> _logger;

    public PlayerOnlineNotificationHandler(
        IBuddyNotificationService buddyNotificationService,
        IBuddyApiService buddyApiService,
        IHubContext<NotificationHub> hubContext,
        ILogger<PlayerOnlineNotificationHandler> logger)
    {
        _buddyNotificationService = buddyNotificationService;
        _buddyApiService = buddyApiService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(PlayerOnlineNotification notification, CancellationToken cancellationToken)
    {
        using var activity = ActivitySources.Events.StartActivity("HandlePlayerOnlineNotification");
        activity?.SetTag("player.name", notification.PlayerName);
        activity?.SetTag("server.name", notification.ServerName);
        activity?.SetTag("server.guid", notification.ServerGuid);
        activity?.SetTag("map.name", notification.MapName);

        try
        {
            _logger.LogInformation("Processing player online notification for {PlayerName} on {ServerName}",
                notification.PlayerName, notification.ServerName);

            // Get users who have this player as a buddy
            var usersToNotify = await _buddyApiService.GetUsersWithBuddy(notification.PlayerName);
            var usersList = usersToNotify.ToList();
            activity?.SetTag("users_to_notify.count", usersList.Count);

            if (!usersList.Any())
            {
                _logger.LogDebug("No users found with {PlayerName} as buddy", notification.PlayerName);
                return;
            }

            var message = new BuddyNotificationMessage
            {
                Type = "buddy_online",
                BuddyName = notification.PlayerName,
                ServerName = notification.ServerName,
                MapName = notification.MapName,
                Timestamp = notification.Timestamp,
                Message = $"{notification.PlayerName} is now online on {notification.ServerName} playing {notification.MapName}"
            };

            var totalNotificationsSent = 0;

            // Send notifications to all connected users who have this buddy
            foreach (var userEmail in usersList)
            {
                var connectionIds = await _buddyNotificationService.GetUserConnectionIds(userEmail);
                foreach (var connectionId in connectionIds)
                {
                    const string eventName = "BuddyOnline";
                    _logger.LogInformation("Sending SignalR event {EventName} to connection {ConnectionId} for user {UserEmail}", eventName, connectionId, userEmail);

                    using var signalRActivity = ActivitySources.SignalR.StartActivity("SendBuddyOnlineNotification");
                    signalRActivity?.SetTag("event.name", eventName);
                    signalRActivity?.SetTag("connection.id", connectionId);
                    signalRActivity?.SetTag("user.email", userEmail);

                    try
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync(eventName, message, cancellationToken);
                        totalNotificationsSent++;
                        _logger.LogInformation("Sent SignalR event {EventName} to connection {ConnectionId}", eventName, connectionId);
                    }
                    catch (Exception ex)
                    {
                        signalRActivity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                        _logger.LogError(ex, "Failed to send SignalR event {EventName} to connection {ConnectionId}", eventName, connectionId);
                    }
                }
            }

            activity?.SetTag("notifications_sent.count", totalNotificationsSent);
            _logger.LogInformation("Sent buddy online notifications to {UserCount} users for {PlayerName}",
                usersList.Count, notification.PlayerName);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error handling player online notification for {PlayerName}", notification.PlayerName);
        }
    }
}