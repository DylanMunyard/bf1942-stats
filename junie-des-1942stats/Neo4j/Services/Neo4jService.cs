using Neo4j.Driver;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.Neo4j.Interfaces;
using junie_des_1942stats.Neo4j.Models;
using junie_des_1942stats.PlayerTracking;
using System.Text.Json;

namespace junie_des_1942stats.Neo4j.Services;

public class Neo4jService : INeo4jService, IDisposable
{
    private readonly IDriver _driver;
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly ILogger<Neo4jService> _logger;

    public Neo4jService(
        IDriver driver, 
        PlayerTrackerDbContext dbContext,
        ILogger<Neo4jService> logger)
    {
        _driver = driver;
        _dbContext = dbContext;
        _logger = logger;
    }

    public async Task TestConnectionAsync()
    {
        using var session = _driver.AsyncSession();
        var result = await session.RunAsync("RETURN 'Neo4j connection successful' as message");
        var record = await result.SingleAsync();
        _logger.LogInformation("Neo4j connection test: {Message}", record["message"].As<string>());
    }

    public async Task InitializeConstraintsAsync()
    {
        using var session = _driver.AsyncSession();
        
        var constraints = new[]
        {
            "CREATE CONSTRAINT player_name_unique IF NOT EXISTS FOR (p:Player) REQUIRE p.name IS UNIQUE",
            "CREATE CONSTRAINT server_guid_unique IF NOT EXISTS FOR (s:Server) REQUIRE s.guid IS UNIQUE", 
            "CREATE CONSTRAINT map_name_unique IF NOT EXISTS FOR (m:Map) REQUIRE m.name IS UNIQUE",
            "CREATE CONSTRAINT session_id_unique IF NOT EXISTS FOR (ses:Session) REQUIRE ses.session_id IS UNIQUE",
            "CREATE CONSTRAINT region_country_unique IF NOT EXISTS FOR (r:GeographicRegion) REQUIRE r.country IS UNIQUE",
            "CREATE CONSTRAINT timeslot_composite_unique IF NOT EXISTS FOR (t:TimeSlot) REQUIRE (t.hour, t.day_of_week, t.date) IS UNIQUE"
        };

        foreach (var constraint in constraints)
        {
            try
            {
                await session.RunAsync(constraint);
                _logger.LogInformation("Created constraint: {Constraint}", constraint);
            }
            catch (Exception ex)
            {
                _logger.LogWarning("Constraint creation failed (may already exist): {Constraint} - {Error}", constraint, ex.Message);
            }
        }
    }

