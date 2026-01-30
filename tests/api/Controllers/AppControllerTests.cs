using api.Caching;
using api.Controllers;
using api.Gamification.Models;
using api.Gamification.Services;
using api.PlayerTracking;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NSubstitute;

namespace api.tests.Controllers;

public class AppControllerTests
{
    private readonly IBadgeDefinitionsService _badgeDefinitionsService;
    private readonly ICacheService _cacheService;
    private readonly AppController _controller;

    public AppControllerTests()
    {
        // Mock interfaces
        _badgeDefinitionsService = Substitute.For<IBadgeDefinitionsService>();
        _cacheService = Substitute.For<ICacheService>();

        var logger = Substitute.For<ILogger<AppController>>();
        // Create a real DbContext using in-memory database for testing
        var options = new DbContextOptionsBuilder<PlayerTrackerDbContext>()
            .UseInMemoryDatabase("test-app-controller-db")
            .Options;
        var dbContext = new PlayerTrackerDbContext(options);

        _controller = new AppController(
            _badgeDefinitionsService,
            _cacheService,
            logger,
            dbContext);
    }

    [Fact]
    public async Task GetInitialData_ReturnsOkResult_WhenCacheIsEmpty()
    {
        // Arrange
        _badgeDefinitionsService.GetAllBadges()
            .Returns(
            [
                new BadgeDefinition { Id = "test1", Name = "Test Badge", Category = "performance", Tier = "bronze" }
            ]);

        _cacheService.GetAsync<AppInitialData>(Arg.Any<string>())
            .Returns(Task.FromResult<AppInitialData?>(null));

        _cacheService.SetAsync(Arg.Any<string>(), Arg.Any<AppInitialData>(), Arg.Any<TimeSpan>())
            .Returns(Task.CompletedTask);

        // Act
        var result = await _controller.GetInitialData();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        Assert.NotNull(okResult.Value);
    }

    [Fact]
    public async Task GetInitialData_ReturnsCachedData_WhenAvailable()
    {
        // Arrange
        var cachedData = new AppInitialData
        {
            BadgeDefinitions = [],
            Categories = ["performance"],
            Tiers = ["bronze"],
            GeneratedAt = DateTime.UtcNow
        };

        _cacheService.GetAsync<AppInitialData>(Arg.Any<string>())
            .Returns(Task.FromResult<AppInitialData?>(cachedData));

        // Act
        var result = await _controller.GetInitialData();

        // Assert
        var okResult = Assert.IsType<OkObjectResult>(result.Result);
        var returnedData = Assert.IsType<AppInitialData>(okResult.Value);
        Assert.Same(cachedData, returnedData);
    }

    [Fact]
    public async Task GetInitialData_ReturnsInternalServerError_OnCacheException()
    {
        // Arrange
        _badgeDefinitionsService.GetAllBadges()
            .Returns([]);

        _cacheService.GetAsync<AppInitialData>(Arg.Any<string>())
            .Returns(Task.FromException<AppInitialData?>(new Exception("Cache error")));

        // Act
        var result = await _controller.GetInitialData();

        // Assert
        var statusResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(500, statusResult.StatusCode);
    }
}
