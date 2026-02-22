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
}