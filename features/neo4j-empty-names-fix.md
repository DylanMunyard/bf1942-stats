# Neo4j Empty Player Names Issue

## Problem
When querying Neo4j relationships, `p1.name` returns empty strings (""). This indicates that Player nodes were created with empty names during the back-fill.

## Root Cause
In `PlayerRelationshipEtlService.cs`, the code uses `po.Session.PlayerName` to identify players:

```csharp
.Select(po => new
{
    PlayerName = po.Session.PlayerName,  // Can be empty!
    po.Timestamp,
    ServerGuid = po.Session.ServerGuid
})
```

If PlayerSessions have empty `PlayerName` values in the database, those empty strings are synced to Neo4j, creating `(:Player {name: ""})` nodes.

## Investigation Queries

### Check SQLite for Empty Names
```sql
-- Count sessions with empty player names
SELECT COUNT(*) 
FROM PlayerSessions 
WHERE PlayerName = '' OR PlayerName IS NULL;

-- Sample sessions with empty names
SELECT * 
FROM PlayerSessions 
WHERE PlayerName = '' OR PlayerName IS NULL
LIMIT 10;

-- Count observations linked to empty-name sessions
SELECT COUNT(*)
FROM PlayerObservations po
JOIN PlayerSessions ps ON po.SessionId = ps.SessionId
WHERE ps.PlayerName = '' OR ps.PlayerName IS NULL;
```

### Check Neo4j for Empty Names
```cypher
// Count players with empty names
MATCH (p:Player)
WHERE p.name = ""
RETURN count(p) as emptyNamePlayers;

// Get relationships involving empty-name players
MATCH (p:Player {name: ""})-[r:PLAYED_WITH]-(other:Player)
RETURN other.name, count(r) as relationshipCount
ORDER BY relationshipCount DESC
LIMIT 20;

// Check total impact
MATCH ()-[r:PLAYED_WITH]-(p:Player {name: ""})
RETURN count(r) as totalCorruptedRelationships;
```

## Solutions

### Option 1: Filter Out Empty Names (Recommended)
Add filtering to prevent empty names from being synced:

```csharp
var groupedObservations = await dbContext.PlayerObservations
    .Include(po => po.Session)
    .Where(po => po.Session.RoundId == roundId)
    .Where(po => !po.Session.IsDeleted)
    .Where(po => po.Session.PlayerName != null && po.Session.PlayerName != "") // ADD THIS
    .Select(po => new
    {
        PlayerName = po.Session.PlayerName,
        po.Timestamp,
        ServerGuid = po.Session.ServerGuid
    })
    .ToListAsync(cancellationToken);
```

Also add filtering in `SyncPlayerServerRelationshipsAsync`:

```csharp
var playerServerData = await dbContext.PlayerSessions
    .Where(ps => ps.LastSeenTime >= fromTimestamp && ps.LastSeenTime <= toTimestamp)
    .Where(ps => !ps.IsDeleted)
    .Where(ps => ps.PlayerName != null && ps.PlayerName != "") // ADD THIS
    .GroupBy(ps => new { ps.PlayerName, ps.ServerGuid })
    // ...
```

### Option 2: Clean Up Neo4j Database
```cypher
// Delete all empty-name players and their relationships
MATCH (p:Player {name: ""})
DETACH DELETE p;

// Verify cleanup
MATCH (p:Player)
WHERE p.name = ""
RETURN count(p);  // Should be 0
```

### Option 3: Fix Source Data (Most Thorough)
If empty player names are a data quality issue in PlayerSessions:

1. Investigate why PlayerSessions have empty names
2. Mark those sessions as deleted or fix the names
3. Re-run the back-fill

## Recommended Action Plan

1. **Investigate the scope**:
   ```sql
   SELECT COUNT(*) as total_sessions FROM PlayerSessions;
   SELECT COUNT(*) as empty_name_sessions 
   FROM PlayerSessions 
   WHERE PlayerName = '' OR PlayerName IS NULL;
   ```

2. **Clean Neo4j** (remove corrupted data):
   ```cypher
   MATCH (p:Player {name: ""})
   DETACH DELETE p;
   ```

3. **Fix the ETL code** (add filtering):
   - Update `DetectCoPlaySessionsForRoundAsync`
   - Update `SyncPlayerServerRelationshipsAsync`

4. **Re-run back-fill** with fixed code (or run incremental sync if possible)

5. **Add validation** to prevent future issues:
   ```csharp
   if (string.IsNullOrWhiteSpace(playerName))
   {
       logger.LogWarning("Skipping observation with empty player name");
       continue;
   }
   ```

## Testing After Fix

```cypher
// Should return 0
MATCH (p:Player)
WHERE p.name = "" OR p.name IS NULL
RETURN count(p);

// Verify relationships are clean
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name <> "" AND p2.name <> ""
RETURN p1.name, p2.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 10;
```