    public async Task SyncLastMonthDataAsync()
    {
        _logger.LogInformation("Starting sync of last month's data to Neo4j");
        
        var oneMonthAgo = DateTime.UtcNow.AddDays(-30);
        
        // Get recent sessions
        var recentSessions = await _dbContext.PlayerSessions
            .Include(ps => ps.Player)
            .Include(ps => ps.Server)
            .Where(ps => ps.StartTime >= oneMonthAgo)
            .OrderByDescending(ps => ps.StartTime)
            .Take(10000) // Limit for trial
            .ToListAsync();

        _logger.LogInformation("Found {Count} sessions from last month", recentSessions.Count);

        using var session = _driver.AsyncSession();
        
        await session.ExecuteWriteAsync(async tx =>
        {
            // Clear existing data for fresh sync
            await tx.RunAsync("MATCH (n) DETACH DELETE n");
            
            // Track unique entities
            var processedPlayers = new HashSet<string>();
            var processedServers = new HashSet<string>();
            var processedMaps = new HashSet<string>();
            var processedRegions = new HashSet<string>();

            // Create nodes for all entities in recent sessions
            foreach (var playerSession in recentSessions)
            {
                // Create Player node
                if (!processedPlayers.Contains(playerSession.PlayerName))
                {
                    var playerNode = new PlayerNode
                    {
                        Name = playerSession.PlayerName,
                        FirstSeen = playerSession.Player.FirstSeen,
                        LastSeen = playerSession.Player.LastSeen,
                        TotalPlayTimeMinutes = playerSession.Player.TotalPlayTimeMinutes,
                        AiBot = playerSession.Player.AiBot,
                        AvgKdRatio = CalculateAvgKdRatio(playerSession.PlayerName, recentSessions)
                    };
                    
                    await CreatePlayerNodeInTransaction(tx, playerNode);
                    processedPlayers.Add(playerSession.PlayerName);
                }

                // Create Server node
                if (!processedServers.Contains(playerSession.ServerGuid))
                {
                    var serverNode = new ServerNode
                    {
                        Guid = playerSession.ServerGuid,
                        Name = playerSession.Server.Name,
                        Ip = playerSession.Server.Ip,
                        Port = playerSession.Server.Port,
                        GameId = playerSession.Server.GameId,
                        MaxPlayers = playerSession.Server.MaxPlayers,
                        Country = playerSession.Server.Country,
                        Region = playerSession.Server.Region,
                        City = playerSession.Server.City,
                        Timezone = playerSession.Server.Timezone
                    };
                    
                    await CreateServerNodeInTransaction(tx, serverNode);
                    processedServers.Add(playerSession.ServerGuid);
                }

                // Create Map node
                if (!processedMaps.Contains(playerSession.MapName))
                {
                    var mapNode = new MapNode
                    {
                        Name = playerSession.MapName,
                        GameType = playerSession.GameType
                    };
                    
                    await CreateMapNodeInTransaction(tx, mapNode);
                    processedMaps.Add(playerSession.MapName);
                }

                // Create GeographicRegion node
                if (playerSession.Server.Country != null && !processedRegions.Contains(playerSession.Server.Country))
                {
                    var regionNode = new GeographicRegionNode
                    {
                        Country = playerSession.Server.Country,
                        Region = playerSession.Server.Region,
                        Timezone = playerSession.Server.Timezone
                    };
                    
                    await CreateGeographicRegionNodeInTransaction(tx, regionNode);
                    processedRegions.Add(playerSession.Server.Country);
                }

                // Create Session node
                var sessionNode = new SessionNode
                {
                    SessionId = playerSession.SessionId,
                    StartTime = playerSession.StartTime,
                    LastSeenTime = playerSession.LastSeenTime,
                    TotalScore = playerSession.TotalScore,
                    TotalKills = playerSession.TotalKills,
                    TotalDeaths = playerSession.TotalDeaths,
                    DurationMinutes = (int)(playerSession.LastSeenTime - playerSession.StartTime).TotalMinutes,
                    KdRatio = playerSession.TotalDeaths > 0 ? (double)playerSession.TotalKills / playerSession.TotalDeaths : playerSession.TotalKills
                };
                
                await CreateSessionNodeInTransaction(tx, sessionNode);

                // Create basic relationships
                await CreateSessionRelationshipsInTransaction(tx, playerSession);
            }

            // Create player-to-player relationships
            await CreatePlayerRelationshipsInTransaction(tx, recentSessions);
            
            _logger.LogInformation("Successfully synced {Count} sessions to Neo4j", recentSessions.Count);
        });
    }

    public async Task ClearAllDataAsync()
    {
        using var session = _driver.AsyncSession();
        await session.RunAsync("MATCH (n) DETACH DELETE n");
        _logger.LogInformation("Cleared all Neo4j data");
    }

    private double CalculateAvgKdRatio(string playerName, List<PlayerSession> sessions)
    {
        var playerSessions = sessions.Where(s => s.PlayerName == playerName).ToList();
        if (!playerSessions.Any()) return 0;
        
        var totalKills = playerSessions.Sum(s => s.TotalKills);
        var totalDeaths = playerSessions.Sum(s => s.TotalDeaths);
        
        return totalDeaths > 0 ? (double)totalKills / totalDeaths : totalKills;
    }

