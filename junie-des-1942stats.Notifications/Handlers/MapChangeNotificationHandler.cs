using MediatR;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Services;

namespace junie_des_1942stats.Notifications.Handlers;

public class MapChangeNotificationHandler : INotificationHandler<MapChangeNotification>
{
    private readonly IBuddyNotificationService _buddyNotificationService;
    private readonly IBuddyApiService _buddyApiService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<MapChangeNotificationHandler> _logger;

    public MapChangeNotificationHandler(
        IBuddyNotificationService buddyNotificationService,
        IBuddyApiService buddyApiService,
        IHubContext<NotificationHub> hubContext,
        ILogger<MapChangeNotificationHandler> logger)
    {
        _buddyNotificationService = buddyNotificationService;
        _buddyApiService = buddyApiService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(MapChangeNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing map change notification for {PlayerName} on {ServerName}: {OldMap} -> {NewMap}",
                notification.PlayerName, notification.ServerName, notification.OldMapName, notification.NewMapName);

            // Get users who have this player as a buddy
            var usersToNotify = await _buddyApiService.GetUsersWithBuddy(notification.PlayerName);

            if (!usersToNotify.Any())
            {
                _logger.LogDebug("No users found with {PlayerName} as buddy", notification.PlayerName);
                return;
            }

            var message = new BuddyNotificationMessage
            {
                Type = "buddy_map_change",
                BuddyName = notification.PlayerName,
                ServerName = notification.ServerName,
                MapName = notification.NewMapName,
                Timestamp = notification.Timestamp,
                Message = $"{notification.PlayerName} switched to {notification.NewMapName} on {notification.ServerName}"
            };

            // Send notifications to all connected users who have this buddy
            foreach (var userId in usersToNotify)
            {
                var connectionIds = await _buddyNotificationService.GetUserConnectionIds(userId);
                foreach (var connectionId in connectionIds)
                {
                    await _hubContext.Clients.Client(connectionId).SendAsync("BuddyMapChange", message, cancellationToken);
                }
            }

            _logger.LogInformation("Sent buddy map change notifications to {UserCount} users for {PlayerName}",
                usersToNotify.Count(), notification.PlayerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling map change notification for {PlayerName}", notification.PlayerName);
        }
    }
}