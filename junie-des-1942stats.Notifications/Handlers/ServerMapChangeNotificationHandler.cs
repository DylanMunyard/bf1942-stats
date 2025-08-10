using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Services;

namespace junie_des_1942stats.Notifications.Handlers;

public class ServerMapChangeNotificationHandler
{
    private readonly IBuddyNotificationService _buddyNotificationService;
    private readonly IBuddyApiService _buddyApiService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<ServerMapChangeNotificationHandler> _logger;

    public ServerMapChangeNotificationHandler(
        IBuddyNotificationService buddyNotificationService,
        IBuddyApiService buddyApiService,
        IHubContext<NotificationHub> hubContext,
        ILogger<ServerMapChangeNotificationHandler> logger)
    {
        _buddyNotificationService = buddyNotificationService;
        _buddyApiService = buddyApiService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(ServerMapChangeNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing server map change notification for {ServerName}: {OldMap} -> {NewMap}",
                notification.ServerName, notification.OldMapName, notification.NewMapName);

            // Get users who have this server as a favourite
            var usersToNotify = await _buddyApiService.GetUsersWithFavouriteServer(notification.ServerGuid);

            if (!usersToNotify.Any())
            {
                _logger.LogDebug("No users found with server {ServerGuid} as favourite", notification.ServerGuid);
                return;
            }

            var message = new BuddyNotificationMessage
            {
                Type = "server_map_change",
                ServerName = notification.ServerName,
                MapName = notification.NewMapName,
                JoinLink = notification.JoinLink,
                Timestamp = notification.Timestamp,
                Message = $"Server {notification.ServerName} changed map from {notification.OldMapName} to {notification.NewMapName}"
            };

            // Send notifications to all connected users who have this server as favourite
            foreach (var userEmail in usersToNotify)
            {
                var connectionIds = await _buddyNotificationService.GetUserConnectionIds(userEmail);
                foreach (var connectionId in connectionIds)
                {
                    const string eventName = "ServerMapChange";
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

            _logger.LogInformation("Sent server map change notifications to {UserCount} users for {ServerName}",
                usersToNotify.Count(), notification.ServerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling server map change notification for {ServerName}", notification.ServerName);
        }
    }
}