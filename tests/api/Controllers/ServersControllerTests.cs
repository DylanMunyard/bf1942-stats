using api.PlayerStats.Models;
using api.ServerStats;
using api.ServerStats.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using NSubstitute;
using Xunit;

namespace api.tests.Controllers;

public class ServersControllerTests
{
    private readonly IServerStatsService _serverStatsService;
    private readonly ILogger<ServersController> _logger;
    private readonly ServersController _controller;

    public ServersControllerTests()
    {
        _serverStatsService = Substitute.For<IServerStatsService>();
        _logger = Substitute.For<ILogger<ServersController>>();

        _controller = new ServersController(_serverStatsService, _logger);
    }

    [Fact]
    public async Task GetServerStats_ReturnsOkResult_WithServerStatistics()
    {
        // Arrange
        const string serverName = "TestServer";
        var mockStats = new ServerStatistics
        {
            ServerGuid = "guid-123",
            ServerName = serverName
        };

        _serverStatsService.GetServerStatistics(serverName, Arg.Any<int>())
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetServerStats(serverName, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<ServerStatistics>(okResult.Value);
        Assert.Equal(serverName, returnedData.ServerName);
        Assert.Equal("guid-123", returnedData.ServerGuid);
    }

    [Fact]
    public async Task GetServerStats_ReturnsBadRequest_WhenServerNameIsEmpty()
    {
        // Act
        var result = await _controller.GetServerStats("", null);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetServerStats_ReturnsBadRequest_WhenServerNameIsNull()
    {
        // Act
        var result = await _controller.GetServerStats(null!, null);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetServerStats_ReturnsNotFound_WhenServerDoesNotExist()
    {
        // Arrange
        const string serverName = "NonExistentServer";
        var mockStats = new ServerStatistics
        {
            ServerGuid = "",
            ServerName = serverName
        };

        _serverStatsService.GetServerStatistics(serverName, Arg.Any<int>())
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetServerStats(serverName, null);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task GetServerStats_DecodesUrlEncodedServerName()
    {
        // Arrange
        const string encodedServerName = "Test%20Server";
        const string decodedServerName = "Test Server";
        var mockStats = new ServerStatistics
        {
            ServerGuid = "guid-123",
            ServerName = decodedServerName
        };

        _serverStatsService.GetServerStatistics(decodedServerName, Arg.Any<int>())
            .Returns(Task.FromResult(mockStats));

        // Act
        var result = await _controller.GetServerStats(encodedServerName, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<ServerStatistics>(okResult.Value);
        Assert.Equal(decodedServerName, returnedData.ServerName);
    }

    [Fact]
    public async Task GetAllServers_ReturnsOkResult_WithServersList()
    {
        // Arrange
        var mockServers = new api.ServerStats.Models.PagedResult<ServerBasicInfo>
        {
            Items =
            [
                new() { ServerName = "Server1", Country = "US" }
            ],
            CurrentPage = 1,
            TotalItems = 1,
            TotalPages = 1
        };

        _serverStatsService.GetAllServersWithPaging(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ServerFilters>())
            .Returns(Task.FromResult(mockServers));

        // Act
        var result = await _controller.GetAllServers();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<api.ServerStats.Models.PagedResult<ServerBasicInfo>>(okResult.Value);
        Assert.Single(returnedData.Items);
        Assert.Equal("Server1", returnedData.Items.First().ServerName);
    }

    [Fact]
    public async Task GetAllServers_ReturnsBadRequest_WhenPageIsLessThanOne()
    {
        // Act
        var result = await _controller.GetAllServers(page: 0);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllServers_ReturnsBadRequest_WhenPageSizeExceedsMaximum()
    {
        // Act
        var result = await _controller.GetAllServers(pageSize: 10000);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllServers_ReturnsBadRequest_WithInvalidSortField()
    {
        // Act
        var result = await _controller.GetAllServers(sortBy: "InvalidField");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllServers_ReturnsBadRequest_WithInvalidGame()
    {
        // Act
        var result = await _controller.GetAllServers(game: "InvalidGame");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetAllServers_ReturnsBadRequest_WhenMinPlayersGreaterThanMax()
    {
        // Act
        var result = await _controller.GetAllServers(minTotalPlayers: 100, maxTotalPlayers: 50);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchServers_ReturnsOkResult_WithMatchingServers()
    {
        // Arrange
        const string query = "BF1942";
        var mockResults = new api.ServerStats.Models.PagedResult<ServerBasicInfo>
        {
            Items =
            [
                new() { ServerName = "BF1942Server", Country = "US" }
            ],
            CurrentPage = 1,
            TotalItems = 1,
            TotalPages = 1
        };

        _serverStatsService.GetAllServersWithPaging(
                Arg.Any<int>(),
                Arg.Any<int>(),
                Arg.Any<string>(),
                Arg.Any<string>(),
                Arg.Any<ServerFilters>())
            .Returns(Task.FromResult(mockResults));

        // Act
        var result = await _controller.SearchServers(query);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<api.ServerStats.Models.PagedResult<ServerBasicInfo>>(okResult.Value);
        Assert.Single(returnedData.Items);
        Assert.Equal("BF1942Server", returnedData.Items.First().ServerName);
    }

    [Fact]
    public async Task SearchServers_ReturnsBadRequest_WhenQueryIsEmpty()
    {
        // Act
        var result = await _controller.SearchServers("");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchServers_ReturnsBadRequest_WhenQueryIsNull()
    {
        // Act
        var result = await _controller.SearchServers(null!);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchServers_ReturnsBadRequest_WithInvalidPage()
    {
        // Act
        var result = await _controller.SearchServers("test", page: 0);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task SearchServers_ReturnsBadRequest_WithInvalidGame()
    {
        // Act
        var result = await _controller.SearchServers("test", game: "InvalidGame");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetServerInsights_ReturnsOkResult_WithServerInsights()
    {
        // Arrange
        const string serverName = "TestServer";
        var mockInsights = new ServerInsights
        {
            ServerGuid = "guid-123",
            ServerName = serverName
        };

        _serverStatsService.GetServerInsights(serverName, Arg.Any<int>())
            .Returns(Task.FromResult(mockInsights));

        // Act
        var result = await _controller.GetServerInsights(serverName, null);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<ServerInsights>(okResult.Value);
        Assert.Equal(serverName, returnedData.ServerName);
        Assert.Equal("guid-123", returnedData.ServerGuid);
    }

    [Fact]
    public async Task GetServerInsights_ReturnsBadRequest_WhenServerNameIsEmpty()
    {
        // Act
        var result = await _controller.GetServerInsights("", null);

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetServerInsights_ReturnsNotFound_WhenServerDoesNotExist()
    {
        // Arrange
        const string serverName = "NonExistentServer";
        var mockInsights = new ServerInsights
        {
            ServerGuid = "",
            ServerName = serverName
        };

        _serverStatsService.GetServerInsights(serverName, Arg.Any<int>())
            .Returns(Task.FromResult(mockInsights));

        // Act
        var result = await _controller.GetServerInsights(serverName, null);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }

    [Fact]
    public async Task GetServerLeaderboards_ReturnsOkResult_WithLeaderboards()
    {
        // Arrange
        const string serverName = "TestServer";
        var mockLeaderboards = new ServerLeaderboards
        {
            ServerGuid = "guid-123",
            ServerName = serverName
        };

        _serverStatsService.GetServerLeaderboards(serverName, Arg.Any<int>(), Arg.Any<int?>())
            .Returns(Task.FromResult(mockLeaderboards));

        // Act
        var result = await _controller.GetServerLeaderboards(serverName);

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<ServerLeaderboards>(okResult.Value);
        Assert.Equal(serverName, returnedData.ServerName);
    }

    [Fact]
    public async Task GetServerLeaderboards_ReturnsBadRequest_WhenServerNameIsEmpty()
    {
        // Act
        var result = await _controller.GetServerLeaderboards("");

        // Assert
        Assert.IsType<BadRequestObjectResult>(result.Result);
    }

    [Fact]
    public async Task GetServerLeaderboards_ReturnsNotFound_WhenServerDoesNotExist()
    {
        // Arrange
        const string serverName = "NonExistentServer";
        var mockLeaderboards = new ServerLeaderboards
        {
            ServerGuid = "",
            ServerName = serverName
        };

        _serverStatsService.GetServerLeaderboards(serverName, Arg.Any<int>(), Arg.Any<int?>())
            .Returns(Task.FromResult(mockLeaderboards));

        // Act
        var result = await _controller.GetServerLeaderboards(serverName);

        // Assert
        var notFoundResult = Assert.IsType<NotFoundObjectResult>(result.Result);
        Assert.NotNull(notFoundResult.Value);
    }
}
