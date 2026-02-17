# Neo4j Player Name Whitespace Issue

## Problem
- One Player node with completely empty name (`""`)
- Multiple Player nodes with leading/trailing whitespace in names
- This causes queries to show blank names or names with unexpected spacing

## Root Cause
The ETL code in `PlayerRelationshipEtlService.cs` doesn't trim player names before syncing to Neo4j. If the source data (PlayerSessions) has untrimmed names, they're synced as-is.

## Cleanup Steps

### 1. Delete the Empty-Name Player
```cypher
// Delete the player with empty name and all relationships
MATCH (p:Player {name: ""})
DETACH DELETE p;

// Verify it's gone
MATCH (p:Player {name: ""})
RETURN count(p);  // Should return 0
```

### 2. Fix Whitespace in Existing Players
```cypher
// Option A: Trim all player names in place
MATCH (p:Player)
WHERE p.name <> trim(p.name)
SET p.name = trim(p.name)
RETURN count(p) as playersFixed;

// Option B: More cautious - see what will change first
MATCH (p:Player)
WHERE p.name <> trim(p.name)
RETURN p.name as original, trim(p.name) as trimmed, size(p.name) as originalLength
LIMIT 20;
```

**Warning**: If two players have names that only differ by whitespace (e.g., " Bob" and "Bob"), trimming will cause a constraint violation since player names must be unique. Check first:

```cypher
// Check for potential duplicates after trimming
MATCH (p:Player)
WITH trim(p.name) as trimmedName, collect(p.name) as originalNames
WHERE size(originalNames) > 1
RETURN trimmedName, originalNames;
```

If duplicates exist, you'll need to merge them:

```cypher
// Merge duplicate players (if any)
MATCH (p1:Player), (p2:Player)
WHERE p1.name <> p2.name 
  AND trim(p1.name) = trim(p2.name)
  AND id(p1) < id(p2)  // Process each pair only once
WITH p1, p2, trim(p1.name) as canonicalName

// Merge all relationships from p2 to p1
OPTIONAL MATCH (p2)-[r:PLAYED_WITH]-(other:Player)
WHERE other <> p1
MERGE (p1)-[r2:PLAYED_WITH]-(other)
ON CREATE SET r2.sessionCount = r.sessionCount,
              r2.firstPlayedTogether = r.firstPlayedTogether,
              r2.lastPlayedTogether = r.lastPlayedTogether,
              r2.servers = r.servers
ON MATCH SET r2.sessionCount = r2.sessionCount + r.sessionCount,
             r2.lastPlayedTogether = CASE 
                 WHEN r.lastPlayedTogether > r2.lastPlayedTogether 
                 THEN r.lastPlayedTogether 
                 ELSE r2.lastPlayedTogether 
             END,
             r2.servers = r2.servers + [x IN r.servers WHERE NOT x IN r2.servers]

WITH p1, p2, canonicalName
DETACH DELETE p2
SET p1.name = canonicalName
RETURN count(*) as playersMerged;
```

## Code Fix: Add Trimming to ETL

### Update `PlayerRelationshipEtlService.cs`

**In `DetectCoPlaySessionsForRoundAsync`:**

```csharp
var groupedObservations = await dbContext.PlayerObservations
    .Include(po => po.Session)
    .Where(po => po.Session.RoundId == roundId)
    .Where(po => !po.Session.IsDeleted)
    .Select(po => new
    {
        PlayerName = po.Session.PlayerName,  // Will trim in next step
        po.Timestamp,
        ServerGuid = po.Session.ServerGuid
    })
    .ToListAsync(cancellationToken);

if (groupedObservations.Count == 0)
{
    return [];
}

// Trim and filter player names in memory
var validObservations = groupedObservations
    .Select(po => new
    {
        PlayerName = po.PlayerName?.Trim() ?? "",
        po.Timestamp,
        po.ServerGuid
    })
    .Where(po => !string.IsNullOrEmpty(po.PlayerName))  // Filter out empty names
    .ToList();

// Group and create pairs using validObservations instead of groupedObservations
var coPlayPairs = validObservations
    .GroupBy(po => new { po.ServerGuid, po.Timestamp })
    .Where(g => g.Count() > 1)
    .SelectMany(g =>
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
```

**In `SyncPlayerServerRelationshipsAsync`:**

```csharp
var playerServerData = await dbContext.PlayerSessions
    .Where(ps => ps.LastSeenTime >= fromTimestamp && ps.LastSeenTime <= toTimestamp)
    .Where(ps => !ps.IsDeleted)
    .GroupBy(ps => new { ps.PlayerName, ps.ServerGuid })
    .Select(g => new
    {
        PlayerName = g.Key.PlayerName,  // Will trim in next step
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
    logger.LogInformation("No valid player-server relationships after filtering");
    return;
}

// Continue with validPlayerServerData instead of playerServerData
// ...
```

## Alternative: Trim at Database Level

You could also trim names in the source database if they're not already trimmed:

```sql
-- Check for whitespace in PlayerSessions
SELECT PlayerName, length(PlayerName) as len, length(trim(PlayerName)) as trimLen
FROM PlayerSessions
WHERE PlayerName != trim(PlayerName)
LIMIT 10;

-- Fix if needed (be careful!)
UPDATE PlayerSessions
SET PlayerName = trim(PlayerName)
WHERE PlayerName != trim(PlayerName);
```

## Testing After Fix

```cypher
// Should return 0
MATCH (p:Player)
WHERE p.name = "" OR p.name IS NULL
RETURN count(p);

// Should return 0
MATCH (p:Player)
WHERE p.name <> trim(p.name)
RETURN count(p);

// Should show clean data
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
RETURN p1.name, p2.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 20;
```

## Recommended Action Plan

1. **Check for duplicate issues**:
   ```cypher
   MATCH (p:Player)
   WITH trim(p.name) as trimmedName, collect(p.name) as originalNames
   WHERE size(originalNames) > 1
   RETURN trimmedName, originalNames;
   ```

2. **Clean up existing data**:
   - Delete empty-name player
   - Merge duplicates if any
   - Trim existing player names

3. **Update ETL code** to trim names before syncing

4. **Re-run back-fill** to ensure all data is clean (or just run incremental sync going forward)

5. **Add validation** in Neo4j constraints/migrations if possible

