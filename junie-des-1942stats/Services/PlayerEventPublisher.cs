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

            if (receivers == 0)
            {
                Console.WriteLine($"WARNING: Published player online event for {playerName} but no subscribers are listening to channel '{ChannelName}'");
            }
            _logger.LogDebug("Published player online event for {PlayerName} on {ServerName} to {Receivers} subscribers",
                playerName, serverName, receivers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing player online event for {PlayerName} on {ServerName}",
                playerName, serverName);
        }
    }

    public async Task PublishServerMapChangeEvent(string serverGuid, string serverName, string oldMapName, string newMapName, string gameType, string? joinLink)
    {
        try
        {
            var payload = JsonSerializer.Serialize(new
            {
                event_type = "server_map_change",
                server_guid = serverGuid,
                server_name = serverName,
                old_map_name = oldMapName,
                new_map_name = newMapName,
                game_type = gameType,
                join_link = joinLink,
                timestamp = DateTime.UtcNow
            });

            var subscriber = _connectionMultiplexer.GetSubscriber();
            var receivers = await subscriber.PublishAsync(RedisChannel.Literal(ChannelName), payload);

            _logger.LogDebug("Published server map change event for {ServerName}: {OldMap} -> {NewMap} to {Receivers} subscribers",
                serverName, oldMapName, newMapName, receivers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing server map change event for {ServerName}",
                serverName);
        }
    }
}