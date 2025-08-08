using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using MediatR;
using junie_des_1942stats.Notifications.Models;

namespace junie_des_1942stats.Notifications.Consumers;

public class PlayerEventConsumer : BackgroundService
{
    private readonly IDatabase _redis;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<PlayerEventConsumer> _logger;
    private const string StreamKey = "player:events";
    private const string ConsumerGroup = "notifications";
    private const string ConsumerName = "notification-consumer";

    public PlayerEventConsumer(
        IDatabase redis,
        IServiceScopeFactory scopeFactory,
        ILogger<PlayerEventConsumer> logger)
    {
        _redis = redis;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _logger.LogInformation("Player event consumer starting...");

        try
        {
            // Create consumer group if it doesn't exist
            try
            {
                await _redis.StreamCreateConsumerGroupAsync(StreamKey, ConsumerGroup, "0", true);
                _logger.LogInformation("Created consumer group {ConsumerGroup} for stream {StreamKey}", ConsumerGroup, StreamKey);
            }
            catch (RedisServerException ex) when (ex.Message.Contains("BUSYGROUP"))
            {
                _logger.LogDebug("Consumer group {ConsumerGroup} already exists", ConsumerGroup);
            }

            while (!stoppingToken.IsCancellationRequested)
            {
                try
                {
                    var result = await _redis.StreamReadGroupAsync(
                        StreamKey,
                        ConsumerGroup,
                        ConsumerName,
                        ">",
                        10);

                    if (result.Any())
                    {
                        foreach (var entry in result)
                        {
                            await ProcessPlayerEvent(entry);
                            
                            // Acknowledge the message
                            await _redis.StreamAcknowledgeAsync(StreamKey, ConsumerGroup, entry.Id);
                        }
                    }
                }
                catch (Exception ex) when (!stoppingToken.IsCancellationRequested)
                {
                    _logger.LogError(ex, "Error reading from Redis stream");
                    await Task.Delay(TimeSpan.FromSeconds(5), stoppingToken);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Fatal error in player event consumer");
        }

        _logger.LogInformation("Player event consumer stopping...");
    }

    private async Task ProcessPlayerEvent(StreamEntry entry)
    {
        try
        {
            using var scope = _scopeFactory.CreateScope();
            var mediator = scope.ServiceProvider.GetRequiredService<IMediator>();

            var notification = CreateNotification(entry);
            if (notification != null)
            {
                _logger.LogDebug("Processing event {EventId} of type {EventType}", entry.Id, notification.GetType().Name);
                await mediator.Publish(notification);
            }
            else
            {
                _logger.LogWarning("Unknown event type in stream entry {EventId}: {Values}", entry.Id, string.Join(", ", entry.Values));
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing stream entry {EventId}", entry.Id);
        }
    }

    private PlayerEventNotification? CreateNotification(StreamEntry entry)
    {
        var values = entry.Values.ToDictionary(v => v.Name.ToString(), v => v.Value.ToString());

        if (!values.TryGetValue("event_type", out var eventType))
        {
            return null;
        }

        if (!DateTime.TryParse(values.GetValueOrDefault("timestamp", ""), out var timestamp))
        {
            timestamp = DateTime.UtcNow;
        }

        return eventType switch
        {
            "player_online" => new PlayerOnlineNotification
            {
                PlayerName = values.GetValueOrDefault("player_name", ""),
                ServerGuid = values.GetValueOrDefault("server_guid", ""),
                ServerName = values.GetValueOrDefault("server_name", ""),
                MapName = values.GetValueOrDefault("map_name", ""),
                GameType = values.GetValueOrDefault("game_type", ""),
                SessionId = int.TryParse(values.GetValueOrDefault("session_id", "0"), out var sessionId) ? sessionId : 0,
                Timestamp = timestamp
            },
            "map_change" => new MapChangeNotification
            {
                PlayerName = values.GetValueOrDefault("player_name", ""),
                ServerGuid = values.GetValueOrDefault("server_guid", ""),
                ServerName = values.GetValueOrDefault("server_name", ""),
                OldMapName = values.GetValueOrDefault("old_map_name", ""),
                NewMapName = values.GetValueOrDefault("new_map_name", ""),
                SessionId = int.TryParse(values.GetValueOrDefault("session_id", "0"), out var sessionId2) ? sessionId2 : 0,
                Timestamp = timestamp
            },
            _ => null
        };
    }
}