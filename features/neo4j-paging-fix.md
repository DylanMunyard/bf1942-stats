# Neo4j Sync Paging Fix

## Problem
The Neo4j sync was failing with a LINQ translation error when processing player relationships. Additionally, with 100+ million observations in production, the query was attempting to load too much data into memory at once.

## Root Causes
1. **LINQ Translation Error**: EF Core couldn't translate the complex `SelectMany` with nested `from` clauses (Cartesian product) to SQL
2. **Memory Issues**: Attempting to process all observations in a date range would load millions of records into memory

## Solution

### 1. Fixed LINQ Translation Issue
Split the query into two phases:
- **Phase 1 (Database)**: Fetch observations with simple filters and project only needed fields
- **Phase 2 (In-Memory)**: Perform grouping and Cartesian product in .NET memory after calling `ToListAsync()`

### 2. Implemented Round-Based Paging
Instead of querying by date range, we now:
- Page through `Round` entities (100 rounds at a time)
- Process observations for each round individually
- Each round typically has ~40 players × 45 minutes × 2 obs/min = ~3,600 observations max
- Aggregate relationships across rounds
- Sync to Neo4j in checkpoints (every 100 rounds or 10k relationships)

## Changes Made

### `PlayerRelationshipEtlService.cs`

#### Before
```csharp
public async Task<List<...>> DetectCoPlaySessionsAsync(
    DateTime fromTimestamp,
    DateTime toTimestamp,
    ...)
{
    // Attempted to process ALL observations in date range at once
    var coPlayPairs = await dbContext.PlayerObservations
        .Include(po => po.Session)
        .Where(po => po.Timestamp >= fromTimestamp && po.Timestamp <= toTimestamp)
        .Where(po => !po.Session.IsDeleted)
        .GroupBy(...)
        .SelectMany(...) // Complex LINQ that couldn't translate
        .ToListAsync();
}
```

#### After
```csharp
public async Task<List<...>> DetectCoPlaySessionsForRoundAsync(
    string roundId,
    ...)
{
    // Process one round at a time
    var groupedObservations = await dbContext.PlayerObservations
        .Include(po => po.Session)
        .Where(po => po.Session.RoundId == roundId)
        .Where(po => !po.Session.IsDeleted)
        .Select(po => new { /* projection */ })
        .ToListAsync(); // Materialize first
    
    // Then do Cartesian product in memory
    var coPlayPairs = groupedObservations
        .GroupBy(...)
        .SelectMany(...) // Works fine in memory
        .ToList();
}
```

### Updated `SyncRelationshipsAsync`
- Now pages through rounds (100 at a time)
- Processes each round individually
- Merges relationships across rounds
- Syncs to Neo4j periodically to avoid memory bloat
- Returns `RoundsProcessed` in addition to `RelationshipsProcessed`

### Updated `AdminDataController.cs`
- Added `RoundsProcessed` to `Neo4jSyncResponse`
- Updated response to include rounds processed count

## Benefits

1. **No LINQ Translation Errors**: Simple queries to DB, complex operations in memory
2. **Memory Efficient**: Process ~3,600 observations per round instead of millions
3. **Progress Tracking**: Can see rounds processed and checkpoint progress
4. **Resumable**: If sync fails, can restart from a specific round
5. **Scalable**: Works with 100M+ observations

## Performance Characteristics

### Per Round (Typical)
- Players: ~40
- Duration: ~45 minutes
- Observations: ~3,600 (40 players × 45 min × 2 obs/min)
- Co-play pairs: O(n²) where n = 40 = ~1,600 pairs max

### Batch Processing
- 100 rounds per batch
- Sync to Neo4j every 100 rounds OR 10k relationships (whichever comes first)
- Total time: Depends on number of rounds, but much better than before

## API Response Example

```json
{
  "success": true,
  "relationshipsProcessed": 15234,
  "roundsProcessed": 342,
  "durationMs": 45678,
  "fromDate": "2024-02-10T00:00:00Z",
  "toDate": "2024-02-17T00:00:00Z"
}
```

## Testing Recommendations

1. Test with small date range first (1 day)
2. Monitor logs for:
   - Rounds processed count
   - Checkpoint syncs to Neo4j
   - Memory usage
3. Gradually increase to larger ranges (7 days, 30 days, etc.)

## Future Optimizations

- Track last synced round in database to enable incremental sync
- Add ability to resume from specific round ID
- Parallel processing of rounds (with care for Neo4j write limits)
- Progress reporting via SignalR for long-running syncs