    private async Task CreatePlayerNodeInTransaction(IAsyncTransaction tx, PlayerNode player)
    {
        var cypher = """
            CREATE (p:Player {
                name: $name,
                first_seen: $first_seen,
                last_seen: $last_seen,
                total_playtime_minutes: $total_playtime_minutes,
                ai_bot: $ai_bot,
                avg_kd_ratio: $avg_kd_ratio
            })
            """;

        await tx.RunAsync(cypher, new
        {
            name = player.Name,
            first_seen = player.FirstSeen,
            last_seen = player.LastSeen,
            total_playtime_minutes = player.TotalPlayTimeMinutes,
            ai_bot = player.AiBot,
            avg_kd_ratio = player.AvgKdRatio
        });
    }

    private async Task CreateServerNodeInTransaction(IAsyncTransaction tx, ServerNode server)
    {
        var cypher = """
            CREATE (s:Server {
                guid: $guid,
                name: $name,
                ip: $ip,
                port: $port,
                game_id: $game_id,
                max_players: $max_players,
                country: $country,
                region: $region,
                city: $city,
                timezone: $timezone
            })
            """;

        await tx.RunAsync(cypher, new
        {
            guid = server.Guid,
            name = server.Name,
            ip = server.Ip,
            port = server.Port,
            game_id = server.GameId,
            max_players = server.MaxPlayers,
            country = server.Country,
            region = server.Region,
            city = server.City,
            timezone = server.Timezone
        });
    }

    private async Task CreateMapNodeInTransaction(IAsyncTransaction tx, MapNode map)
    {
        var cypher = """
            CREATE (m:Map {
                name: $name,
                game_type: $game_type
            })
            """;

        await tx.RunAsync(cypher, new
        {
            name = map.Name,
            game_type = map.GameType
        });
    }

    private async Task CreateGeographicRegionNodeInTransaction(IAsyncTransaction tx, GeographicRegionNode region)
    {
        var cypher = """
            CREATE (r:GeographicRegion {
                country: $country,
                region: $region,
                timezone: $timezone
            })
            """;

        await tx.RunAsync(cypher, new
        {
            country = region.Country,
            region = region.Region,
            timezone = region.Timezone
        });
    }

    private async Task CreateSessionNodeInTransaction(IAsyncTransaction tx, SessionNode session)
    {
        var cypher = """
            CREATE (ses:Session {
                session_id: $session_id,
                start_time: $start_time,
                last_seen_time: $last_seen_time,
                total_score: $total_score,
                total_kills: $total_kills,
                total_deaths: $total_deaths,
                duration_minutes: $duration_minutes,
                kd_ratio: $kd_ratio
            })
            """;

        await tx.RunAsync(cypher, new
        {
            session_id = session.SessionId,
            start_time = session.StartTime,
            last_seen_time = session.LastSeenTime,
            total_score = session.TotalScore,
            total_kills = session.TotalKills,
            total_deaths = session.TotalDeaths,
            duration_minutes = session.DurationMinutes,
            kd_ratio = session.KdRatio
        });
    }

    private async Task CreateSessionRelationshipsInTransaction(IAsyncTransaction tx, PlayerSession playerSession)
    {
        // Player -> Session
        await tx.RunAsync("""
            MATCH (p:Player {name: $player_name}), (ses:Session {session_id: $session_id})
            CREATE (p)-[:PLAYED_SESSION]->(ses)
            """, new { player_name = playerSession.PlayerName, session_id = playerSession.SessionId });

        // Session -> Server
        await tx.RunAsync("""
            MATCH (ses:Session {session_id: $session_id}), (s:Server {guid: $server_guid})
            CREATE (ses)-[:OCCURRED_ON]->(s)
            """, new { session_id = playerSession.SessionId, server_guid = playerSession.ServerGuid });

        // Session -> Map
        await tx.RunAsync("""
            MATCH (ses:Session {session_id: $session_id}), (m:Map {name: $map_name})
            CREATE (ses)-[:PLAYED_ON_MAP]->(m)
            """, new { session_id = playerSession.SessionId, map_name = playerSession.MapName });

        // Player -> Server (FREQUENTS relationship)
        await tx.RunAsync("""
            MATCH (p:Player {name: $player_name}), (s:Server {guid: $server_guid})
            MERGE (p)-[r:FREQUENTS]->(s)
            ON CREATE SET 
                r.session_count = 1,
                r.total_playtime_minutes = $duration_minutes,
                r.first_played = $start_time,
                r.last_played = $last_seen_time,
                r.total_score = $total_score,
                r.best_score = $total_score
            ON MATCH SET
                r.session_count = r.session_count + 1,
                r.total_playtime_minutes = r.total_playtime_minutes + $duration_minutes,
                r.last_played = $last_seen_time,
                r.total_score = r.total_score + $total_score,
                r.best_score = CASE WHEN $total_score > r.best_score THEN $total_score ELSE r.best_score END
            """, new 
            { 
                player_name = playerSession.PlayerName, 
                server_guid = playerSession.ServerGuid,
                duration_minutes = (int)(playerSession.LastSeenTime - playerSession.StartTime).TotalMinutes,
                start_time = playerSession.StartTime,
                last_seen_time = playerSession.LastSeenTime,
                total_score = playerSession.TotalScore
            });
    }

