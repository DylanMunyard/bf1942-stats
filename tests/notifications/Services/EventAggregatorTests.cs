using Microsoft.Extensions.Logging;
using NSubstitute;
using notifications.Services;

namespace notifications.tests.Services;

public class EventAggregatorTests
{
    private readonly ILogger<EventAggregator> _logger;
    private readonly EventAggregator _eventAggregator;

    public EventAggregatorTests()
    {
        _logger = Substitute.For<ILogger<EventAggregator>>();
        _eventAggregator = new EventAggregator(_logger);
    }

    [Fact]
    public void Subscribe_RegistersHandlerForEventType()
    {
        // Arrange
        var handlerCalled = false;
        async Task Handler(TestEvent @event, CancellationToken ct)
        {
            handlerCalled = true;
            await Task.CompletedTask;
        }

        // Act
        _eventAggregator.Subscribe<TestEvent>(Handler);

        // Assert
        Assert.False(handlerCalled); // Handler should not be called on subscribe
    }

    [Fact]
    public async Task PublishAsync_CallsRegisteredHandler()
    {
        // Arrange
        var handlerCalled = false;
        var @event = new TestEvent { Data = "test data" };

        async Task Handler(TestEvent e, CancellationToken ct)
        {
            handlerCalled = true;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler);

        // Act
        await _eventAggregator.PublishAsync(@event);

        // Assert
        Assert.True(handlerCalled);
    }

    [Fact]
    public async Task PublishAsync_CallsMultipleHandlersForSameEvent()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var @event = new TestEvent { Data = "test" };

        async Task Handler1(TestEvent e, CancellationToken ct)
        {
            handler1Called = true;
            await Task.CompletedTask;
        }

        async Task Handler2(TestEvent e, CancellationToken ct)
        {
            handler2Called = true;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler1);
        _eventAggregator.Subscribe<TestEvent>(Handler2);

        // Act
        await _eventAggregator.PublishAsync(@event);

        // Assert
        Assert.True(handler1Called);
        Assert.True(handler2Called);
    }

    [Fact]
    public async Task PublishAsync_DoesNotCallUnsubscribedHandler()
    {
        // Arrange
        var handlerCalled = false;
        var @event = new TestEvent { Data = "test" };

        async Task Handler(TestEvent e, CancellationToken ct)
        {
            handlerCalled = true;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler);
        _eventAggregator.Unsubscribe<TestEvent>(Handler);

        // Act
        await _eventAggregator.PublishAsync(@event);

        // Assert
        Assert.False(handlerCalled);
    }

    [Fact]
    public async Task PublishAsync_ThrowsArgumentNullException_WhenEventIsNull()
    {
        // Act & Assert
        await Assert.ThrowsAsync<ArgumentNullException>(() => _eventAggregator.PublishAsync<TestEvent>(null!));
    }

    [Fact]
    public async Task PublishAsync_ContinuesWithOtherHandlers_WhenOneThrows()
    {
        // Arrange
        var handler1Called = false;
        var handler2Called = false;
        var @event = new TestEvent { Data = "test" };

        Task Handler1(TestEvent e, CancellationToken ct)
        {
            handler1Called = true;
            throw new InvalidOperationException("Handler 1 error");
        }

        async Task Handler2(TestEvent e, CancellationToken ct)
        {
            handler2Called = true;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler1);
        _eventAggregator.Subscribe<TestEvent>(Handler2);

        // Act - Synchronous exceptions are caught and logged, execution continues
        await _eventAggregator.PublishAsync(@event);

        // Assert - Both handlers are marked as called despite the exception
        Assert.True(handler1Called);
        Assert.True(handler2Called);
    }

    [Fact]
    public async Task PublishAsync_PassesEventToHandler()
    {
        // Arrange
        var receivedEvent = (TestEvent?)null;
        var @event = new TestEvent { Data = "test data" };

        async Task Handler(TestEvent e, CancellationToken ct)
        {
            receivedEvent = e;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler);

        // Act
        await _eventAggregator.PublishAsync(@event);

        // Assert
        Assert.NotNull(receivedEvent);
        Assert.Equal("test data", receivedEvent.Data);
    }

    [Fact]
    public async Task PublishAsync_PassesCancellationToken()
    {
        // Arrange
        var receivedToken = CancellationToken.None;
        var @event = new TestEvent { Data = "test" };
        var cts = new CancellationTokenSource();

        async Task Handler(TestEvent e, CancellationToken ct)
        {
            receivedToken = ct;
            await Task.CompletedTask;
        }

        _eventAggregator.Subscribe<TestEvent>(Handler);

        // Act
        await _eventAggregator.PublishAsync(@event, cts.Token);

        // Assert
        Assert.Equal(cts.Token, receivedToken);
    }

    [Fact]
    public async Task PublishAsync_LogsWarning_WhenNoHandlersRegistered()
    {
        // Arrange
        var @event = new TestEvent { Data = "test" };

        // Act
        await _eventAggregator.PublishAsync(@event);

        // Assert
        _logger.Received().Log(
            LogLevel.Warning,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    // Test event class
    private class TestEvent
    {
        public string Data { get; set; } = "";
    }
}
