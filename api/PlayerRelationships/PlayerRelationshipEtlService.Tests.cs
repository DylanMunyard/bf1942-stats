// Example test file to validate co-play detection logic
// This would normally go in a test project, but showing here for reference

/*
using Xunit;
using Microsoft.EntityFrameworkCore;
using api.PlayerTracking;
using api.PlayerRelationships;

namespace api.Tests.PlayerRelationships;

public class PlayerRelationshipEtlServiceTests
{
    [Fact]
    public async Task DetectCoPlaySessions_FindsPlayersWithSameTimestamp()
    {
        // Arrange
        var options = new DbContextOptionsBuilder<PlayerTrackerDbContext>()
            .UseInMemoryDatabase(databaseName: "TestDb")
            .Options;

        await using var context = new PlayerTrackerDbContext(options);
        
        // Create test data: 3 players on same server at same time
        var server = new GameServer 
        { 
            Guid = "test-server-1", 
            Name = "Test Server", 
            Game = "bf1942" 
        };
        context.Servers.Add(server);

        var timestamp = DateTime.UtcNow;

        // Player 1
        var session1 = new PlayerSession
        {
            PlayerName = "Player1",
            ServerGuid = server.Guid,
            StartTime = timestamp,
            LastSeenTime = timestamp,
            IsActive = true
        };
        context.PlayerSessions.Add(session1);
        await context.SaveChangesAsync();

        context.PlayerObservations.Add(new PlayerObservation
        {
            SessionId = session1.SessionId,
            Timestamp = timestamp,
            Score = 100,
            Kills = 10,
            Deaths = 5,
            Ping = 50,
            Team = 1,
            TeamLabel = "Allies"
        });

        // Player 2
        var session2 = new PlayerSession
        {
            PlayerName = "Player2",
            ServerGuid = server.Guid,
            StartTime = timestamp,
            LastSeenTime = timestamp,
            IsActive = true
        };
        context.PlayerSessions.Add(session2);
        await context.SaveChangesAsync();

        context.PlayerObservations.Add(new PlayerObservation
        {
            SessionId = session2.SessionId,
            Timestamp = timestamp,
            Score = 80,
            Kills = 8,
            Deaths = 6,
            Ping = 60,
            Team = 1,
            TeamLabel = "Allies"
        });

        // Player 3 (same time, different server - should NOT match)
        var server2 = new GameServer 
        { 
            Guid = "test-server-2", 
            Name = "Other Server", 
            Game = "bf1942" 
        };
        context.Servers.Add(server2);

        var session3 = new PlayerSession
        {
            PlayerName = "Player3",
            ServerGuid = server2.Guid,
            StartTime = timestamp,
            LastSeenTime = timestamp,
            IsActive = true
        };
        context.PlayerSessions.Add(session3);
        await context.SaveChangesAsync();

        context.PlayerObservations.Add(new PlayerObservation
        {
            SessionId = session3.SessionId,
            Timestamp = timestamp,
            Score = 50,
            Kills = 5,
            Deaths = 5,
            Ping = 40,
            Team = 2,
            TeamLabel = "Axis"
        });

        await context.SaveChangesAsync();

        var logger = new Mock<ILogger<PlayerRelationshipEtlService>>().Object;
        var etlService = new PlayerRelationshipEtlService(context, logger);

        // Act
        var result = await etlService.DetectCoPlaySessionsAsync(
            timestamp.AddMinutes(-1),
            timestamp.AddMinutes(1)
        );

        // Assert
        Assert.Single(result); // Only one pair: Player1-Player2
        
        var pair = result[0];
        Assert.True(
            (pair.Player1 == "Player1" && pair.Player2 == "Player2") ||
            (pair.Player1 == "Player2" && pair.Player2 == "Player1")
        );
        Assert.Equal(server.Guid, pair.ServerGuid);
        Assert.Equal(timestamp, pair.Timestamp);
    }

    [Fact]
    public void AggregateRelationships_CombinesMultipleSessions()
    {
        // Arrange
        var logger = new Mock<ILogger<PlayerRelationshipEtlService>>().Object;
        var etlService = new PlayerRelationshipEtlService(null!, logger);

        var now = DateTime.UtcNow;
        var pairs = new List<(string, string, DateTime, string)>
        {
            ("Player1", "Player2", now.AddDays(-7), "server-1"),
            ("Player1", "Player2", now.AddDays(-6), "server-1"),
            ("Player1", "Player2", now.AddDays(-5), "server-2"),
            ("Player1", "Player2", now, "server-1")
        };

        // Act
        var result = etlService.AggregateRelationships(pairs);

        // Assert
        Assert.Single(result);
        
        var key = ("Player1", "Player2");
        Assert.True(result.ContainsKey(key));
        
        var metrics = result[key];
        Assert.Equal(4, metrics.ObservationCount);
        Assert.Equal(now.AddDays(-7), metrics.FirstSeen);
        Assert.Equal(now, metrics.LastSeen);
        Assert.Equal(2, metrics.ServerGuids.Count);
        Assert.Contains("server-1", metrics.ServerGuids);
        Assert.Contains("server-2", metrics.ServerGuids);
    }
}
*/

// To run these tests:
// 1. Create a test project: dotnet new xunit -n api.Tests
// 2. Add references:
//    - dotnet add reference ../api/api.csproj
//    - dotnet add package Microsoft.EntityFrameworkCore.InMemory
//    - dotnet add package Moq
// 3. Copy this file to api.Tests/PlayerRelationships/
// 4. Run: dotnet test
