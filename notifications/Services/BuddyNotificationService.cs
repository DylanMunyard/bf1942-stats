using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using junie_des_1942stats.Notifications.Telemetry;
using System.Diagnostics;

namespace junie_des_1942stats.Notifications.Services;

public class BuddyNotificationService : IBuddyNotificationService
{
    private readonly IDatabase _redis;
    private readonly ILogger<BuddyNotificationService> _logger;
    private const string UserConnectionsKeyPrefix = "user_connections:";

    public BuddyNotificationService(
        IDatabase redis,
        ILogger<BuddyNotificationService> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task<IEnumerable<string>> GetUserConnectionIds(string userEmail)
    {
        using var activity = ActivitySources.Redis.StartActivity("GetUserConnectionIds");
        activity?.SetTag("user.email", userEmail);

        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            activity?.SetTag("redis.key", key);

            var connectionIds = await _redis.SetMembersAsync(key);
            var connections = connectionIds.Select(c => c.ToString()).ToArray();

            activity?.SetTag("connections.count", connections.Length);
            return connections;
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error getting connection IDs for user {UserEmail}", userEmail);
            return Enumerable.Empty<string>();
        }
    }

    public async Task AddUserConnection(string userEmail, string connectionId)
    {
        using var activity = ActivitySources.Redis.StartActivity("AddUserConnection");
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("connection.id", connectionId);

        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            activity?.SetTag("redis.key", key);

            await _redis.SetAddAsync(key, connectionId);
            // Set expiry to 24 hours to clean up stale connections
            await _redis.KeyExpireAsync(key, TimeSpan.FromHours(24));

            _logger.LogDebug("Added connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error adding connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
    }

    public async Task RemoveUserConnection(string userEmail, string connectionId)
    {
        using var activity = ActivitySources.Redis.StartActivity("RemoveUserConnection");
        activity?.SetTag("user.email", userEmail);
        activity?.SetTag("connection.id", connectionId);

        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            activity?.SetTag("redis.key", key);

            await _redis.SetRemoveAsync(key, connectionId);

            _logger.LogDebug("Removed connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            _logger.LogError(ex, "Error removing connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
    }
}