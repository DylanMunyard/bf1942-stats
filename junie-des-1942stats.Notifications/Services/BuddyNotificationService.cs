using Microsoft.Extensions.Logging;
using StackExchange.Redis;

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
        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            var connectionIds = await _redis.SetMembersAsync(key);
            return connectionIds.Select(c => c.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connection IDs for user {UserEmail}", userEmail);
            return Enumerable.Empty<string>();
        }
    }

    public async Task AddUserConnection(string userEmail, string connectionId)
    {
        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            await _redis.SetAddAsync(key, connectionId);
            // Set expiry to 24 hours to clean up stale connections
            await _redis.KeyExpireAsync(key, TimeSpan.FromHours(24));

            _logger.LogDebug("Added connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
    }

    public async Task RemoveUserConnection(string userEmail, string connectionId)
    {
        try
        {
            var key = UserConnectionsKeyPrefix + userEmail;
            await _redis.SetRemoveAsync(key, connectionId);

            _logger.LogDebug("Removed connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing connection {ConnectionId} for user {UserEmail}", connectionId, userEmail);
        }
    }
}