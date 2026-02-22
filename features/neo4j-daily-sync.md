# Neo4j Daily Relationship Sync

## Summary

Added automatic Neo4j relationship synchronization to the `DailyAggregateRefreshJob`. This ensures player co-play relationships and player-server relationships are kept up-to-date in the Neo4j graph database.

## Changes Made

### 1. Updated `DailyAggregateRefreshJob.cs`

- **Added import**: `using api.PlayerRelationships;`
- **Added method**: `RefreshNeo4jRelationshipsAsync()` - Syncs last 7 days of relationships incrementally
- **Integrated into daily refresh**: Added `results["neo4jRelationships"]` to the daily job execution
- **Error handling**: Neo4j sync failures don't break the entire daily refresh

### How It Works

```csharp
private async Task<int> RefreshNeo4jRelationshipsAsync(IServiceScope scope, CancellationToken ct)
{
    try
    {
        var relationshipEtl = scope.ServiceProvider.GetRequiredService<PlayerRelationshipEtlService>();
        
        // Sync last 7 days of data daily (catches new rounds + late arrivals)
        var toTime = DateTime.UtcNow;
        var fromTime = toTime.AddDays(-7);
        
        // Sync player co-play relationships
        var result = await relationshipEtl.SyncRelationshipsAsync(fromTime, toTime, ct);
        
        // Also sync player-server relationships
        await relationshipEtl.SyncPlayerServerRelationshipsAsync(fromTime, toTime, ct);
        
        return result.RelationshipsProcessed;
    }
    catch (Exception ex)
    {
        logger.LogError(ex, "Failed to sync Neo4j relationships - continuing with other aggregates");
        return 0;
    }
}
```

## Key Features

### ✅ Incremental Updates
- Syncs **last 7 days** of data each run
- Handles late-arriving data (rounds that complete after initial sync)
- Upsert pattern ensures no duplicates

### ✅ Idempotent
The underlying Neo4j queries use `MERGE` with `ON CREATE`/`ON MATCH`:
```cypher
MERGE (p1)-[r:PLAYED_WITH]-(p2)
ON CREATE SET r.sessionCount = rel.observationCount, ...
ON MATCH SET r.sessionCount = r.sessionCount + rel.observationCount, ...
```
This means relationships are **additive** - running the sync multiple times safely accumulates data.

### ✅ Two Relationship Types
1. **Player ↔ Player** (PLAYED_WITH): Co-play sessions detected from same-timestamp observations
2. **Player → Server** (PLAYS_ON): Player activity on specific servers

### ✅ Fault Tolerant
- Wrapped in try-catch to prevent Neo4j issues from breaking SQLite aggregates
- Logs errors but continues daily refresh execution

## Schedule

Runs daily at **2:00 AM UTC** as part of `DailyAggregateRefreshBackgroundService`.

## Initial Backfill

For the first run, you'll want to backfill historical data. Use the admin endpoint or run manually:

```csharp
// Example: Backfill all data from start of 2024
var fromTime = new DateTime(2024, 1, 1);
var toTime = DateTime.UtcNow;

var result = await relationshipEtl.SyncRelationshipsAsync(fromTime, toTime, cancellationToken);
```

## Monitoring

Look for these log entries:
- `"Syncing Neo4j relationships from {FromTime} to {ToTime}"`
- `"Neo4j sync completed: {RelationshipsProcessed} relationships from {RoundsProcessed} rounds"`
- `"Daily aggregate refresh completed: ... neo4j={Neo4j}"`

## Database Impact

### SQLite
- Read-only queries on `PlayerObservations`, `PlayerSessions`, `Rounds`, `Servers`
- No performance impact on main application

### Neo4j
- Creates/updates `Player` and `Server` nodes
- Creates/updates `PLAYED_WITH` and `PLAYS_ON` relationships
- Uses batched writes (1000 records per batch) for efficiency

## Next Steps

1. **Deploy updated code** with Neo4j connection configured
2. **Run initial backfill** for historical data (if needed)
3. **Monitor logs** to ensure daily sync runs successfully
4. **Query graph** using Neo4j Browser or Cypher queries

Example query to find top co-players:
```cypher
MATCH (p1:Player {name: 'PlayerName'})-[r:PLAYED_WITH]-(p2:Player)
RETURN p2.name, r.sessionCount, r.lastPlayedTogether
ORDER BY r.sessionCount DESC
LIMIT 10
```
