using Microsoft.Extensions.Logging;
using NSubstitute;
using StackExchange.Redis;
using notifications.Services;
using Xunit;

namespace notifications.tests.Services;

public class BuddyNotificationServiceTests
{
    private readonly IDatabase _redisDatabase;
    private readonly ILogger<BuddyNotificationService> _logger;
    private readonly BuddyNotificationService _service;

    public BuddyNotificationServiceTests()
    {
        _redisDatabase = Substitute.For<IDatabase>();
        _logger = Substitute.For<ILogger<BuddyNotificationService>>();
        _service = new BuddyNotificationService(_redisDatabase, _logger);
    }

    [Fact]
    public async Task GetUserConnectionIds_ReturnsConnectionIds_WhenUserExists()
    {
        // Arrange
        const string userEmail = "test@example.com";
        var connectionIds = new RedisValue[] { "conn1", "conn2", "conn3" };

        _redisDatabase.SetMembersAsync(Arg.Any<RedisKey>())
            .Returns(connectionIds);

        // Act
        var result = await _service.GetUserConnectionIds(userEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(3, result.Count());
        Assert.Contains("conn1", result);
        Assert.Contains("conn2", result);
        Assert.Contains("conn3", result);
    }

    [Fact]
    public async Task GetUserConnectionIds_ReturnsEmptyCollection_WhenUserDoesNotExist()
    {
        // Arrange
        const string userEmail = "test@example.com";
        var emptyArray = Array.Empty<RedisValue>();

        _redisDatabase.SetMembersAsync(Arg.Any<RedisKey>())
            .Returns(emptyArray);

        // Act
        var result = await _service.GetUserConnectionIds(userEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task GetUserConnectionIds_ReturnsEmptyCollection_WhenRedisThrows()
    {
        // Arrange
        const string userEmail = "test@example.com";

        _redisDatabase.SetMembersAsync(Arg.Any<RedisKey>())
            .Returns(Task.FromException<RedisValue[]>(new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis error")));

        // Act
        var result = await _service.GetUserConnectionIds(userEmail);

        // Assert
        Assert.NotNull(result);
        Assert.Empty(result);
    }

    [Fact]
    public async Task AddUserConnection_AddsConnectionToRedis()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(true);
        _redisDatabase.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>())
            .Returns(true);

        // Act
        await _service.AddUserConnection(userEmail, connectionId);

        // Assert
        await _redisDatabase.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains(userEmail)),
            Arg.Is<RedisValue>(v => v.ToString() == connectionId));
    }

    [Fact]
    public async Task AddUserConnection_SetsExpiryTo24Hours()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(true);
        _redisDatabase.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>())
            .Returns(true);

        // Act
        await _service.AddUserConnection(userEmail, connectionId);

        // Assert
        await _redisDatabase.Received(1).KeyExpireAsync(
            Arg.Any<RedisKey>(),
            Arg.Is<TimeSpan?>(ts => ts == TimeSpan.FromHours(24)));
    }

    [Fact]
    public async Task AddUserConnection_HandleRedisException()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis error")));

        // Act & Assert - Should not throw
        await _service.AddUserConnection(userEmail, connectionId);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Fact]
    public async Task RemoveUserConnection_RemovesConnectionFromRedis()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(true);

        // Act
        await _service.RemoveUserConnection(userEmail, connectionId);

        // Assert
        await _redisDatabase.Received(1).SetRemoveAsync(
            Arg.Is<RedisKey>(k => k.ToString().Contains(userEmail)),
            Arg.Is<RedisValue>(v => v.ToString() == connectionId));
    }

    [Fact]
    public async Task RemoveUserConnection_HandleRedisException()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(Task.FromException<bool>(new RedisConnectionException(ConnectionFailureType.SocketFailure, "Redis error")));

        // Act & Assert - Should not throw
        await _service.RemoveUserConnection(userEmail, connectionId);

        _logger.Received().Log(
            LogLevel.Error,
            Arg.Any<EventId>(),
            Arg.Any<Arg.AnyType>(),
            Arg.Any<Exception>(),
            Arg.Any<Func<Arg.AnyType, Exception?, string>>());
    }

    [Fact]
    public async Task GetUserConnectionIds_UsesCorrectRedisKey()
    {
        // Arrange
        const string userEmail = "test@example.com";
        var connectionIds = new RedisValue[] { "conn1" };

        _redisDatabase.SetMembersAsync(Arg.Any<RedisKey>())
            .Returns(connectionIds);

        // Act
        await _service.GetUserConnectionIds(userEmail);

        // Assert
        await _redisDatabase.Received(1).SetMembersAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"user_connections:{userEmail}"));
    }

    [Fact]
    public async Task AddUserConnection_UsesCorrectRedisKey()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetAddAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(true);
        _redisDatabase.KeyExpireAsync(Arg.Any<RedisKey>(), Arg.Any<TimeSpan?>())
            .Returns(true);

        // Act
        await _service.AddUserConnection(userEmail, connectionId);

        // Assert
        await _redisDatabase.Received(1).SetAddAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"user_connections:{userEmail}"),
            Arg.Any<RedisValue>());
    }

    [Fact]
    public async Task RemoveUserConnection_UsesCorrectRedisKey()
    {
        // Arrange
        const string userEmail = "test@example.com";
        const string connectionId = "conn123";

        _redisDatabase.SetRemoveAsync(Arg.Any<RedisKey>(), Arg.Any<RedisValue>())
            .Returns(true);

        // Act
        await _service.RemoveUserConnection(userEmail, connectionId);

        // Assert
        await _redisDatabase.Received(1).SetRemoveAsync(
            Arg.Is<RedisKey>(k => k.ToString() == $"user_connections:{userEmail}"),
            Arg.Any<RedisValue>());
    }

    [Fact]
    public async Task GetUserConnectionIds_ConvertRedisValuesToStrings()
    {
        // Arrange
        const string userEmail = "test@example.com";
        var connectionIds = new RedisValue[] { "abc123", "def456" };

        _redisDatabase.SetMembersAsync(Arg.Any<RedisKey>())
            .Returns(connectionIds);

        // Act
        var result = await _service.GetUserConnectionIds(userEmail);

        // Assert
        var resultArray = result.ToArray();
        Assert.Equal("abc123", resultArray[0]);
        Assert.Equal("def456", resultArray[1]);
    }
}
