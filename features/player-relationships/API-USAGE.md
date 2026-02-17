# Player Relationships API Usage

## Configuration

### Enable Neo4j Integration

**Production (appsettings.json):**
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

**Development (appsettings.Development.json):**
Neo4j is disabled by default. To enable:
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

**Docker/Kubernetes:**
Use environment variables:
```bash
export Neo4j__Uri="bolt://neo4j-service:7687"
export Neo4j__Username="neo4j"
export Neo4j__Password="your-secure-password"
export Neo4j__Database="neo4j"
```

### Verify Neo4j is Running

```bash
# Check if Neo4j container is up
docker ps | grep neo4j

# Start Neo4j if needed
docker-compose -f docker-compose.dev.yml up -d neo4j

# Access Neo4j browser
open http://localhost:7474
```

## Admin Endpoints

### Sync Player Relationships

**Endpoint:** `POST /stats/admin/data/neo4j/sync`

**Authentication:** Requires Admin role

**Request Body:**
```json
{
  "days": 7
}
```

**Parameters:**
- `days` (optional): Number of days to sync (default: 7, max: 365)

**Response (Success):**
```json
{
  "success": true,
  "relationshipsProcessed": 1523,
  "durationMs": 3421,
  "fromDate": "2026-02-10T13:08:04Z",
  "toDate": "2026-02-17T13:08:04Z",
  "errorMessage": null
}
```

**Response (Failure - Neo4j not configured):**
```json
{
  "success": false,
  "relationshipsProcessed": 0,
  "durationMs": 0,
  "fromDate": null,
  "toDate": null,
  "errorMessage": "Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json."
}
```

**Response (Failure - Invalid days):**
```json
{
  "success": false,
  "errorMessage": "Days must be between 1 and 365"
}
```

### Example Usage with curl

```bash
# Get admin token first (replace with your admin credentials)
TOKEN=$(curl -s -X POST http://localhost:5555/stats/auth/login \
  -H "Content-Type: application/json" \
  -d '{"email":"admin@example.com","password":"your-password"}' \
  | jq -r '.accessToken')

# Sync last 7 days
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 7}'

# Sync last 30 days
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 30}'

# Full sync (365 days - use carefully!)
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 365}'
```

### Example Usage with fetch (JavaScript)

```javascript
// Sync last 7 days
const response = await fetch('/stats/admin/data/neo4j/sync', {
  method: 'POST',
  headers: {
    'Content-Type': 'application/json',
    'Authorization': `Bearer ${accessToken}`
  },
  body: JSON.stringify({ days: 7 })
});

const result = await response.json();
console.log(`Synced ${result.relationshipsProcessed} relationships in ${result.durationMs}ms`);
```

## What Gets Synced

### Player Relationships (PLAYED_WITH)

The sync process:
1. Finds all `PlayerObservations` in the time range
2. Groups by `(ServerGuid, Timestamp)` to find co-play sessions
3. Creates/updates `PLAYED_WITH` relationships between players with:
   - `sessionCount`: Number of times they played together
   - `firstPlayedTogether`: When they first met
   - `lastPlayedTogether`: Most recent co-play session
   - `servers`: List of server GUIDs where they played together

### Player-Server Relationships (PLAYS_ON)

For each player, tracks:
- `sessionCount`: Number of sessions on this server
- `lastPlayed`: Last time they played on this server

### Server Nodes

Basic server info:
- `guid`: Server GUID (unique identifier)
- `name`: Server name
- `game`: Game type (bf1942, fh2, bfvietnam)

## Performance Considerations

### Sync Duration by Days

Approximate sync times (depends on data volume):
- 7 days: 5-30 seconds
- 30 days: 30-120 seconds
- 90 days: 2-5 minutes
- 365 days: 10-30 minutes

### Batching

The ETL processes relationships in batches of 1000 to prevent overwhelming Neo4j. For large syncs, the endpoint may take several minutes.

### Recommended Sync Schedule

**Initial Setup:**
```bash
# Sync last 90 days to build initial graph
curl -X POST .../neo4j/sync -d '{"days": 90}'
```

**Ongoing (Background Job - TODO):**
- Run every 15 minutes: sync last 1 day
- Keeps graph up-to-date with minimal overhead

**Manual Resync:**
```bash
# If you suspect data drift, resync last 30 days
curl -X POST .../neo4j/sync -d '{"days": 30}'
```

## Verifying the Sync

After syncing, check Neo4j Browser (http://localhost:7474):

```cypher
// Count nodes and relationships
MATCH (n) RETURN labels(n) AS type, count(*) AS count;

// Sample some data
MATCH (p:Player)-[r:PLAYED_WITH]-(other:Player)
RETURN p.name, other.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 10;

// Check recent activity
MATCH (p:Player)
WHERE p.lastSeen > datetime() - duration('P7D')
RETURN count(*) AS activePlayers;
```

## Troubleshooting

### "Neo4j integration is not enabled"

1. Check `appsettings.json` or `appsettings.Development.json`
2. Ensure `Neo4j.Uri` is set (not empty string)
3. Restart the API after changing config

### Sync takes forever

1. Check Neo4j indexes are created (run `scripts/init-neo4j.cypher`)
2. Verify Neo4j has enough memory (see docker-compose.dev.yml)
3. Reduce the number of days

### Connection refused / timeout

1. Verify Neo4j is running: `docker ps | grep neo4j`
2. Check Neo4j logs: `docker logs bf1942-neo4j`
3. Test connectivity: `curl http://localhost:7474`

### "Days must be between 1 and 365"

The API limits sync to max 365 days to prevent accidental full database scans. If you need to sync older data, run multiple syncs or adjust the code.

## Next Steps

1. **Schedule background job** to auto-sync every 15 minutes
2. **Build query endpoints** to expose relationship data via API
3. **Add caching** for expensive graph queries
4. **Create UI components** to visualize player networks

See `features/player-relationships/README.md` for more details.
