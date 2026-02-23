using api.PlayerRelationships.Models;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace api.PlayerRelationships;

/// <summary>
/// Service for querying player relationships from Neo4j graph database.
/// Provides high-level queries for player networks, communities, and analytics.
/// </summary>
public class PlayerRelationshipService(
    Neo4jService neo4jService,
    ILogger<PlayerRelationshipService> logger) : IPlayerRelationshipService
{
    private static DateTime ToDateTime(object value)
    {
        if (value is ZonedDateTime zdt) return zdt.ToDateTimeOffset().UtcDateTime;
        if (value is LocalDateTime ldt) return ldt.ToDateTime();
        if (value is DateTimeOffset dto) return dto.UtcDateTime;
        if (value is DateTime dt) return dt;
        return DateTime.Parse(value?.ToString() ?? "");
    }

    private static DateTime? ToNullableDateTime(object? value)
    {
        if (value is null) return null;
        try { return ToDateTime(value); }
        catch { return null; }
    }

    /// <summary>
    /// Get players who most frequently play with the specified player.
    /// </summary>
    public async Task<List<PlayerRelationship>> GetMostFrequentCoPlayersAsync(
        string playerName, 
        int limit = 20,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting frequent co-players for {PlayerName}", playerName);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (p:Player {name: $playerName})-[r:PLAYED_WITH]-(other:Player)
                RETURN other.name AS otherPlayer,
                       r.sessionCount AS sessionCount,
                       r.firstPlayedTogether AS firstPlayed,
                       r.lastPlayedTogether AS lastPlayed,
                       r.servers AS servers
                ORDER BY r.sessionCount DESC
                LIMIT $limit";

            var cursor = await tx.RunAsync(query, new { playerName, limit });
            var results = new List<PlayerRelationship>();

            await foreach (var record in cursor)
            {
                results.Add(new PlayerRelationship
                {
                    Player1Name = playerName,
                    Player2Name = record["otherPlayer"].As<string>(),
                    SessionCount = record["sessionCount"].As<int>(),
                    FirstPlayedTogether = ToDateTime(record["firstPlayed"]),
                    LastPlayedTogether = ToDateTime(record["lastPlayed"]),
                    ServerGuids = record["servers"].As<List<string>>() ?? [],
                    TotalMinutes = 0, // Not tracked in current schema
                    AvgScoreDiff = 0  // Not tracked in current schema
                });
            }

            return results;
        });
    }

    /// <summary>
    /// Get players who play on the same servers but have never played together.
    /// Great for finding potential squad mates.
    /// </summary>
    public async Task<List<string>> GetPotentialConnectionsAsync(
        string playerName,
        int limit = 20,
        int daysActive = 30,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Finding potential connections for {PlayerName}", playerName);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysActive);
            
            var query = @"
                // Find servers where the player is active
                MATCH (p:Player {name: $playerName})-[r1:PLAYS_ON]->(s:Server)
                WHERE r1.lastPlayed > $cutoffDate
                
                // Find other players on same servers
                WITH p, s
                MATCH (other:Player)-[r2:PLAYS_ON]->(s)
                WHERE other.name <> $playerName 
                  AND r2.lastPlayed > $cutoffDate
                  AND NOT EXISTS((p)-[:PLAYED_WITH]-(other))
                
                // Count common servers and sort by overlap
                WITH other.name AS otherPlayer, COUNT(DISTINCT s) AS commonServers
                ORDER BY commonServers DESC
                LIMIT $limit
                
                RETURN otherPlayer";

            var cursor = await tx.RunAsync(query, new { playerName, cutoffDate, limit });
            var results = new List<string>();

            await foreach (var record in cursor)
            {
                results.Add(record["otherPlayer"].As<string>());
            }

            return results;
        });
    }

    /// <summary>
    /// Get all servers where two players have played together.
    /// </summary>
    public async Task<List<string>> GetSharedServersAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting shared servers for {Player1} and {Player2}", player1Name, player2Name);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (p1:Player {name: $player1Name})-[r:PLAYED_WITH]-(p2:Player {name: $player2Name})
                RETURN r.servers AS servers";

            var cursor = await tx.RunAsync(query, new { player1Name, player2Name });
            var record = await cursor.SingleOrDefaultAsync();

            if (record == null)
                return new List<string>();

            return record["servers"].As<List<string>>() ?? new List<string>();
        });
    }

    /// <summary>
    /// Find recent new connections (players who started playing together recently).
    /// </summary>
    public async Task<List<PlayerRelationship>> GetRecentConnectionsAsync(
        string playerName,
        int daysSince = 7,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting recent connections for {PlayerName} in last {Days} days", playerName, daysSince);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-daysSince);

            var query = @"
                MATCH (p:Player {name: $playerName})-[r:PLAYED_WITH]-(other:Player)
                WHERE r.firstPlayedTogether > $cutoffDate
                   OR r.lastPlayedTogether > $cutoffDate
                RETURN other.name AS otherPlayer,
                       r.sessionCount AS sessionCount,
                       r.firstPlayedTogether AS firstPlayed,
                       r.lastPlayedTogether AS lastPlayed,
                       r.servers AS servers
                ORDER BY r.firstPlayedTogether DESC
                LIMIT 50";

            var cursor = await tx.RunAsync(query, new { playerName, cutoffDate });
            var results = new List<PlayerRelationship>();

            await foreach (var record in cursor)
            {
                results.Add(new PlayerRelationship
                {
                    Player1Name = playerName,
                    Player2Name = record["otherPlayer"].As<string>(),
                    SessionCount = record["sessionCount"].As<int>(),
                    FirstPlayedTogether = ToDateTime(record["firstPlayed"]),
                    LastPlayedTogether = ToDateTime(record["lastPlayed"]),
                    ServerGuids = record["servers"].As<List<string>>() ?? [],
                    TotalMinutes = 0,
                    AvgScoreDiff = 0
                });
            }

            return results;
        });
    }

    /// <summary>
    /// Get relationship strength between two players.
    /// Returns null if they've never played together.
    /// </summary>
    public async Task<PlayerRelationship?> GetRelationshipAsync(
        string player1Name,
        string player2Name,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting relationship between {Player1} and {Player2}", player1Name, player2Name);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (p1:Player {name: $player1Name})-[r:PLAYED_WITH]-(p2:Player {name: $player2Name})
                RETURN r.sessionCount AS sessionCount,
                       r.firstPlayedTogether AS firstPlayed,
                       r.lastPlayedTogether AS lastPlayed,
                       r.servers AS servers";

            var cursor = await tx.RunAsync(query, new { player1Name, player2Name });
            var record = await cursor.SingleOrDefaultAsync();

            if (record == null)
                return null;

            return new PlayerRelationship
            {
                Player1Name = player1Name,
                Player2Name = player2Name,
                SessionCount = record["sessionCount"].As<int>(),
                FirstPlayedTogether = ToDateTime(record["firstPlayed"]),
                LastPlayedTogether = ToDateTime(record["lastPlayed"]),
                ServerGuids = record["servers"].As<List<string>>() ?? [],
                TotalMinutes = 0,
                AvgScoreDiff = 0
            };
        });
    }

    /// <summary>
    /// Get network statistics for a player.
    /// </summary>
    public async Task<PlayerNetworkStats> GetPlayerNetworkStatsAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting network stats for {PlayerName}", playerName);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (p:Player {name: $playerName})
                OPTIONAL MATCH (p)-[r:PLAYED_WITH]-(other:Player)
                WITH p, COUNT(DISTINCT other) AS connectionCount, 
                     SUM(r.sessionCount) AS totalSessions,
                     COLLECT(DISTINCT r.servers) AS allServers
                
                OPTIONAL MATCH (p)-[ps:PLAYS_ON]->(s:Server)
                
                RETURN connectionCount,
                       totalSessions,
                       COUNT(DISTINCT s) AS serverCount,
                       p.firstSeen AS firstSeen,
                       p.lastSeen AS lastSeen,
                       SIZE([item IN REDUCE(s = [], list IN allServers | s + list) WHERE item IS NOT NULL | item]) AS uniqueServersWithFriends";

            var cursor = await tx.RunAsync(query, new { playerName });
            var record = await cursor.SingleOrDefaultAsync();

            if (record == null)
            {
                return new PlayerNetworkStats
                {
                    PlayerName = playerName,
                    ConnectionCount = 0,
                    TotalCoPlaySessions = 0,
                    ServerCount = 0,
                    FirstSeen = DateTime.UtcNow,
                    LastSeen = DateTime.UtcNow
                };
            }

            return new PlayerNetworkStats
            {
                PlayerName = playerName,
                ConnectionCount = record["connectionCount"].As<int>(),
                TotalCoPlaySessions = record["totalSessions"].As<int?>() ?? 0,
                ServerCount = record["serverCount"].As<int>(),
                FirstSeen = ToNullableDateTime(record["firstSeen"]) ?? DateTime.UtcNow,
                LastSeen = ToNullableDateTime(record["lastSeen"]) ?? DateTime.UtcNow
            };
        });
    }

    /// <summary>
    /// Get the player's extended network (friends of friends).
    /// </summary>
    public async Task<PlayerNetworkGraph> GetPlayerNetworkGraphAsync(
        string playerName,
        int depth = 2,
        int maxNodes = 100,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting network graph for {PlayerName} with depth {Depth}", playerName, depth);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            // Get nodes and relationships up to specified depth
            var query = @"
                MATCH path = (p:Player {name: $playerName})-[:PLAYED_WITH*1.." + depth + @"]-(other:Player)
                WITH p, other, relationships(path) AS rels
                LIMIT $maxNodes
                
                UNWIND rels AS rel
                WITH DISTINCT rel, startNode(rel) AS n1, endNode(rel) AS n2
                
                RETURN 
                    n1.name AS player1,
                    n2.name AS player2,
                    rel.sessionCount AS sessionCount,
                    rel.lastPlayedTogether AS lastPlayed";

            var cursor = await tx.RunAsync(query, new { playerName, maxNodes });
            
            var nodes = new HashSet<string> { playerName };
            var edges = new List<NetworkEdge>();

            await foreach (var record in cursor)
            {
                var player1 = record["player1"].As<string>();
                var player2 = record["player2"].As<string>();
                
                nodes.Add(player1);
                nodes.Add(player2);
                
                edges.Add(new NetworkEdge
                {
                    Source = player1,
                    Target = player2,
                    Weight = record["sessionCount"].As<int>(),
                    LastInteraction = ToNullableDateTime(record["lastPlayed"]) ?? DateTime.MinValue
                });
            }

            return new PlayerNetworkGraph
            {
                CenterPlayer = playerName,
                Nodes = nodes.Select(n => new NetworkNode { Id = n, Label = n }).ToList(),
                Edges = edges,
                Depth = depth
            };
        });
    }

    /// <summary>
    /// Get social statistics for a server.
    /// </summary>
    public async Task<ServerSocialStats> GetServerSocialStatsAsync(
        string serverGuid,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting social stats for server {ServerGuid}", serverGuid);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            // Get various social metrics for the server
            var query = @"
                MATCH (s:Server {guid: $serverGuid})
                OPTIONAL MATCH (p:Player)-[:PLAYS_ON]->(s)
                WITH s, COUNT(DISTINCT p) AS uniquePlayers
                
                // Count relationships between players who play on this server
                OPTIONAL MATCH (p1:Player)-[:PLAYS_ON]->(s)<-[:PLAYS_ON]-(p2:Player)
                WHERE p1 <> p2
                OPTIONAL MATCH (p1)-[r:PLAYED_WITH]-(p2)
                WHERE $serverGuid IN r.servers
                
                WITH s, uniquePlayers, COUNT(DISTINCT r) AS relationshipCount
                
                // Calculate average connections per player
                WITH s, uniquePlayers, relationshipCount,
                     CASE WHEN uniquePlayers > 0 
                          THEN toFloat(relationshipCount * 2) / uniquePlayers 
                          ELSE 0 
                     END AS avgConnections
                
                // Get retention rate (players active in last 30 days vs last 90 days)
                OPTIONAL MATCH (recent:Player)-[r1:PLAYS_ON]->(s)
                WHERE r1.lastPlayed > datetime() - duration('P30D')
                WITH s, uniquePlayers, relationshipCount, avgConnections, 
                     COUNT(DISTINCT recent) AS recentPlayers
                
                OPTIONAL MATCH (older:Player)-[r2:PLAYS_ON]->(s)
                WHERE r2.lastPlayed > datetime() - duration('P90D')
                WITH s, uniquePlayers, relationshipCount, avgConnections, recentPlayers,
                     COUNT(DISTINCT older) AS olderPlayers
                
                RETURN uniquePlayers,
                       avgConnections,
                       CASE WHEN olderPlayers > 0 
                            THEN toFloat(recentPlayers) / olderPlayers 
                            ELSE 0 
                       END AS retentionRate,
                       relationshipCount";

            var cursor = await tx.RunAsync(query, new { serverGuid });
            var record = await cursor.SingleOrDefaultAsync();

            if (record == null)
            {
                return new ServerSocialStats
                {
                    ServerGuid = serverGuid,
                    UniquePlayerCount = 0,
                    AverageConnectionsPerPlayer = 0,
                    CommunityCount = 0,
                    RetentionRate = 0
                };
            }

            // TODO: Add community detection in a future phase
            return new ServerSocialStats
            {
                ServerGuid = serverGuid,
                UniquePlayerCount = record["uniquePlayers"].As<int>(),
                AverageConnectionsPerPlayer = record["avgConnections"].As<double>(),
                CommunityCount = 0, // Will be implemented with community detection
                RetentionRate = record["retentionRate"].As<double>()
            };
        });
    }

    /// <summary>
    /// Get all detected communities.
    /// </summary>
    public async Task<List<PlayerCommunity>> GetCommunitiesAsync(
        int minSize = 3,
        bool activeOnly = true,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting communities with minSize={MinSize}, activeOnly={ActiveOnly}", minSize, activeOnly);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var cutoffDate = activeOnly ? DateTime.UtcNow.AddDays(-30) : DateTime.MinValue;

            // Query for communities (stored as Community nodes after detection)
            var query = @"
                MATCH (c:Community)
                WHERE SIZE(c.members) >= $minSize
                  AND (c.lastActiveDate IS NULL OR c.lastActiveDate > $cutoffDate)
                RETURN c.id AS id,
                       c.name AS name,
                       c.members AS members,
                       c.coreMembers AS coreMembers,
                       c.formationDate AS formationDate,
                       c.lastActiveDate AS lastActiveDate,
                       c.avgSessionsPerPair AS avgSessionsPerPair,
                       c.cohesionScore AS cohesionScore,
                       c.primaryServers AS primaryServers
                ORDER BY c.cohesionScore DESC";

            var cursor = await tx.RunAsync(query, new { minSize, cutoffDate });
            var communities = new List<PlayerCommunity>();

            await foreach (var record in cursor)
            {
                var primaryServersData = record["primaryServers"].As<List<string>>() ?? [];

                communities.Add(new PlayerCommunity
                {
                    Id = record["id"].As<string>(),
                    Name = record["name"].As<string>(),
                    Members = record["members"].As<List<string>>(),
                    CoreMembers = record["coreMembers"].As<List<string>>(),
                    PrimaryServers = primaryServersData,
                    FormationDate = ToDateTime(record["formationDate"]),
                    LastActiveDate = ToDateTime(record["lastActiveDate"]),
                    AvgSessionsPerPair = record["avgSessionsPerPair"].As<double>(),
                    CohesionScore = record["cohesionScore"].As<double>()
                });
            }

            return communities;
        });
    }

    /// <summary>
    /// Get a specific community by ID.
    /// </summary>
    public async Task<PlayerCommunity?> GetCommunityByIdAsync(
        string communityId,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting community {CommunityId}", communityId);

        var communities = await GetCommunitiesAsync(minSize: 0, activeOnly: false, cancellationToken);
        logger.LogDebug("Retrieved {Count} total communities for search", communities.Count);

        var result = communities.FirstOrDefault(c => c.Id == communityId);
        if (result == null && communities.Count > 0)
        {
            var availableIds = string.Join(", ", communities.Take(5).Select(c => c.Id));
            logger.LogWarning("Community {CommunityId} not found. Sample IDs: {SampleIds}", communityId, availableIds);
        }
        return result;
    }

    /// <summary>
    /// Get communities that a player belongs to.
    /// </summary>
    public async Task<List<PlayerCommunity>> GetPlayerCommunitiesAsync(
        string playerName,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting communities for player {PlayerName}", playerName);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (c:Community)
                WHERE $playerName IN c.members
                RETURN c.id AS id,
                       c.name AS name,
                       c.members AS members,
                       c.coreMembers AS coreMembers,
                       c.formationDate AS formationDate,
                       c.lastActiveDate AS lastActiveDate,
                       c.avgSessionsPerPair AS avgSessionsPerPair,
                       c.cohesionScore AS cohesionScore,
                       c.primaryServers AS primaryServers
                ORDER BY c.cohesionScore DESC";

            var cursor = await tx.RunAsync(query, new { playerName });
            var communities = new List<PlayerCommunity>();

            await foreach (var record in cursor)
            {
                var primaryServersData = record["primaryServers"].As<List<string>>() ?? [];

                communities.Add(new PlayerCommunity
                {
                    Id = record["id"].As<string>(),
                    Name = record["name"].As<string>(),
                    Members = record["members"].As<List<string>>(),
                    CoreMembers = record["coreMembers"].As<List<string>>(),
                    PrimaryServers = primaryServersData,
                    FormationDate = ToDateTime(record["formationDate"]),
                    LastActiveDate = ToDateTime(record["lastActiveDate"]),
                    AvgSessionsPerPair = record["avgSessionsPerPair"].As<double>(),
                    CohesionScore = record["cohesionScore"].As<double>()
                });
            }

            return communities;
        });
    }

    /// <summary>
    /// Run community detection algorithm and store results.
    /// Uses pure Cypher-based clustering (no GDS required for Kubernetes deployment).
    /// </summary>
    public async Task<string> DetectAndStoreCommunities(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Starting community detection (Cypher-based clustering)");

        return await neo4jService.ExecuteWriteAsync(async tx =>
        {
            // First, clear existing communities
            await tx.RunAsync("MATCH (c:Community) DELETE c");

            // Minimum session threshold for considering players connected
            const int minSessions = 3;

            // Step 1: Find player groups using simple connection-based clustering
            // Group players by finding the smallest player name they're connected to (creates stable groups)
            var assignQuery = $@"
                // For each player, find all their strong connections
                MATCH (p:Player)
                MATCH (p)-[rel:PLAYED_WITH]-(teammate)
                WHERE rel.sessionCount >= {minSessions}
                WITH p, COLLECT(DISTINCT teammate.name) AS teammates
                WHERE SIZE(teammates) >= 1
                // Find the 'leader' - the alphabetically first name (stable, deterministic)
                WITH p, teammates + [p.name] AS allNames, teammates
                // Get lexicographically smallest name for community ID
                UNWIND allNames AS name
                WITH p, teammates, MIN(name) AS leader
                SET p.communityId = leader
                RETURN COUNT(DISTINCT leader) AS communityCount,
                       COUNT(p) AS playersAssigned";

            // Step 2: Create community nodes with full statistics
            var createCommunitiesQuery = @"
                // Group players by community
                MATCH (p:Player)
                WHERE p.communityId IS NOT NULL
                WITH p.communityId AS communityId, COLLECT(p.name) AS members
                WHERE SIZE(members) >= 3

                // Calculate cohesion and other stats
                UNWIND members AS m1
                UNWIND members AS m2
                WITH communityId, members, m1, m2
                WHERE m1 < m2
                MATCH (p1:Player {name: m1})-[r:PLAYED_WITH]-(p2:Player {name: m2})
                WITH communityId, members, AVG(r.sessionCount) AS avgSessions,
                     MAX(r.lastPlayedTogether) AS lastActive, COUNT(r) AS edgeCount

                // Cohesion = density of connections within the community
                WITH communityId, members, avgSessions, lastActive, edgeCount,
                     CASE WHEN SIZE(members) <= 1
                          THEN 0.0
                          ELSE toFloat(edgeCount * 2) / (SIZE(members) * (SIZE(members) - 1))
                     END AS cohesion

                // Find core members (those with most connections in the community)
                UNWIND members AS member
                MATCH (p:Player {name: member})-[r:PLAYED_WITH]-(other:Player)
                WHERE other.name IN members
                WITH communityId, members, avgSessions, lastActive, cohesion, member, COUNT(r) AS degree
                ORDER BY degree DESC
                WITH communityId, members, avgSessions, lastActive, cohesion, COLLECT(member)[0..5] AS coreMembers

                // Find primary servers (as simple lists since Neo4j can't store complex objects)
                UNWIND members AS member
                MATCH (p:Player {name: member})-[ps:PLAYS_ON]->(s:Server)
                WITH communityId, members, avgSessions, lastActive, cohesion, coreMembers,
                     s.guid AS serverGuid, s.name AS serverName, COUNT(*) AS playCount
                ORDER BY playCount DESC
                WITH communityId, members, avgSessions, lastActive, cohesion, coreMembers,
                     COLLECT(serverGuid)[0..5] AS serverGuids,
                     COLLECT(serverName)[0..5] AS serverNames

                // Create Community node (with simplified primaryServers structure)
                CREATE (c:Community {
                    id: 'comm_' + SUBSTRING(communityId, 0, 20),
                    name: 'Squad: ' + coreMembers[0],
                    members: members,
                    coreMembers: coreMembers,
                    primaryServers: serverNames,
                    formationDate: datetime(),
                    lastActiveDate: lastActive,
                    avgSessionsPerPair: avgSessions,
                    cohesionScore: cohesion
                })
                RETURN COUNT(c) AS createdCommunities";

            try
            {
                logger.LogDebug("Assigning players to communities with minimum {MinSessions} sessions", minSessions);
                var assignCursor = await tx.RunAsync(assignQuery);
                var assignResult = await assignCursor.SingleAsync();
                var communityCount = assignResult["communityCount"].As<int>();
                var playersAssigned = assignResult["playersAssigned"].As<int>();
                logger.LogDebug("Assigned {PlayersAssigned} players to {CommunityCount} communities", playersAssigned, communityCount);

                logger.LogDebug("Creating community nodes");
                var createCursor = await tx.RunAsync(createCommunitiesQuery);
                var createResult = await createCursor.SingleAsync();
                var createdCount = createResult["createdCommunities"].As<int>();

                logger.LogInformation("Community detection completed: created {CreatedCount} communities", createdCount);
                return $"Successfully detected and created {createdCount} communities using pure Cypher clustering";
            }
            catch (Exception ex)
            {
                logger.LogError(ex, "Error during community detection");
                throw;
            }
        });
    }

    /// <summary>
    /// Get squad recommendations for a player based on multiple factors.
    /// </summary>
    public async Task<List<SquadRecommendation>> GetSquadRecommendationsAsync(
        string playerName,
        int limit = 10,
        bool onlineOnly = false,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting squad recommendations for {PlayerName}", playerName);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var cutoffDate = DateTime.UtcNow.AddDays(-30);
            var onlineFilter = onlineOnly ? "AND candidate.lastSeen > datetime() - duration('PT15M')" : "";

            // Complex query to find and score potential squad mates
            var query = $@"
                MATCH (p:Player {{name: $playerName}})
                OPTIONAL MATCH (p)-[r1:PLAYS_ON]->(s:Server)<-[r2:PLAYS_ON]-(candidate:Player)
                WHERE candidate.name <> $playerName 
                  AND r1.lastPlayed > $cutoffDate
                  AND r2.lastPlayed > $cutoffDate
                  AND NOT EXISTS((p)-[:PLAYED_WITH]-(candidate))
                  {onlineFilter}
                
                WITH p, candidate, COUNT(DISTINCT s) AS commonServerCount, 
                     COLLECT(DISTINCT {{
                         guid: s.guid, 
                         name: s.name,
                         lastPlayed1: r1.lastPlayed,
                         lastPlayed2: r2.lastPlayed
                     }}) AS serverData
                WHERE commonServerCount > 0
                
                // Calculate play time overlap (simplified - would need session data for accuracy)
                WITH p, candidate, commonServerCount, serverData,
                     SIZE([x IN serverData WHERE 
                         duration.between(x.lastPlayed1, x.lastPlayed2).hours < 24]) AS recentOverlaps
                
                // Score based on multiple factors
                WITH p, candidate, commonServerCount, serverData, recentOverlaps,
                     (commonServerCount * 20.0 + recentOverlaps * 30.0) AS baseScore
                
                // Boost score if they have mutual connections
                OPTIONAL MATCH (p)-[:PLAYED_WITH]-(mutual:Player)-[:PLAYED_WITH]-(candidate)
                WITH p, candidate, commonServerCount, serverData, recentOverlaps, baseScore,
                     COUNT(DISTINCT mutual) AS mutualConnections
                
                WITH candidate.name AS candidateName,
                     candidate.lastSeen AS lastSeen,
                     baseScore + (mutualConnections * 10.0) AS finalScore,
                     commonServerCount,
                     serverData,
                     recentOverlaps,
                     mutualConnections,
                     CASE 
                         WHEN mutualConnections > 5 THEN ['Many mutual friends']
                         WHEN mutualConnections > 0 THEN ['Some mutual friends']
                         ELSE []
                     END +
                     CASE 
                         WHEN commonServerCount > 3 THEN ['Play on many same servers']
                         WHEN commonServerCount > 0 THEN ['Play on same servers']
                         ELSE []
                     END +
                     CASE 
                         WHEN recentOverlaps > 5 THEN ['Often online at same time']
                         WHEN recentOverlaps > 0 THEN ['Sometimes online together']
                         ELSE []
                     END AS reasons
                
                ORDER BY finalScore DESC
                LIMIT $limit
                
                RETURN candidateName, lastSeen, finalScore, commonServerCount, 
                       serverData, reasons, mutualConnections";

            var cursor = await tx.RunAsync(query, new { playerName, cutoffDate, limit });
            var recommendations = new List<SquadRecommendation>();

            await foreach (var record in cursor)
            {
                var serverDataList = record["serverData"].As<List<Dictionary<string, object>>>();
                var commonServers = serverDataList?.Select(s => new CommonServer
                {
                    ServerGuid = s["guid"].ToString()!,
                    ServerName = s["name"].ToString()!,
                    BothPlayedSessions = 1, // Simplified - would need session counting
                    LastSeenTogether = ToDateTime(s["lastPlayed2"])
                }).ToList() ?? [];

                recommendations.Add(new SquadRecommendation
                {
                    PlayerName = record["candidateName"].As<string>(),
                    CompatibilityScore = Math.Min(100, record["finalScore"].As<double>()),
                    Reasons = record["reasons"].As<List<string>>(),
                    CommonServers = commonServers,
                    PlayTimeOverlap = new PlayTimeOverlap
                    {
                        OverlapPercentage = 0, // Would need session data
                        CommonHoursUtc = [], // Would need session data
                        CommonDays = [] // Would need session data
                    },
                    IsOnline = ToDateTime(record["lastSeen"]).AddMinutes(15) > DateTime.UtcNow
                });
            }

            return recommendations;
        });
    }

    /// <summary>
    /// Record feedback on squad recommendations to improve future suggestions.
    /// </summary>
    public async Task RecordSquadRecommendationFeedback(
        string playerName,
        string recommendedPlayer,
        bool wasHelpful,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Recording squad recommendation feedback: {Player} -> {Recommended}, Helpful: {WasHelpful}", 
            playerName, recommendedPlayer, wasHelpful);

        await neo4jService.ExecuteWriteAsync<bool>(async tx =>
        {
            // Create or update a feedback relationship
            var query = @"
                MATCH (p1:Player {name: $playerName}), (p2:Player {name: $recommendedPlayer})
                MERGE (p1)-[f:SQUAD_FEEDBACK]->(p2)
                SET f.wasHelpful = $wasHelpful,
                    f.timestamp = datetime(),
                    f.feedbackCount = COALESCE(f.feedbackCount, 0) + 1";

            await tx.RunAsync(query, new { playerName, recommendedPlayer, wasHelpful });
            
            // If helpful, maybe they'll play together in the future
            if (wasHelpful)
            {
                logger.LogInformation("Positive squad feedback recorded: {Player1} + {Player2}", 
                    playerName, recommendedPlayer);
            }
            
            return true;
        });
    }

    /// <summary>
    /// Analyze player migration patterns between servers.
    /// </summary>
    public async Task<PlayerMigrationFlow> GetPlayerMigrationFlowAsync(
        DateTime startDate,
        DateTime endDate,
        string? game = null,
        CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Getting player migration flow from {Start} to {End}", startDate, endDate);

        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var gameFilter = !string.IsNullOrEmpty(game) ? "AND s1.game = $game AND s2.game = $game" : "";

            // Find players who moved between servers
            var query = $@"
                MATCH (p:Player)-[r1:PLAYS_ON]->(s1:Server)
                WHERE r1.lastPlayed >= $startDate AND r1.lastPlayed <= $endDate
                {gameFilter}
                
                WITH p, s1, r1.lastPlayed AS lastPlayedS1
                
                MATCH (p)-[r2:PLAYS_ON]->(s2:Server)
                WHERE s2 <> s1
                  AND r2.lastPlayed > lastPlayedS1
                  AND r2.lastPlayed <= $endDate
                  {gameFilter.Replace("s1", "s2")}
                
                WITH s1, s2, p, lastPlayedS1, r2.lastPlayed AS firstPlayedS2,
                     duration.between(lastPlayedS1, r2.lastPlayed).days AS migrationDays
                
                WITH s1.guid AS sourceGuid, s1.name AS sourceName, s1.game AS sourceGame,
                     s2.guid AS targetGuid, s2.name AS targetName, s2.game AS targetGame,
                     COUNT(DISTINCT p) AS playerCount,
                     AVG(migrationDays) AS avgMigrationDays
                WHERE playerCount > 0
                
                RETURN sourceGuid, sourceName, sourceGame,
                       targetGuid, targetName, targetGame,
                       playerCount, avgMigrationDays
                ORDER BY playerCount DESC";

            var cursor = await tx.RunAsync(query, new { startDate, endDate, game });
            var links = new List<MigrationLink>();
            var serverNodes = new Dictionary<string, ServerNode>();

            await foreach (var record in cursor)
            {
                var sourceGuid = record["sourceGuid"].As<string>();
                var targetGuid = record["targetGuid"].As<string>();
                var playerCount = record["playerCount"].As<int>();

                links.Add(new MigrationLink
                {
                    SourceGuid = sourceGuid,
                    TargetGuid = targetGuid,
                    PlayerCount = playerCount,
                    SessionCount = 0, // Would need session data
                    AvgMigrationDays = record["avgMigrationDays"].As<double>()
                });

                // Track server nodes
                if (!serverNodes.ContainsKey(sourceGuid))
                {
                    serverNodes[sourceGuid] = new ServerNode
                    {
                        Guid = sourceGuid,
                        Name = record["sourceName"].As<string>(),
                        Game = record["sourceGame"].As<string>(),
                        Inflow = 0,
                        Outflow = 0,
                        LifecycleStage = "Unknown"
                    };
                }

                if (!serverNodes.ContainsKey(targetGuid))
                {
                    serverNodes[targetGuid] = new ServerNode
                    {
                        Guid = targetGuid,
                        Name = record["targetName"].As<string>(),
                        Game = record["targetGame"].As<string>(),
                        Inflow = 0,
                        Outflow = 0,
                        LifecycleStage = "Unknown"
                    };
                }

                serverNodes[sourceGuid] = serverNodes[sourceGuid] with 
                { 
                    Outflow = serverNodes[sourceGuid].Outflow + playerCount 
                };
                
                serverNodes[targetGuid] = serverNodes[targetGuid] with 
                { 
                    Inflow = serverNodes[targetGuid].Inflow + playerCount 
                };
            }

            // Determine lifecycle stages based on net migration
            foreach (var node in serverNodes.Values)
            {
                var netMigration = node.NetMigration;
                var total = node.Inflow + node.Outflow;
                
                string stage;
                if (total == 0)
                    stage = "Dead";
                else if (netMigration > total * 0.2)
                    stage = "Growing";
                else if (netMigration < -total * 0.2)
                    stage = "Declining";
                else
                    stage = "Stable";

                serverNodes[node.Guid] = node with { LifecycleStage = stage };
            }

            return new PlayerMigrationFlow
            {
                StartDate = startDate,
                EndDate = endDate,
                Links = links,
                Nodes = serverNodes.Values.ToList()
            };
        });
    }

    /// <summary>
    /// Analyze server lifecycle patterns.
    /// </summary>
    public async Task<List<ServerNode>> GetServerLifecycleAnalysisAsync(
        int daysBack = 90,
        CancellationToken cancellationToken = default)
    {
        var endDate = DateTime.UtcNow;
        var startDate = endDate.AddDays(-daysBack);

        var flow = await GetPlayerMigrationFlowAsync(startDate, endDate, cancellationToken: cancellationToken);
        
        // Sort by lifecycle stage and net migration
        return flow.Nodes
            .OrderBy(n => n.LifecycleStage)
            .ThenByDescending(n => Math.Abs(n.NetMigration))
            .ToList();
    }
}