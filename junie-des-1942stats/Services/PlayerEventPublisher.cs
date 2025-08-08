using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;

namespace junie_des_1942stats.Services;

public class PlayerEventPublisher : IPlayerEventPublisher
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly ILogger<PlayerEventPublisher> _logger;
    private const string ChannelName = "player:events";

    public PlayerEventPublisher(IConnectionMultiplexer connectionMultiplexer, ILogger<PlayerEventPublisher> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _logger = logger;
    }

    public async Task PublishPlayerOnlineEvent(string playerName, string serverGuid, string serverName, string mapName, string gameType, int sessionId)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                event_type = "player_online",
                player_name = playerName,
                server_guid = serverGuid,
                server_name = serverName,
                map_name = mapName,
                game_type = gameType,
                session_id = sessionId,
                timestamp = DateTime.UtcNow
            });

            var subscriber = _connectionMultiplexer.GetSubscriber();
            var receivers = await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload);

            _logger.LogDebug("Published player online event for {PlayerName} on {ServerName} to {Receivers} subscribers", 
                playerName, serverName, receivers);
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
            var payload = JsonSerializer.Serialize(new
            {
                event_type = "map_change",
                player_name = playerName,
                server_guid = serverGuid,
                server_name = serverName,
                old_map_name = oldMapName,
                new_map_name = newMapName,
                session_id = sessionId,
                timestamp = DateTime.UtcNow
            });

            var subscriber = _connectionMultiplexer.GetSubscriber();
            var receivers = await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload);
            
            _logger.LogDebug("Published map change event for {PlayerName} on {ServerName}: {OldMap} -> {NewMap} to {Receivers} subscribers", 
                playerName, serverName, oldMapName, newMapName, receivers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing map change event for {PlayerName} on {ServerName}", 
                playerName, serverName);
        }
    }
}