    private async Task CreatePlayerRelationshipsInTransaction(IAsyncTransaction tx, List<PlayerSession> sessions)
    {
        // Group sessions by server and time window to find concurrent players
        var sessionGroups = sessions
            .GroupBy(s => new { s.ServerGuid, Date = s.StartTime.Date, Hour = s.StartTime.Hour })
            .Where(g => g.Count() > 1);

        foreach (var group in sessionGroups)
        {
            var concurrentSessions = group.ToList();
            
            // Create PLAYED_WITH relationships between all players in concurrent sessions
            for (int i = 0; i < concurrentSessions.Count; i++)
            {
                for (int j = i + 1; j < concurrentSessions.Count; j++)
                {
                    var session1 = concurrentSessions[i];
                    var session2 = concurrentSessions[j];
                    
                    if (session1.PlayerName != session2.PlayerName)
                    {
                        await tx.RunAsync("""
                            MATCH (p1:Player {name: $player1}), (p2:Player {name: $player2})
                            MERGE (p1)-[r:PLAYED_WITH]-(p2)
                            ON CREATE SET 
                                r.sessions_together = 1,
                                r.first_played_together = $session_time,
                                r.last_played_together = $session_time,
                                r.same_team_sessions = 0,
                                r.opposing_team_sessions = 1
                            ON MATCH SET
                                r.sessions_together = r.sessions_together + 1,
                                r.last_played_together = $session_time,
                                r.opposing_team_sessions = r.opposing_team_sessions + 1
                            """, new 
                            { 
                                player1 = session1.PlayerName, 
                                player2 = session2.PlayerName,
                                session_time = session1.StartTime
                            });
                    }
                }
            }
        }
    }

    // Analytics query implementations
    public async Task<List<PlayerCommunityResult>> GetServerCommunitiesAsync()
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (p:Player)-[f:FREQUENTS]->(s:Server)
            WHERE f.session_count >= 3
            RETURN s.name as serverName, collect(p.name) as regularPlayers
            ORDER BY size(regularPlayers) DESC
            LIMIT 20
            """;

        var result = await session.RunAsync(cypher);
        var communities = new List<PlayerCommunityResult>();

        await foreach (var record in result)
        {
            communities.Add(new PlayerCommunityResult
            {
                ServerName = record["serverName"].As<string>(),
                RegularPlayers = record["regularPlayers"].As<List<string>>()
            });
        }

        return communities;
    }

    public async Task<List<PlayerSimilarityResult>> GetSimilarPlayersAsync()
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (p1:Player), (p2:Player)
            WHERE p1 <> p2 
              AND abs(p1.avg_kd_ratio - p2.avg_kd_ratio) < 0.3
              AND p1.ai_bot = false 
              AND p2.ai_bot = false
            OPTIONAL MATCH (p1)-[r:PLAYED_WITH]-(p2)
            RETURN p1.name as player1, p2.name as player2, 
                   abs(p1.avg_kd_ratio - p2.avg_kd_ratio) as kdDifference,
                   r IS NOT NULL as hasPlayedTogether
            ORDER BY kdDifference ASC
            LIMIT 50
            """;

