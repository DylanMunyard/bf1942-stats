using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Services;

namespace junie_des_1942stats.Notifications.Handlers;

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
        try
        {
            _logger.LogInformation("Processing player online notification for {PlayerName} on {ServerName}",
                notification.PlayerName, notification.ServerName);

            // Get users who have this player as a buddy
            var usersToNotify = await _buddyApiService.GetUsersWithBuddy(notification.PlayerName);

            if (!usersToNotify.Any())
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

            // Send notifications to all connected users who have this buddy
            foreach (var userEmail in usersToNotify)
            {
                var connectionIds = await _buddyNotificationService.GetUserConnectionIds(userEmail);
                foreach (var connectionId in connectionIds)
                {
                    const string eventName = "BuddyOnline";
                    _logger.LogInformation("Sending SignalR event {EventName} to connection {ConnectionId} for user {UserEmail}", eventName, connectionId, userEmail);
                    try
                    {
                        await _hubContext.Clients.Client(connectionId).SendAsync(eventName, message, cancellationToken);
                        _logger.LogInformation("Sent SignalR event {EventName} to connection {ConnectionId}", eventName, connectionId);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Failed to send SignalR event {EventName} to connection {ConnectionId}", eventName, connectionId);
                    }
                }
            }

            _logger.LogInformation("Sent buddy online notifications to {UserCount} users for {PlayerName}",
                usersToNotify.Count(), notification.PlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling player online notification for {PlayerName}", notification.PlayerName);
        }
    }
}