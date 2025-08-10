using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Hubs;
using junie_des_1942stats.Notifications.Services;

namespace junie_des_1942stats.Notifications.Handlers;

public class ServerMapChangeNotificationHandler
{
    private readonly IBuddyNotificationService _buddyNotificationService;
    private readonly IHubContext<NotificationHub> _hubContext;
    private readonly ILogger<ServerMapChangeNotificationHandler> _logger;

    public ServerMapChangeNotificationHandler(
        IBuddyNotificationService buddyNotificationService,
        IHubContext<NotificationHub> hubContext,
        ILogger<ServerMapChangeNotificationHandler> logger)
    {
        _buddyNotificationService = buddyNotificationService;
        _hubContext = hubContext;
        _logger = logger;
    }

    public async Task Handle(ServerMapChangeNotification notification, CancellationToken cancellationToken)
    {
        try
        {
            _logger.LogInformation("Processing server map change notification for {ServerName}: {OldMap} -> {NewMap}",
                notification.ServerName, notification.OldMapName, notification.NewMapName);

            var message = new BuddyNotificationMessage
            {
                Type = "server_map_change",
                ServerName = notification.ServerName,
                MapName = notification.NewMapName,
                JoinLink = notification.JoinLink,
                Timestamp = notification.Timestamp,
                Message = $"Server {notification.ServerName} changed map from {notification.OldMapName} to {notification.NewMapName}"
            };

            // Send notification to all connected users
            const string eventName = "ServerMapChange";
            _logger.LogInformation("Broadcasting SignalR event {EventName} to all clients", eventName);
            try
            {
                await _hubContext.Clients.All.SendAsync(eventName, message, cancellationToken);
                _logger.LogInformation("Broadcasted SignalR event {EventName} to all clients", eventName);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to broadcast SignalR event {EventName}", eventName);
            }

            _logger.LogInformation("Sent server map change notification for {ServerName}", notification.ServerName);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling server map change notification for {ServerName}", notification.ServerName);
        }
    }
}