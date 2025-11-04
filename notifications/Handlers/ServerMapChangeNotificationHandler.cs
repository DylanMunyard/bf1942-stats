using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging;
using notifications.Models;
using notifications.Hubs;
using notifications.Services;
using notifications.Telemetry;
using System.Diagnostics;

namespace notifications.Handlers;

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
        using var activity = ActivitySources.Events.StartActivity("HandleServerMapChangeNotification");
        activity?.SetTag("server.name", notification.ServerName);
        activity?.SetTag("server.guid", notification.ServerGuid);
        activity?.SetTag("map.old", notification.OldMapName);
        activity?.SetTag("map.new", notification.NewMapName);
        activity?.SetTag("game.type", notification.GameType);

        try
        {
            _logger.LogInformation("Processing server map change notification for {ServerName}: {OldMap} -> {NewMap}",
                notification.ServerName, notification.OldMapName, notification.NewMapName);

            // Get users who have this server as a favourite
            var usersToNotify = await _buddyApiService.GetUsersWithFavouriteServer(notification.ServerGuid);
            var usersList = usersToNotify.ToList();
            activity?.SetTag("users_to_notify.count", usersList.Count);

            if (!usersList.Any())
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

            var totalNotificationsSent = 0;

            // Send notifications to all connected users who have this server as favourite
            foreach (var userEmail in usersList)
            {
                var connectionIds = await _buddyNotificationService.GetUserConnectionIds(userEmail);
                foreach (var connectionId in connectionIds)
                {
                    const string eventName = "ServerMapChange";
                    _logger.LogInformation("Sending SignalR event {EventName} to connection {ConnectionId} for user {UserEmail}", eventName, connectionId, userEmail);

                    using var signalRActivity = ActivitySources.SignalR.StartActivity("SendServerMapChangeNotification");
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
            _logger.LogInformation("Sent server map change notifications to {UserCount} users for {ServerName}",
                usersList.Count, notification.ServerName);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error handling server map change notification for {ServerName}", notification.ServerName);
        }
    }
}