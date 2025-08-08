using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using junie_des_1942stats.Notifications.Services;
using junie_des_1942stats.Notifications.Models;

namespace junie_des_1942stats.Notifications.Consumers
{
public class PlayerEventConsumer : BackgroundService
{
    private readonly IConnectionMultiplexer _connectionMultiplexer;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerEventConsumer> _logger;
    private const string ChannelName = "player:events";

    public PlayerEventConsumer(
        IConnectionMultiplexer connectionMultiplexer,
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerEventConsumer> logger)
    {
        _connectionMultiplexer = connectionMultiplexer;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Player event consumer starting (Pub/Sub)...");

        ChannelMessageQueue? channelQueue = null;
        var subscriber = _connectionMultiplexer.GetSubscriber();

        try
        {
            channelQueue = await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelName));

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var message = await channelQueue.ReadAsync(stoppingToken);
                    await ProcessPlayerEvent(message.Message);
                }
                catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error processing Pub/Sub message");
                }
            }
         }
         catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
         {
             // normal shutdown
         }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in player event consumer");
        }
        finally
        {
            if (channelQueue != null)
            {
                await channelQueue.UnsubscribeAsync();
            }
        }

        _logger.LogInformation("Player event consumer stopping...");
    }

    private async Task ProcessPlayerEvent(RedisValue jsonMessage)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var eventAggregator = scope.ServiceProvider.GetRequiredService<IEventAggregator>();

            var notification = CreateNotification(jsonMessage);
            if (notification != null)
            {
                _logger.LogDebug("Processing event of type {EventType}", notification.GetType().Name);
                await eventAggregator.PublishAsync(notification);
            }
            else
            {
                _logger.LogWarning("Unknown event type in message: {Message}", jsonMessage.ToString());
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing Pub/Sub message");
        }
    }

    private PlayerEventNotification? CreateNotification(RedisValue jsonMessage)
    {
        try
        {
            using var doc = JsonDocument.Parse(jsonMessage!.ToString());
            var root = doc.RootElement;

            if (!root.TryGetProperty("event_type", out var eventTypeProp))
            {
                return null;
            }

            var eventType = eventTypeProp.GetString() ?? string.Empty;
            var timestamp = DateTime.UtcNow;
            if (root.TryGetProperty("timestamp", out var tsEl) && tsEl.ValueKind == JsonValueKind.String)
            {
                if (DateTime.TryParse(tsEl.GetString(), out var parsed))
                {
                    timestamp = parsed;
                }
            }

            switch (eventType)
            {
                case "player_online":
                    return new PlayerOnlineNotification
                    {
                        PlayerName = root.GetProperty("player_name").GetString() ?? string.Empty,
                        ServerGuid = root.GetProperty("server_guid").GetString() ?? string.Empty,
                        ServerName = root.GetProperty("server_name").GetString() ?? string.Empty,
                        MapName = root.GetProperty("map_name").GetString() ?? string.Empty,
                        GameType = root.GetProperty("game_type").GetString() ?? string.Empty,
                        SessionId = root.TryGetProperty("session_id", out var sidEl) && sidEl.TryGetInt32(out var sid) ? sid : 0,
                        Timestamp = timestamp
                    };
                case "map_change":
                    return new MapChangeNotification
                    {
                        PlayerName = root.GetProperty("player_name").GetString() ?? string.Empty,
                        ServerGuid = root.GetProperty("server_guid").GetString() ?? string.Empty,
                        ServerName = root.GetProperty("server_name").GetString() ?? string.Empty,
                        OldMapName = root.GetProperty("old_map_name").GetString() ?? string.Empty,
                        NewMapName = root.GetProperty("new_map_name").GetString() ?? string.Empty,
                        SessionId = root.TryGetProperty("session_id", out var sidEl2) && sidEl2.TryGetInt32(out var sid2) ? sid2 : 0,
                        Timestamp = timestamp
                    };
                default:
                    return null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to parse Pub/Sub message: {Message}", jsonMessage.ToString());
            return null;
        }
    }
}
}