        var result = await session.RunAsync(cypher);
        var similarities = new List<PlayerSimilarityResult>();

        await foreach (var record in result)
        {
            similarities.Add(new PlayerSimilarityResult
            {
                Player1 = record["player1"].As<string>(),
                Player2 = record["player2"].As<string>(),
                KdRatioDifference = record["kdDifference"].As<double>(),
                HasPlayedTogether = record["hasPlayedTogether"].As<bool>()
            });
        }

        return similarities;
    }

    public async Task<List<GeographicBattleResult>> GetCrossBorderBattlesAsync()
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (p1:Player)-[:PLAYED_WITH]-(p2:Player),
                  (p1)-[:FREQUENTS]->(s1:Server)-[:LOCATED_IN]->(r1:GeographicRegion),
                  (p2)-[:FREQUENTS]->(s2:Server)-[:LOCATED_IN]->(r2:GeographicRegion)
            WHERE r1.country <> r2.country
            RETURN r1.country as country1, r2.country as country2, count(*) as crossBorderBattles
            ORDER BY crossBorderBattles DESC
            LIMIT 20
            """;

        var result = await session.RunAsync(cypher);
        var battles = new List<GeographicBattleResult>();

        await foreach (var record in result)
        {
            battles.Add(new GeographicBattleResult
            {
                Country1 = record["country1"].As<string>(),
                Country2 = record["country2"].As<string>(),
                CrossBorderBattles = record["crossBorderBattles"].As<int>()
            });
        }

        return battles;
    }

    public async Task<List<MapMetaResult>> GetMapCompetitivenessAsync()
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (ses:Session)-[:PLAYED_ON_MAP]->(m:Map)
            WHERE ses.kd_ratio BETWEEN 0.7 AND 1.4
            WITH m.name as mapName, 
                 count(*) as balancedMatches,
                 avg(ses.kd_ratio) as avgKdRatio,
                 stdDev(ses.kd_ratio) as kdStdDev
            RETURN mapName, balancedMatches, 
                   (balancedMatches * (1.0 - kdStdDev)) as competitiveScore
            ORDER BY competitiveScore DESC
            LIMIT 20
            """;

        var result = await session.RunAsync(cypher);
        var mapMeta = new List<MapMetaResult>();

        await foreach (var record in result)
        {
            mapMeta.Add(new MapMetaResult
            {
                MapName = record["mapName"].As<string>(),
                BalancedMatches = record["balancedMatches"].As<int>(),
                CompetitiveScore = record["competitiveScore"].As<double>()
            });
        }

        return mapMeta;
    }

    public async Task<Dictionary<string, object>> GetPlayerNetworkStatsAsync(string playerName)
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (p:Player {name: $playerName})
            OPTIONAL MATCH (p)-[pw:PLAYED_WITH]-(other:Player)
            OPTIONAL MATCH (p)-[f:FREQUENTS]->(s:Server)
            RETURN p.name as playerName,
                   count(DISTINCT other) as uniquePlaymates,
                   count(DISTINCT s) as serversVisited,
                   max(f.session_count) as favoriteServerSessions,
                   sum(pw.sessions_together) as totalSharedSessions
            """;

        var result = await session.RunAsync(cypher, new { playerName });
        var record = await result.SingleAsync();

        return new Dictionary<string, object>
        {
            ["playerName"] = record["playerName"].As<string>(),
            ["uniquePlaymates"] = record["uniquePlaymates"].As<int>(),
            ["serversVisited"] = record["serversVisited"].As<int>(),
            ["favoriteServerSessions"] = record["favoriteServerSessions"].As<int>(),
            ["totalSharedSessions"] = record["totalSharedSessions"].As<int>()
        };
    }

    public async Task<List<string>> GetPlayerRecommendationsAsync(string playerName)
    {
        using var session = _driver.AsyncSession();
        
        var cypher = """
            MATCH (p:Player {name: $playerName})-[:PLAYED_WITH]-(friend:Player)-[:PLAYED_WITH]-(recommendation:Player)
            WHERE p <> recommendation 
              AND NOT (p)-[:PLAYED_WITH]-(recommendation)
              AND recommendation.ai_bot = false
            RETURN recommendation.name as recommendedPlayer, count(*) as mutualFriends
            ORDER BY mutualFriends DESC
            LIMIT 10
            """;

        var result = await session.RunAsync(cypher, new { playerName });
        var recommendations = new List<string>();

        await foreach (var record in result)
        {
            recommendations.Add(record["recommendedPlayer"].As<string>());
        }

        return recommendations;
    }

    // Simple node creation methods for interface compliance
    public async Task CreatePlayerNodeAsync(PlayerNode player)
    {
        using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => CreatePlayerNodeInTransaction(tx, player));
    }

    public async Task CreateServerNodeAsync(ServerNode server)
    {
        using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => CreateServerNodeInTransaction(tx, server));
    }

    public async Task CreateMapNodeAsync(MapNode map)
    {
        using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => CreateMapNodeInTransaction(tx, map));
    }

    public async Task CreateSessionNodeAsync(SessionNode session)
    {
        using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => CreateSessionNodeInTransaction(tx, session));
    }

    public async Task CreateGeographicRegionNodeAsync(GeographicRegionNode region)
    {
        using var session = _driver.AsyncSession();
        await session.ExecuteWriteAsync(tx => CreateGeographicRegionNodeInTransaction(tx, region));
    }

    public async Task CreateTimeSlotNodeAsync(TimeSlotNode timeSlot)
    {
        // Implementation for time slot creation
        using var session = _driver.AsyncSession();
        await session.RunAsync("""
            CREATE (t:TimeSlot {
                hour: $hour,
                day_of_week: $day_of_week,
                date: $date
            })
            """, new
            {
                hour = timeSlot.Hour,
                day_of_week = timeSlot.DayOfWeek,
                date = timeSlot.Date
            });
    }

    // Simple relationship creation methods for interface compliance
    public async Task CreatePlayedWithRelationshipAsync(string player1, string player2, PlayedWithRelationship relationship)
    {
        using var session = _driver.AsyncSession();
        await session.RunAsync("""
            MATCH (p1:Player {name: $player1}), (p2:Player {name: $player2})
            CREATE (p1)-[:PLAYED_WITH $props]-(p2)
            """, new { player1, player2, props = relationship });
    }

    public async Task CreateFrequentsRelationshipAsync(string playerName, string serverGuid, FrequentsRelationship relationship)
    {
        using var session = _driver.AsyncSession();
        await session.RunAsync("""
            MATCH (p:Player {name: $playerName}), (s:Server {guid: $serverGuid})
            CREATE (p)-[:FREQUENTS $props]->(s)
            """, new { playerName, serverGuid, props = relationship });
    }

    public async Task CreatePrefersMapRelationshipAsync(string playerName, string mapName, PrefersMapRelationship relationship)
    {
        using var session = _driver.AsyncSession();
        await session.RunAsync("""
            MATCH (p:Player {name: $playerName}), (m:Map {name: $mapName})
            CREATE (p)-[:PREFERS $props]->(m)
            """, new { playerName, mapName, props = relationship });
    }

    public async Task CreatePerformsBestInRelationshipAsync(string playerName, TimeSlotNode timeSlot, PerformsBestInRelationship relationship)
    {
        using var session = _driver.AsyncSession();
        await session.RunAsync("""
            MATCH (p:Player {name: $playerName}), 
                  (t:TimeSlot {hour: $hour, day_of_week: $day_of_week, date: $date})
            CREATE (p)-[:PERFORMS_BEST_IN $props]->(t)
            """, new 
            { 
                playerName, 
                hour = timeSlot.Hour,
                day_of_week = timeSlot.DayOfWeek,
                date = timeSlot.Date,
                props = relationship 
            });
    }

    public void Dispose()
    {
        _driver?.Dispose();
    }
}