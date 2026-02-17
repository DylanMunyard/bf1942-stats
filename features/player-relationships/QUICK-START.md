# Neo4j Player Relationships - Quick Start

## 5-Minute Setup

### 1. Start Neo4j (30 seconds)

```bash
./scripts/start-neo4j.sh
```

**Access:** http://localhost:7474  
**Login:** `neo4j` / `bf1942stats`

### 2. Enable in API (30 seconds)

⚠️ **Schema initialization is now automatic!** No manual steps needed.

Edit `api/appsettings.Development.json`:
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

### 3. Start API (1 minute)

```bash
cd api && dotnet run
```

Look for in logs:
```
Neo4j driver initialized: bolt://localhost:7687
Checking for pending Neo4j migrations...
Applying migration: 001_InitialSchema.cypher
Successfully applied migration 001_InitialSchema.cypher
All migrations applied successfully
```

✅ **Schema created automatically!**

### 4. Run First Sync (2 minutes)

```bash
# Get admin token
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

### 5. Query! (1 minute)

In Neo4j Browser:
```cypher
// See who plays with whom
MATCH (p1:Player)-[r:PLAYED_WITH]-(p2:Player)
RETURN p1.name, p2.name, r.sessionCount
ORDER BY r.sessionCount DESC
LIMIT 10;
```

## Common Queries

### Most Frequent Co-Players
```cypher
MATCH (p1:Player {name: 'YourPlayerName'})-[r:PLAYED_WITH]-(p2:Player)
RETURN p2.name, r.sessionCount, r.lastPlayedTogether
ORDER BY r.sessionCount DESC
LIMIT 20;
```

### Potential Connections (Same Server, Never Met)
```cypher
MATCH (p1:Player {name: 'YourPlayerName'})-[:PLAYS_ON]->(s:Server)<-[:PLAYS_ON]-(p2:Player)
WHERE NOT (p1)-[:PLAYED_WITH]-(p2)
  AND p2.lastSeen > datetime() - duration('P30D')
RETURN p2.name, s.name
LIMIT 20;
```

### Recent Friendships (Last 7 Days)
```cypher
MATCH (p1:Player {name: 'YourPlayerName'})-[r:PLAYED_WITH]-(p2:Player)
WHERE r.firstPlayedTogether > datetime() - duration('P7D')
RETURN p2.name, r.firstPlayedTogether
ORDER BY r.firstPlayedTogether DESC;
```

### Player Network Visualization
```cypher
MATCH path = (p:Player {name: 'YourPlayerName'})-[:PLAYED_WITH*1..2]-(other)
RETURN path
LIMIT 50;
```

## Troubleshooting

| Problem | Solution |
|---------|----------|
| "Neo4j integration is not enabled" | Update `appsettings.Development.json` with Neo4j config |
| Connection refused | Start Neo4j: `docker-compose -f docker-compose.dev.yml up -d neo4j` |
| Queries are slow | Run `scripts/init-neo4j.cypher` to create indexes |
| Can't login to Neo4j | Password is `bf1942stats` (set in docker-compose.dev.yml) |
| Sync takes forever | Check indexes exist, or reduce days parameter |

## Files Reference

- **Setup:** `features/player-relationships/GETTING-STARTED.md`
- **Testing:** `features/player-relationships/TESTING.md`
- **API Usage:** `features/player-relationships/API-USAGE.md`
- **Query Examples:** `scripts/neo4j-queries-reference.cypher`
- **Schema Init:** `scripts/init-neo4j.cypher`
- **Architecture:** `features/player-relationships/README.md`

## Key Concepts

**Co-Play Detection:**  
Players with observations at the same timestamp on the same server = playing together

**Graph Schema:**
```
(Player)-[:PLAYED_WITH {sessionCount, lastPlayed, ...}]->(Player)
(Player)-[:PLAYS_ON {sessionCount, lastPlayed}]->(Server)
```

**Sync Process:**
1. Query PlayerObservations from SQLite
2. Group by (ServerGuid, Timestamp)
3. Create player pairs
4. Aggregate metrics
5. Batch MERGE to Neo4j

## Performance Tips

- ✅ Always create indexes first (`scripts/init-neo4j.cypher`)
- ✅ Use LIMIT in queries to prevent unbounded results
- ✅ Initial sync: 90 days, then incremental (daily)
- ✅ Batch size: 1000 relationships per transaction
- ✅ Memory: Allocate 50% of RAM to Neo4j (see docker-compose)

## Next Steps

1. Run sync for last 90 days (initial graph build)
2. Schedule background job (every 15 min, sync 1 day)
3. Implement query API endpoints
4. Build UI components for relationship visualization
5. Add caching layer for expensive queries

**Ready to query relationships!** 🎉
