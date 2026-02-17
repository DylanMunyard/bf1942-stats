using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using Neo4j.Driver;

namespace api.PlayerRelationships;

/// <summary>
/// ETL service that syncs PlayerObservations from SQLite to Neo4j graph database.
/// Detects co-play sessions (players online at the same time on the same server)
/// and creates/updates relationship edges in Neo4j.
/// </summary>
public class PlayerRelationshipEtlService(
    PlayerTrackerDbContext dbContext,
    Neo4jService neo4jService,
    ILogger<PlayerRelationshipEtlService> logger)
{
    /// <summary>
    /// Find co-play sessions for a specific round by grouping observations with the same timestamp.
    /// This is the core detection logic: if players have observations at the same time
    /// on the same server, they played together.
    /// </summary>
    public async Task<List<(string Player1, string Player2, DateTime Timestamp, string ServerGuid)>> 
        DetectCoPlaySessionsForRoundAsync(
            string roundId,
            CancellationToken cancellationToken = default)
    {
        logger.LogDebug("Detecting co-play sessions for round {RoundId}", roundId);

        // Query: Find all observations for sessions in this round
        // Note: We materialize the grouped data first, then do the Cartesian product in memory
        // because EF Core can't translate the complex SelectMany to SQL
        var groupedObservations = await dbContext.PlayerObservations
            .Include(po => po.Session)
            .Where(po => po.Session.RoundId == roundId)
            .Where(po => !po.Session.IsDeleted) // Exclude deleted sessions
            .Select(po => new
            {
                PlayerName = po.Session.PlayerName,
                po.Timestamp,
                ServerGuid = po.Session.ServerGuid
            })
            .ToListAsync(cancellationToken);

        if (groupedObservations.Count == 0)
        {
            return [];
        }

        // Trim and filter player names in memory to avoid whitespace issues
        var validObservations = groupedObservations
            .Select(po => new
            {
                PlayerName = po.PlayerName?.Trim() ?? "",
                po.Timestamp,
                po.ServerGuid
            })
            .Where(po => !string.IsNullOrEmpty(po.PlayerName))
            .ToList();

        if (validObservations.Count == 0)
        {
            logger.LogDebug("No valid observations after filtering empty names for round {RoundId}", roundId);
            return [];
        }

        // Group and create pairs in memory (client-side evaluation)
        var coPlayPairs = validObservations
            .GroupBy(po => new { po.ServerGuid, po.Timestamp })
            .Where(g => g.Count() > 1) // Only groups with multiple players
            .SelectMany(g =>
                // Create pairs of all players in this group (cartesian product)
                from p1 in g
                from p2 in g
                where string.Compare(p1.PlayerName, p2.PlayerName, StringComparison.Ordinal) < 0
                select new
                {
                    Player1 = p1.PlayerName,
                    Player2 = p2.PlayerName,
                    Timestamp = p1.Timestamp,
                    ServerGuid = p1.ServerGuid
                })
            .Distinct()
            .ToList();

        return coPlayPairs
            .Select(p => (p.Player1, p.Player2, p.Timestamp, p.ServerGuid))
            .ToList();
    }

    /// <summary>
    /// Aggregate co-play pairs into relationships with session counts and durations.
    /// Groups by player pair and calculates metrics.
    /// </summary>
    public Dictionary<(string, string), RelationshipMetrics> AggregateRelationships(
        List<(string Player1, string Player2, DateTime Timestamp, string ServerGuid)> coPlayPairs)
    {
        var relationships = new Dictionary<(string, string), RelationshipMetrics>();

        foreach (var pair in coPlayPairs)
        {
            var key = (pair.Player1, pair.Player2);
            
            if (!relationships.ContainsKey(key))
            {
                relationships[key] = new RelationshipMetrics
                {
                    Player1Name = pair.Player1,
                    Player2Name = pair.Player2,
                    FirstSeen = pair.Timestamp,
                    LastSeen = pair.Timestamp,
                    ServerGuids = [pair.ServerGuid],
                    ObservationCount = 1
                };
            }
            else
            {
                var metrics = relationships[key];
                metrics.ObservationCount++;
                
                if (pair.Timestamp < metrics.FirstSeen)
                    metrics.FirstSeen = pair.Timestamp;
                
                if (pair.Timestamp > metrics.LastSeen)
                    metrics.LastSeen = pair.Timestamp;
                
                if (!metrics.ServerGuids.Contains(pair.ServerGuid))
                    metrics.ServerGuids.Add(pair.ServerGuid);
            }
        }

        return relationships;
    }

    /// <summary>
    /// Sync detected relationships to Neo4j.
    /// Uses batch MERGE to create/update Player nodes and PLAYED_WITH relationships.
    /// </summary>
    public async Task SyncToNeo4jAsync(
        Dictionary<(string, string), RelationshipMetrics> relationships,
        CancellationToken cancellationToken = default)
    {
        if (relationships.Count == 0)
        {
            logger.LogInformation("No relationships to sync to Neo4j");
            return;
        }

        logger.LogInformation("Syncing {Count} relationships to Neo4j", relationships.Count);

        var relationshipData = relationships.Select(kvp => new Dictionary<string, object>
        {
            ["player1"] = kvp.Key.Item1,
            ["player2"] = kvp.Key.Item2,
            ["observationCount"] = kvp.Value.ObservationCount,
            ["firstSeen"] = kvp.Value.FirstSeen.ToString("o"), // ISO 8601
            ["lastSeen"] = kvp.Value.LastSeen.ToString("o"),
            ["serverGuids"] = kvp.Value.ServerGuids
        }).ToList();

        // Batch size for processing (prevent overwhelming Neo4j)
        const int batchSize = 1000;
        var batches = relationshipData.Chunk(batchSize).ToList();

        logger.LogInformation("Processing {BatchCount} batches of {BatchSize} relationships", 
            batches.Count, batchSize);

        var totalProcessed = 0;
        foreach (var batch in batches)
        {
            await neo4jService.ExecuteWriteAsync(async tx =>
            {
                // Cypher query to merge players and relationships
                var query = @"
                    UNWIND $relationships AS rel
                    MERGE (p1:Player {name: rel.player1})
                    ON CREATE SET p1.firstSeen = datetime(rel.firstSeen),
                                  p1.lastSeen = datetime(rel.lastSeen),
                                  p1.totalSessions = 0
                    ON MATCH SET p1.lastSeen = CASE 
                        WHEN datetime(rel.lastSeen) > p1.lastSeen 
                        THEN datetime(rel.lastSeen) 
                        ELSE p1.lastSeen 
                    END
                    
                    MERGE (p2:Player {name: rel.player2})
                    ON CREATE SET p2.firstSeen = datetime(rel.firstSeen),
                                  p2.lastSeen = datetime(rel.lastSeen),
                                  p2.totalSessions = 0
                    ON MATCH SET p2.lastSeen = CASE 
                        WHEN datetime(rel.lastSeen) > p2.lastSeen 
                        THEN datetime(rel.lastSeen) 
                        ELSE p2.lastSeen 
                    END
                    
                    MERGE (p1)-[r:PLAYED_WITH]-(p2)
                    ON CREATE SET r.sessionCount = rel.observationCount,
                                  r.firstPlayedTogether = datetime(rel.firstSeen),
                                  r.lastPlayedTogether = datetime(rel.lastSeen),
                                  r.servers = rel.serverGuids
                    ON MATCH SET r.sessionCount = r.sessionCount + rel.observationCount,
                                 r.lastPlayedTogether = CASE
                                     WHEN datetime(rel.lastSeen) > r.lastPlayedTogether
                                     THEN datetime(rel.lastSeen)
                                     ELSE r.lastPlayedTogether
                                 END,
                                 r.servers = CASE
                                     WHEN size([x IN rel.serverGuids WHERE NOT x IN r.servers]) > 0
                                     THEN r.servers + [x IN rel.serverGuids WHERE NOT x IN r.servers]
                                     ELSE r.servers
                                 END
                    
                    RETURN count(*) as processed";

                var result = await tx.RunAsync(query, new { relationships = batch });
                var summary = await result.ConsumeAsync();
                return summary.Counters.NodesCreated + summary.Counters.RelationshipsCreated;
            });

            totalProcessed += batch.Count();
            logger.LogDebug("Processed batch: {Processed}/{Total}", totalProcessed, relationshipData.Count);
        }

        logger.LogInformation(
            "Successfully synced {Count} relationships to Neo4j",
            relationships.Count);
    }

    /// <summary>
    /// Full sync process: page through rounds, detect co-play sessions, and sync to Neo4j.
    /// This processes rounds in batches to handle large datasets (100M+ observations).
    /// </summary>
    public async Task<SyncResult> SyncRelationshipsAsync(
        DateTime fromTimestamp,
        DateTime toTimestamp,
        CancellationToken cancellationToken = default)
    {
        var startTime = DateTime.UtcNow;
        logger.LogInformation(
            "Starting relationship sync from {FromTime} to {ToTime}",
            fromTimestamp,
            toTimestamp);

        const int roundBatchSize = 100; // Process 100 rounds at a time
        var totalRelationshipsProcessed = 0;
        var roundsProcessed = 0;
        var allRelationships = new Dictionary<(string, string), RelationshipMetrics>();

        // Get total count for progress tracking
        var totalRounds = await dbContext.Rounds
            .Where(r => !r.IsDeleted)
            .Where(r => r.StartTime >= fromTimestamp && r.StartTime <= toTimestamp)
            .CountAsync(cancellationToken);

        logger.LogInformation("Found {TotalRounds} rounds to process", totalRounds);

        if (totalRounds == 0)
        {
            return new SyncResult
            {
                Success = true,
                RelationshipsProcessed = 0,
                RoundsProcessed = 0,
                Duration = DateTime.UtcNow - startTime
            };
        }

        // Page through rounds
        var offset = 0;
        while (offset < totalRounds)
        {
            var roundBatch = await dbContext.Rounds
                .Where(r => !r.IsDeleted)
                .Where(r => r.StartTime >= fromTimestamp && r.StartTime <= toTimestamp)
                .OrderBy(r => r.StartTime)
                .Skip(offset)
                .Take(roundBatchSize)
                .Select(r => r.RoundId)
                .ToListAsync(cancellationToken);

            if (roundBatch.Count == 0)
                break;

            logger.LogInformation(
                "Processing rounds {From}-{To} of {Total}",
                offset + 1,
                offset + roundBatch.Count,
                totalRounds);

            // Process each round in the batch
            foreach (var roundId in roundBatch)
            {
                var coPlayPairs = await DetectCoPlaySessionsForRoundAsync(roundId, cancellationToken);
                
                if (coPlayPairs.Count > 0)
                {
                    // Aggregate relationships for this round
                    var roundRelationships = AggregateRelationships(coPlayPairs);
                    
                    // Merge into the global relationship dictionary
                    MergeRelationships(allRelationships, roundRelationships);
                }

                roundsProcessed++;
                
                // Sync to Neo4j periodically (every 100 rounds or when we have 10k+ relationships)
                if (roundsProcessed % 100 == 0 || allRelationships.Count >= 10000)
                {
                    if (allRelationships.Count > 0)
                    {
                        logger.LogInformation(
                            "Syncing {Count} relationships to Neo4j (checkpoint at round {Round})",
                            allRelationships.Count,
                            roundsProcessed);
                        
                        await SyncToNeo4jAsync(allRelationships, cancellationToken);
                        totalRelationshipsProcessed += allRelationships.Count;
                        allRelationships.Clear();
                    }
                }
            }

            offset += roundBatch.Count;
        }

        // Final sync for any remaining relationships
        if (allRelationships.Count > 0)
        {
            logger.LogInformation(
                "Syncing final {Count} relationships to Neo4j",
                allRelationships.Count);
            
            await SyncToNeo4jAsync(allRelationships, cancellationToken);
            totalRelationshipsProcessed += allRelationships.Count;
        }

        var duration = DateTime.UtcNow - startTime;
        logger.LogInformation(
            "Relationship sync completed in {Duration}s: {RoundsProcessed} rounds, {RelationshipsProcessed} relationships",
            duration.TotalSeconds,
            roundsProcessed,
            totalRelationshipsProcessed);

        return new SyncResult
        {
            Success = true,
            RelationshipsProcessed = totalRelationshipsProcessed,
            RoundsProcessed = roundsProcessed,
            Duration = duration
        };
    }

    /// <summary>
    /// Merge relationships from a round into the global dictionary, combining metrics.
    /// </summary>
    private void MergeRelationships(
        Dictionary<(string, string), RelationshipMetrics> target,
        Dictionary<(string, string), RelationshipMetrics> source)
    {
        foreach (var (key, metrics) in source)
        {
            if (!target.ContainsKey(key))
            {
                target[key] = metrics;
            }
            else
            {
                var existing = target[key];
                existing.ObservationCount += metrics.ObservationCount;
                
                if (metrics.FirstSeen < existing.FirstSeen)
                    existing.FirstSeen = metrics.FirstSeen;
                
                if (metrics.LastSeen > existing.LastSeen)
                    existing.LastSeen = metrics.LastSeen;
                
                // Merge server GUIDs
                foreach (var serverGuid in metrics.ServerGuids)
                {
                    if (!existing.ServerGuids.Contains(serverGuid))
                        existing.ServerGuids.Add(serverGuid);
                }
            }
        }
    }

    /// <summary>
    /// Sync player-server relationships (who plays on which servers).
    /// </summary>
    public async Task SyncPlayerServerRelationshipsAsync(
        DateTime fromTimestamp,
        DateTime toTimestamp,
        CancellationToken cancellationToken = default)
    {
        logger.LogInformation(
            "Syncing player-server relationships from {FromTime} to {ToTime}",
            fromTimestamp,
            toTimestamp);

        // Aggregate player activity per server
        var playerServerData = await dbContext.PlayerSessions
            .Where(ps => ps.LastSeenTime >= fromTimestamp && ps.LastSeenTime <= toTimestamp)
            .Where(ps => !ps.IsDeleted)
            .GroupBy(ps => new { ps.PlayerName, ps.ServerGuid })
            .Select(g => new
            {
                g.Key.PlayerName,
                g.Key.ServerGuid,
                SessionCount = g.Count(),
                LastPlayed = g.Max(ps => ps.LastSeenTime)
            })
            .ToListAsync(cancellationToken);

        if (playerServerData.Count == 0)
        {
            logger.LogInformation("No player-server relationships to sync");
            return;
        }

        // Trim player names and filter out empty ones
        var validPlayerServerData = playerServerData
            .Select(ps => new
            {
                PlayerName = ps.PlayerName?.Trim() ?? "",
                ps.ServerGuid,
                ps.SessionCount,
                ps.LastPlayed
            })
            .Where(ps => !string.IsNullOrEmpty(ps.PlayerName))
            .ToList();

        if (validPlayerServerData.Count == 0)
        {
            logger.LogInformation("No valid player-server relationships after filtering empty names");
            return;
        }

        // Get server info for MERGE
        var serverGuids = validPlayerServerData.Select(ps => ps.ServerGuid).Distinct().ToList();
        var servers = await dbContext.Servers
            .Where(s => serverGuids.Contains(s.Guid))
            .Select(s => new { s.Guid, s.Name, s.Game })
            .ToListAsync(cancellationToken);

        var serverLookup = servers.ToDictionary(s => s.Guid);

        // Batch sync to Neo4j
        const int batchSize = 1000;
        var batches = validPlayerServerData.Chunk(batchSize);

        foreach (var batch in batches)
        {
            await neo4jService.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                    UNWIND $relationships AS rel
                    MERGE (p:Player {name: rel.playerName})
                    MERGE (s:Server {guid: rel.serverGuid})
                    ON CREATE SET s.name = rel.serverName,
                                  s.game = rel.game
                    
                    MERGE (p)-[r:PLAYS_ON]->(s)
                    ON CREATE SET r.sessionCount = rel.sessionCount,
                                  r.lastPlayed = datetime(rel.lastPlayed)
                    ON MATCH SET r.sessionCount = r.sessionCount + rel.sessionCount,
                                 r.lastPlayed = CASE
                                     WHEN datetime(rel.lastPlayed) > r.lastPlayed
                                     THEN datetime(rel.lastPlayed)
                                     ELSE r.lastPlayed
                                 END
                    
                    RETURN count(*) as processed";

                var relationshipData = batch.Select(ps => new Dictionary<string, object>
                {
                    ["playerName"] = ps.PlayerName,
                    ["serverGuid"] = ps.ServerGuid,
                    ["serverName"] = serverLookup.TryGetValue(ps.ServerGuid, out var server) ? server.Name : "Unknown",
                    ["game"] = serverLookup.TryGetValue(ps.ServerGuid, out var srv) ? srv.Game : "unknown",
                    ["sessionCount"] = ps.SessionCount,
                    ["lastPlayed"] = ps.LastPlayed.ToString("o")
                }).ToList();

                await tx.RunAsync(query, new { relationships = relationshipData });
                return true;
            });
        }

        logger.LogInformation(
            "Synced {Count} player-server relationships to Neo4j",
            playerServerData.Count);
    }

    public class RelationshipMetrics
    {
        public required string Player1Name { get; set; }
        public required string Player2Name { get; set; }
        public DateTime FirstSeen { get; set; }
        public DateTime LastSeen { get; set; }
        public int ObservationCount { get; set; }
        public List<string> ServerGuids { get; set; } = [];
    }

    public class SyncResult
    {
        public bool Success { get; set; }
        public int RelationshipsProcessed { get; set; }
        public int RoundsProcessed { get; set; }
        public TimeSpan Duration { get; set; }
        public string? ErrorMessage { get; set; }
    }
}
