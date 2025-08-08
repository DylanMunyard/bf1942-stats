using System.Collections.Concurrent;

namespace junie_des_1942stats.Notifications.Services;

public interface IEventAggregator
{
    Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class;
    void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
    void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class;
}

public class EventAggregator : IEventAggregator
{
    private readonly ConcurrentDictionary<Type, List<Delegate>> _handlers = new();
    private readonly ILogger<EventAggregator> _logger;

    public EventAggregator(ILogger<EventAggregator> logger)
    {
        _logger = logger;
    }

    public async Task PublishAsync<TEvent>(TEvent @event, CancellationToken cancellationToken = default) where TEvent : class
    {
        if (@event == null) throw new ArgumentNullException(nameof(@event));

        var eventType = typeof(TEvent);
        if (!_handlers.TryGetValue(eventType, out var handlers))
        {
            _logger.LogDebug("No handlers registered for event type {EventType}", eventType.Name);
            return;
        }

        var tasks = new List<Task>();
        foreach (Func<TEvent, CancellationToken, Task> handler in handlers)
        {
            try
            {
                tasks.Add(handler(@event, cancellationToken));
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error executing handler for event type {EventType}", eventType.Name);
            }
        }

        await Task.WhenAll(tasks);
    }

    public void Subscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        var eventType = typeof(TEvent);
        _handlers.AddOrUpdate(
            eventType,
            _ => new List<Delegate> { handler },
            (_, existing) =>
            {
                existing.Add(handler);
                return existing;
            });
    }

    public void Unsubscribe<TEvent>(Func<TEvent, CancellationToken, Task> handler) where TEvent : class
    {
        var eventType = typeof(TEvent);
        if (_handlers.TryGetValue(eventType, out var handlers))
        {
            handlers.Remove(handler);
        }
    }
}
