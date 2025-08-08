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

    public async Task<IEnumerable<string>> GetUserConnectionIds(int userId)
    {
        try
        {
            var key = UserConnectionsKeyPrefix + userId;
            var connectionIds = await _redis.SetMembersAsync(key);
            return connectionIds.Select(c => c.ToString());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting connection IDs for user {UserId}", userId);
            return Enumerable.Empty<string>();
        }
    }

    public async Task AddUserConnection(int userId, string connectionId)
    {
        try
        {
            var key = UserConnectionsKeyPrefix + userId;
            await _redis.SetAddAsync(key, connectionId);
            // Set expiry to 24 hours to clean up stale connections
            await _redis.KeyExpireAsync(key, TimeSpan.FromHours(24));
            
            _logger.LogDebug("Added connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
    }

    public async Task RemoveUserConnection(int userId, string connectionId)
    {
        try
        {
            var key = UserConnectionsKeyPrefix + userId;
            await _redis.SetRemoveAsync(key, connectionId);
            
            _logger.LogDebug("Removed connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing connection {ConnectionId} for user {UserId}", connectionId, userId);
        }
    }
}