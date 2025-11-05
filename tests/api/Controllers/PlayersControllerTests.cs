using api.ClickHouse;
using api.Players;
using api.Players.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace api.tests.Controllers;

public class PlayersControllerTests
{
    private readonly IPlayerStatsService _playerStatsService;
    private readonly IServerStatisticsService _serverStatisticsService;
    private readonly IPlayerComparisonService _playerComparisonService;
    private readonly PlayerRoundsReadService _playerRoundsService;
    private readonly PlayersController _controller;

    public PlayersControllerTests()
    {
        // Mock interfaces
        _playerStatsService = Substitute.For<IPlayerStatsService>();
        _serverStatisticsService = Substitute.For<IServerStatisticsService>();
        _playerComparisonService = Substitute.For<IPlayerComparisonService>();

        // Create dependencies for PlayerRoundsReadService
        var httpClient = new HttpClient();
        var playerRoundsLogger = Substitute.For<ILogger<PlayerRoundsReadService>>();
        var serviceProvider = Substitute.For<IServiceProvider>();

        // Create PlayerRoundsReadService with mock dependencies
        _playerRoundsService = new PlayerRoundsReadService(
            httpClient,
            "http://localhost:8123",
            playerRoundsLogger,
            serviceProvider);

        var logger = Substitute.For<ILogger<PlayersController>>();
        _controller = new PlayersController(
            _playerStatsService,
            _serverStatisticsService,
            _playerComparisonService,
            _playerRoundsService,
            logger);
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsOkResult_WithPlayersList()
    {
        // Arrange
        var mockPlayers = new PagedResult<PlayerBasicInfo>
        {
            Items =
            [
                new() { PlayerName = "Player1", TotalPlayTimeMinutes = 100, IsActive = true, LastSeen = DateTime.UtcNow }
            ],
            Page = 1,
            TotalItems = 1,
            TotalPages = 1
        };

        _playerStatsService.GetAllPlayersWithPaging(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PlayerFilters>())
            .Returns(Task.FromResult(mockPlayers));

        // Act
        var result = await _controller.GetAllPlayers();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<PagedResult<PlayerBasicInfo>>(okResult.Value);
        Assert.Single(returnedData.Items);
        Assert.Equal("Player1", returnedData.Items.First().PlayerName);
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsBadRequest_WhenPageIsLessThanOne()
    {
        // Act
        var result = await _controller.GetAllPlayers(page: 0);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsBadRequest_WhenPageSizeExceedsMaximum()
    {
        // Act
        var result = await _controller.GetAllPlayers(pageSize: 10000);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllPlayers_ReturnsBadRequest_WithInvalidSortField()
    {
        // Act
        var result = await _controller.GetAllPlayers(sortBy: "InvalidField");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerStats_ReturnsOkResult_WithPlayerDetails()
    {
        // Arrange
        const string playerName = "TestPlayer";
        var mockStats = new PlayerTimeStatistics
        {
            TotalPlayTimeMinutes = 500,
            IsActive = true,
            Servers = [],
            Insights = new PlayerInsights { ServerRankings = [] },
            RecentStats = new RecentStats()
        };

        _playerStatsService.GetPlayerStatistics(playerName)
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetPlayerStats(playerName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<PlayerTimeStatistics>(okResult.Value);
        Assert.Equal(500, returnedData.TotalPlayTimeMinutes);
    }

    [Fact]
    public async Task GetPlayerStats_ReturnsBadRequest_WhenPlayerNameIsEmpty()
    {
        // Act
        var result = await _controller.GetPlayerStats("");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerStats_ReturnsBadRequest_WhenPlayerNameIsNull()
    {
        // Act
        var result = await _controller.GetPlayerStats(null!);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerStats_DecodesUrlEncodedPlayerName()
    {
        // Arrange
        const string encodedPlayerName = "Test%20Player";
        const string decodedPlayerName = "Test Player";
        var mockStats = new PlayerTimeStatistics
        {
            TotalPlayTimeMinutes = 100,
            IsActive = true,
            Servers = [],
            Insights = new PlayerInsights { ServerRankings = [] },
            RecentStats = new RecentStats()
        };

        _playerStatsService.GetPlayerStatistics(decodedPlayerName)
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetPlayerStats(encodedPlayerName);

        // Assert
        Assert.IsType<OkObjectResult>(result.Result);
        await _playerStatsService.Received(1).GetPlayerStatistics(decodedPlayerName);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsOkResult_WithMatchingPlayers()
    {
        // Arrange
        const string query = "Test";
        var mockResults = new PagedResult<PlayerBasicInfo>
        {
            Items =
            [
                new() { PlayerName = "TestPlayer", TotalPlayTimeMinutes = 200, IsActive = true }
            ],
            Page = 1,
            TotalItems = 1,
            TotalPages = 1
        };

        _playerStatsService.GetAllPlayersWithPaging(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<PlayerFilters>())
            .Returns(Task.FromResult(mockResults));

        // Act
        var result = await _controller.SearchPlayers(query);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<PagedResult<PlayerBasicInfo>>(okResult.Value);
        Assert.Single(returnedData.Items);
        Assert.Equal("TestPlayer", returnedData.Items.First().PlayerName);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WhenQueryIsEmpty()
    {
        // Act
        var result = await _controller.SearchPlayers("");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WhenQueryIsNull()
    {
        // Act
        var result = await _controller.SearchPlayers(null!);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchPlayers_ReturnsBadRequest_WithInvalidPage()
    {
        // Act
        var result = await _controller.SearchPlayers("test", page: 0);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetPlayerStats_EnsuresRecentStatsNotNull()
    {
        // Arrange
        const string playerName = "TestPlayer";
        var mockStats = new PlayerTimeStatistics
        {
            TotalPlayTimeMinutes = 500,
            IsActive = true,
            Servers = [],
            Insights = new PlayerInsights { ServerRankings = [] },
            RecentStats = null
        };

        _playerStatsService.GetPlayerStatistics(playerName)
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetPlayerStats(playerName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<PlayerTimeStatistics>(okResult.Value);
        Assert.NotNull(returnedData.RecentStats);
    }

    [Fact]
    public async Task GetPlayerStats_AppliesServerRankings()
    {
        // Arrange
        const string playerName = "TestPlayer";
        var serverGuid = "server-guid-123";

        var mockStats = new PlayerTimeStatistics
        {
            TotalPlayTimeMinutes = 500,
            IsActive = true,
            Servers =
            [
                new() { ServerGuid = serverGuid, ServerName = "TestServer", TotalMinutes = 100 }
            ],
            Insights = new PlayerInsights
            {
                ServerRankings =
                [
                    new() { ServerGuid = serverGuid, Rank = 5, TotalRankedPlayers = 100 }
                ]
            },
            RecentStats = new RecentStats()
        };

        _playerStatsService.GetPlayerStatistics(playerName)
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetPlayerStats(playerName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<PlayerTimeStatistics>(okResult.Value);
        var ranking = returnedData.Servers.First().Ranking;
        Assert.NotNull(ranking);
        Assert.Equal(5, ranking.Rank);
    }
}
