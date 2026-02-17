# Testing Neo4j Integration

## Quick Test Flow

### 1. Start Neo4j

```bash
./scripts/start-neo4j.sh

# Or manually:
docker-compose -f docker-compose.dev.yml up -d neo4j
docker logs bf1942-neo4j -f  # Wait for "Started"
```

### 2. Initialize Neo4j Schema

✅ **Automated!** Schema initialization now happens automatically when the API starts.

Just verify it worked:
1. Open http://localhost:7474
2. Login: `neo4j` / `bf1942stats`
3. Run: `SHOW INDEXES;` - should see several indexes
4. Run: `MATCH (t:MigrationTracker) RETURN t;` - should see migration tracker node

### 3. Enable Neo4j in API

**Option A: Update appsettings.Development.json**
```json
{
  "Neo4j": {
    "Uri": "bolt://localhost:7687",
    "Username": "neo4j",
    "Password": "bf1942stats",
    "Database": "neo4j"
  }
}
```

**Option B: Use environment variables**
```bash
export Neo4j__Uri="bolt://localhost:7687"
export Neo4j__Username="neo4j"
export Neo4j__Password="bf1942stats"
export Neo4j__Database="neo4j"
```

### 4. Start the API

```bash
cd api
dotnet run
```

Check logs for:
```
Neo4j driver initialized: bolt://localhost:7687, Database: neo4j
```

If you see this, Neo4j integration is enabled!

### 5. Run a Test Sync

Get an admin token and sync the last 7 days:

```bash
# Replace with your admin credentials
TOKEN=$(curl -s -X POST http://localhost:5555/stats/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"your-password"}' \
  | jq -r '.accessToken')

# Sync last 7 days
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 7}' | jq
```

Expected response:
```json
{
  "success": true,
  "relationshipsProcessed": 1234,
  "durationMs": 5432,
  "fromDate": "2026-02-10T13:08:04.123Z",
  "toDate": "2026-02-17T13:08:04.123Z",
  "errorMessage": null
}
```

### 6. Verify Data in Neo4j

Go back to Neo4j Browser and run:

```cypher
// Count everything
MATCH (n) RETURN labels(n) AS type, count(*) AS count;

// Should see:
// Player: 100+
// Server: 5+
// Relationships: 500+

// Sample player relationships
MATCH (p:Player)-[r:PLAYED_WITH]-(other:Player)
RETURN p.name, other.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 10;

// Check server relationships
MATCH (p:Player)-[r:PLAYS_ON]->(s:Server)
RETURN p.name, s.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 10;
```

## Manual Testing Scenarios

### Test Case 1: Small Sync (Fast)

```bash
# Sync just 1 day
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 1}'
```

Expected:
- Returns in <10 seconds
- Processes 50-500 relationships (depending on activity)

### Test Case 2: Medium Sync

```bash
# Sync 30 days
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 30}'
```

Expected:
- Returns in 30-120 seconds
- Processes 1000-5000 relationships

### Test Case 3: Large Sync

```bash
# Sync 90 days (initial setup)
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 90}'
```

Expected:
- Returns in 2-5 minutes
- Processes 5000-20000 relationships

### Test Case 4: Re-sync (Idempotent)

Run the same sync twice:

```bash
# First sync
curl -X POST .../neo4j/sync -d '{"days": 7}'

# Second sync (should be faster - updates existing data)
curl -X POST .../neo4j/sync -d '{"days": 7}'
```

Expected:
- Second sync is faster (using MERGE ON MATCH)
- Relationship counts increase (sessionCount updated)
- No duplicate relationships created

### Test Case 5: Invalid Input

```bash
# Too many days
curl -X POST .../neo4j/sync -d '{"days": 500}'
# Should return 400: "Days must be between 1 and 365"

# Zero days
curl -X POST .../neo4j/sync -d '{"days": 0}'
# Should return 400: "Days must be between 1 and 365"

# Without admin token
curl -X POST .../neo4j/sync -d '{"days": 7}'
# Should return 401: Unauthorized
```

### Test Case 6: Neo4j Disabled

1. Set `Neo4j.Uri` to empty string in appsettings
2. Restart API
3. Try to sync:

```bash
curl -X POST .../neo4j/sync -d '{"days": 7}'
```

Expected:
```json
{
  "success": false,
  "errorMessage": "Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json."
}
```

## Query Testing

After syncing, test these queries in Neo4j Browser:

### Query 1: Most Active Players

```cypher
MATCH (p:Player)
RETURN p.name, p.totalSessions, p.lastSeen
ORDER BY p.totalSessions DESC
LIMIT 20;
```

### Query 2: Top Co-Players

```cypher
MATCH (p1:Player)-[r:PLAYED_WITH]-(p2:Player)
RETURN p1.name, p2.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 20;
```

### Query 3: Server Popularity

