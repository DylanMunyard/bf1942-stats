# Neo4j Back-fill Testing Summary

## Issue Found: Player Name Whitespace

### Problem
- 1 Player node with empty name (`""`)
- Multiple Player nodes with leading/trailing whitespace (e.g., `" PlayerName"`)
- Causes queries to show blank or oddly-spaced names

### Root Cause
The ETL code didn't trim player names before syncing to Neo4j. If source data had untrimmed names, they were synced as-is.

### Fix Applied
✅ Updated `PlayerRelationshipEtlService.cs`:
- Added `.Trim()` to player names in `DetectCoPlaySessionsForRoundAsync`
- Added `.Trim()` to player names in `SyncPlayerServerRelationshipsAsync`
- Added filter to exclude empty/null names after trimming
- Added debug logging when names are filtered out

### Cleanup Required
Run the cleanup script to fix existing data:

```bash
# Open Neo4j Browser at http://localhost:7474
# Copy/paste queries from cleanup-neo4j-whitespace.cypher
```

**Steps:**
1. **Check for issues** (Step 1 in cleanup script)
2. **Delete empty-name player** (Step 2)
3. **Check for duplicates** (Step 3) - preview first, then uncomment merge if needed
4. **Trim all player names** (Step 4)
5. **Verify cleanup** (Step 5)

### Testing the Back-fill

After cleanup, use queries from `features/neo4j-backfill-testing.md`:

**Quick validation:**
```cypher
// Top players by co-play time (should show clean names now)
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
RETURN p1.name, p2.name, r.sessionCount, r.totalMinutes
ORDER BY r.totalMinutes DESC
LIMIT 20;

// No empty or whitespace names
MATCH (p:Player)
WHERE p.name = "" OR p.name IS NULL OR p.name <> trim(p.name)
RETURN count(p);  // Should be 0
```

### Re-running Back-fill (Optional)

If you want to re-run with the fixed code:

1. **Clear Neo4j data:**
   ```cypher
   MATCH (n) DETACH DELETE n;
   ```

2. **Run back-fill API:**
   ```bash
   curl -X POST "http://localhost:5000/api/admin/neo4j/sync?fromDate=2024-01-01&toDate=2024-12-31"
   ```

3. **Monitor progress:**
   - Check logs for round processing
   - Watch for "Syncing X relationships to Neo4j" messages
   - Look for any warnings about filtered names

### Expected Results After Fix

- **No empty player names**: All players have valid, trimmed names
- **Clean relationships**: All PLAYED_WITH edges connect valid players
- **Accurate data**: Player names match source database (trimmed)

### Files Created

- `cleanup-neo4j-whitespace.cypher` - Step-by-step cleanup script
- `features/neo4j-whitespace-fix.md` - Detailed explanation and alternatives
- `features/neo4j-backfill-testing.md` - 22 test queries for validation
- `check-neo4j-whitespace.cypher` - Quick diagnostic queries

