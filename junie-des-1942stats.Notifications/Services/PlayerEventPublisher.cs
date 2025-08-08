using Microsoft.Extensions.Logging;
using StackExchange.Redis;

namespace junie_des_1942stats.Notifications.Services;

public class PlayerEventPublisher : IPlayerEventPublisher
{
    private readonly IDatabase _redis;
    private readonly ILogger<PlayerEventPublisher> _logger;
    private const string StreamKey = "player:events";

    public PlayerEventPublisher(IDatabase redis, ILogger<PlayerEventPublisher> logger)
    {
        _redis = redis;
        _logger = logger;
    }

    public async Task PublishPlayerOnlineEvent(string playerName, string serverGuid, string serverName, string mapName, string gameType, int sessionId)
    {
        try
        {
            var values = new[]
            {
                new NameValueEntry("event_type", "player_online"),
                new NameValueEntry("player_name", playerName),
                new NameValueEntry("server_guid", serverGuid),
                new NameValueEntry("server_name", serverName),
                new NameValueEntry("map_name", mapName),
                new NameValueEntry("game_type", gameType),
                new NameValueEntry("session_id", sessionId.ToString()),
                new NameValueEntry("timestamp", DateTime.UtcNow.ToString("O"))
            };

            var messageId = await _redis.StreamAddAsync(StreamKey, values);
            
            _logger.LogDebug("Published player online event for {PlayerName} on {ServerName} with ID {MessageId}", 
                playerName, serverName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player online event for {PlayerName} on {ServerName}", 
                playerName, serverName);
        }
    }

    public async Task PublishMapChangeEvent(string playerName, string serverGuid, string serverName, string oldMapName, string newMapName, int sessionId)
    {
        try
        {
            var values = new[]
            {
                new NameValueEntry("event_type", "map_change"),
                new NameValueEntry("player_name", playerName),
                new NameValueEntry("server_guid", serverGuid),
                new NameValueEntry("server_name", serverName),
                new NameValueEntry("old_map_name", oldMapName),
                new NameValueEntry("new_map_name", newMapName),
                new NameValueEntry("session_id", sessionId.ToString()),
                new NameValueEntry("timestamp", DateTime.UtcNow.ToString("O"))
            };

            var messageId = await _redis.StreamAddAsync(StreamKey, values);
            
            _logger.LogDebug("Published map change event for {PlayerName} on {ServerName}: {OldMap} -> {NewMap} with ID {MessageId}", 
                playerName, serverName, oldMapName, newMapName, messageId);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing map change event for {PlayerName} on {ServerName}", 
                playerName, serverName);
        }
    }
}