```cypher
MATCH (p:Player)-[r:PLAYS_ON]->(s:Server)
RETURN s.name, count(p) AS uniquePlayers, sum(r.sessionCount) AS totalSessions
ORDER BY uniquePlayers DESC
LIMIT 10;
```

### Query 4: Recent Connections

```cypher
MATCH (p1:Player)-[r:PLAYED_WITH]-(p2:Player)
WHERE r.firstPlayedTogether > datetime() - duration('P7D')
RETURN p1.name, p2.name, r.firstPlayedTogether
ORDER BY r.firstPlayedTogether DESC
LIMIT 20;
```

### Query 5: Player Network (2 Hops)

```cypher
MATCH path = (p:Player {name: 'YourPlayerName'})-[:PLAYED_WITH*1..2]-(other:Player)
RETURN path
LIMIT 50;
```

This shows the player's direct connections and their connections' connections.

## Performance Testing

### Measure Sync Performance

```bash
# Time the sync
time curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 30}'
```

### Measure Query Performance in Neo4j

```cypher
// Use PROFILE to see execution details
PROFILE
MATCH (p1:Player {name: 'PlayerX'})-[r:PLAYED_WITH]-(p2:Player)
RETURN p2.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 20;
```

Look for:
- **DB Hits**: Should be low (<1000 for simple queries)
- **Index usage**: Should say "NodeIndexSeek" (not "AllNodesScan")
- **Time**: Should be <50ms for simple lookups

## Load Testing

### Concurrent Syncs

⚠️ **Warning:** Don't run this in production!

```bash
# Run 5 syncs concurrently (for testing only)
for i in {1..5}; do
  curl -X POST .../neo4j/sync -d '{"days": 1}' &
done
wait
```

Expected:
- All should succeed (Neo4j handles concurrent writes)
- May be slower than sequential (lock contention)

## Debugging

### Enable Detailed Logging

In `appsettings.Development.json`:
```json
{
  "Logging": {
    "LogLevel": {
      "api.PlayerRelationships": "Debug"
    }
  }
}
```

Restart API and check logs for:
- "Detecting co-play sessions from..."
- "Detected X co-play pairs..."
- "Syncing X relationships to Neo4j"
- "Processing X batches..."

### Check Neo4j Logs

```bash
docker logs bf1942-neo4j -f
```

Look for:
- Connection errors
- Memory warnings
- Slow query warnings

### Test Neo4j Connectivity

In Neo4j Browser:
```cypher
RETURN datetime() AS currentTime;
```

Should return immediately.

### Check Indexes

```cypher
SHOW INDEXES;
```

Should show:
- `player_name_unique` (UNIQUENESS)
- `player_name_index` (RANGE)
- `played_with_session_count` (RANGE)
- `played_with_last_played` (RANGE)
- etc.

If missing, re-run `scripts/init-neo4j.cypher`.

## Integration Tests (TODO)

Example test structure (would go in a test project):

```csharp
[Fact]
public async Task SyncPlayerRelationships_CreatesCorrectGraph()
{
    // Arrange: Create test data in SQLite
    // Act: Run sync
    // Assert: Query Neo4j and verify relationships exist
}

[Fact]
public async Task SyncPlayerRelationships_IsIdempotent()
{
    // Arrange: Create test data
    // Act: Sync twice
    // Assert: No duplicate relationships, counts updated correctly
}

[Fact]
public async Task SyncPlayerRelationships_HandlesDeletedSessions()
{
    // Arrange: Create session, mark as deleted
    // Act: Sync
    // Assert: Deleted session not included in relationships
}
```

## Success Criteria

✅ Neo4j starts and is accessible at http://localhost:7474  
✅ Indexes are created successfully  
✅ API logs "Neo4j driver initialized"  
✅ Sync endpoint returns `success: true`  
✅ Data appears in Neo4j Browser  
✅ Queries return results in <100ms  
✅ Re-sync is idempotent (no duplicates)  

## Common Issues

### "Neo4j integration is not enabled"
→ Update appsettings.Development.json with Neo4j config

### "Connection refused"
→ Start Neo4j: `docker-compose -f docker-compose.dev.yml up -d neo4j`

### "Authentication failed"
→ Check password in docker-compose.dev.yml matches appsettings.json

### Slow queries
→ Run `scripts/init-neo4j.cypher` to create indexes

### Out of memory
→ Increase Neo4j heap in docker-compose.dev.yml

### Sync timeout
→ Reduce number of days, or increase HTTP timeout

## Next Steps After Testing

1. ✅ Verified sync works
2. ✅ Verified data appears in Neo4j
3. ✅ Verified queries are fast
4. → Implement query endpoints (PlayerRelationshipService)
5. → Add background job for automatic sync
6. → Build UI components
7. → Add monitoring/metrics
