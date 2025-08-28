using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using StackExchange.Redis;
using System.Text.Json;
using junie_des_1942stats.Notifications.Services;
using junie_des_1942stats.Notifications.Models;
using junie_des_1942stats.Notifications.Telemetry;
using System.Diagnostics;

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
            Console.WriteLine($"Player event consumer subscribing to Redis channel '{ChannelName}'");

            // Log Redis connection diagnostics
            var database = _connectionMultiplexer.GetDatabase();
            var server = _connectionMultiplexer.GetServers().FirstOrDefault();
            if (server != null)
            {
                _logger.LogInformation("Redis connection status: Connected={Connected}, Server={Server}, ClientName={ClientName}",
                    _connectionMultiplexer.IsConnected, server.EndPoint, _connectionMultiplexer.ClientName);
            }
            else
            {
                _logger.LogWarning("No Redis servers found in connection multiplexer");
            }

            ChannelMessageQueue? channelQueue = null;
            var subscriber = _connectionMultiplexer.GetSubscriber();

            try
            {
                channelQueue = await subscriber.SubscribeAsync(RedisChannel.Literal(ChannelName));
                Console.WriteLine($"Successfully subscribed to Redis channel '{ChannelName}'");
                _logger.LogInformation("Successfully subscribed to Redis channel '{ChannelName}' - waiting for messages...", ChannelName);

                // Log current subscription count
                var redisServer = _connectionMultiplexer.GetServers().FirstOrDefault();
                if (redisServer != null)
                {
                    var subscriptionCount = await redisServer.SubscriptionSubscriberCountAsync(RedisChannel.Literal(ChannelName));
                    _logger.LogInformation("Current subscriber count for channel '{ChannelName}': {Count}", ChannelName, subscriptionCount);
                }

                var lastHeartbeat = DateTime.UtcNow;
                var messageCount = 0;

                while (!stoppingToken.IsCancellationRequested)
                {
                    using var timeoutCts = new CancellationTokenSource(TimeSpan.FromSeconds(30));
                    try
                    {
                        // Add timeout to ReadAsync to allow periodic heartbeat logging
                        using var combinedCts = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken, timeoutCts.Token);

                        var message = await channelQueue.ReadAsync(combinedCts.Token);
                        messageCount++;
                        _logger.LogInformation("Received Redis message #{MessageCount}", messageCount);
                        _logger.LogDebug("Message content: {RawMessage}", message.Message.ToString());
                        await ProcessPlayerEvent(message.Message);
                        lastHeartbeat = DateTime.UtcNow;
                    }
                    catch (OperationCanceledException) when (timeoutCts.Token.IsCancellationRequested && !stoppingToken.IsCancellationRequested)
                    {
                        // Timeout occurred - log heartbeat
                        var elapsed = DateTime.UtcNow - lastHeartbeat;
                        _logger.LogInformation("Consumer heartbeat - still listening on '{ChannelName}', last message: {ElapsedSeconds}s ago, total messages: {MessageCount}",
                            ChannelName, elapsed.TotalSeconds, messageCount);
                        continue;
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
            using var activity = ActivitySources.Events.StartActivity("ProcessPlayerEvent");
            
            try
            {
                using var scope = _scopeFactory.CreateScope();
                var eventAggregator = scope.ServiceProvider.GetRequiredService<IEventAggregator>();

                var notification = CreateNotification(jsonMessage);
                if (notification != null)
                {
                    var eventType = notification.GetType().Name;
                    activity?.SetTag("event.type", eventType);
                    
                    _logger.LogDebug("Processing event of type {EventType}", eventType);

                    // Publish using the concrete type to ensure proper handler resolution
                    await (notification switch
                    {
                        PlayerOnlineNotification playerOnlineNotification => eventAggregator.PublishAsync(playerOnlineNotification),
                        ServerMapChangeNotification serverMapChangeNotification => eventAggregator.PublishAsync(serverMapChangeNotification),
                        _ => Task.CompletedTask
                    });

                    _logger.LogDebug("Successfully processed {EventType}", eventType);
                }
                else
                {
                    activity?.SetStatus(ActivityStatusCode.Error, "Failed to parse event");
                    _logger.LogWarning("Failed to parse event from Redis message: {Message}", jsonMessage.ToString());
                }
            }
            catch (Exception ex)
            {
                activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
                _logger.LogError(ex, "Error processing Pub/Sub message");
            }
        }

        private object? CreateNotification(RedisValue jsonMessage)
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
                    case "server_map_change":
                        return new ServerMapChangeNotification
                        {
                            ServerGuid = root.GetProperty("server_guid").GetString() ?? string.Empty,
                            ServerName = root.GetProperty("server_name").GetString() ?? string.Empty,
                            OldMapName = root.GetProperty("old_map_name").GetString() ?? string.Empty,
                            NewMapName = root.GetProperty("new_map_name").GetString() ?? string.Empty,
                            GameType = root.GetProperty("game_type").GetString() ?? string.Empty,
                            JoinLink = root.TryGetProperty("join_link", out var joinLinkEl) ? joinLinkEl.GetString() : null,
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