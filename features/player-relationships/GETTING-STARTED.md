# Getting Started with Player Relationships

## Quick Start

### 1. Start Neo4j

```bash
# Start all services including Neo4j
docker-compose -f docker-compose.dev.yml up -d neo4j

# Check logs
docker logs bf1942-neo4j -f
```

Wait for Neo4j to be ready (you'll see "Started" in the logs).

### 2. Enable Neo4j in API

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

### 3. Start API (Migrations Run Automatically!)

```bash
cd api
dotnet run
```

**Watch the logs:**
```
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Checking for pending Neo4j migrations...
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Found 0 previously applied migrations
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Found 1 total migration files
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Found 1 pending migrations. Applying...
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Applying migration: 001_InitialSchema.cypher
info: api.PlayerRelationships.Neo4jMigrationService[0]
      Successfully applied migration 001_InitialSchema.cypher in 142ms
info: api.PlayerRelationships.Neo4jMigrationService[0]
      All migrations applied successfully
```

✅ **Schema created automatically!** No manual steps needed.

### 4. Verify Setup

**Via API:**
```bash
curl -X GET http://localhost:5555/stats/admin/data/neo4j/migrations \
  -H "Authorization: Bearer $ADMIN_TOKEN" | jq

# Response:
{
  "appliedCount": 1,
  "pendingCount": 0,
  "isUpToDate": true,
  "appliedMigrations": ["001_InitialSchema.cypher"],
  "pendingMigrations": []
}
```

**Via Neo4j Browser (http://localhost:7474):**
```cypher
// Login: neo4j / bf1942stats

// Check indexes were created
SHOW INDEXES;

// Check migration tracker
MATCH (tracker:MigrationTracker {id: 'singleton'})
RETURN tracker;
```

## Performance Tuning

### Adjust Memory (if needed)

Edit `docker-compose.dev.yml` and adjust these values based on your system:

**For 8GB RAM system (default):**
```yaml
- NEO4J_server_memory_heap_max__size=2G
- NEO4J_server_memory_pagecache_size=1G
```

**For 16GB RAM system:**
```yaml
- NEO4J_server_memory_heap_max__size=4G
- NEO4J_server_memory_pagecache_size=4G
```

**For 32GB RAM system:**
```yaml
- NEO4J_server_memory_heap_max__size=8G
- NEO4J_server_memory_pagecache_size=12G
```

After changing, restart Neo4j:
```bash
docker-compose -f docker-compose.dev.yml restart neo4j
```

### Check Memory Usage

In Neo4j Browser:
```cypher
CALL dbms.queryJmx('java.lang:type=Memory') 
YIELD attributes 
RETURN attributes.HeapMemoryUsage.value.used, 
       attributes.HeapMemoryUsage.value.max;
```

## Running the ETL

### Manual ETL (for testing)

```bash
# TODO: Create CLI tool or API endpoint to trigger ETL
# For now, you can test the detection logic in a C# test project
```

### Example: Detect Co-Play Sessions

The ETL service finds players who were online at the same time:

```csharp
// In your test or background job
var etlService = new PlayerRelationshipEtlService(dbContext, logger);

// Detect co-play for last 24 hours
var pairs = await etlService.DetectCoPlaySessionsAsync(
    DateTime.UtcNow.AddDays(-1),
    DateTime.UtcNow
);

// Aggregate into relationships
var relationships = etlService.AggregateRelationships(pairs);

// Sync to Neo4j (not yet implemented)
await etlService.SyncToNeo4jAsync(relationships);
```

## Common Queries

See `scripts/neo4j-queries-reference.cypher` for a comprehensive list.

### Quick Examples

**Who plays with "PlayerX" the most?**
```cypher
MATCH (p1:Player {name: 'PlayerX'})-[r:PLAYED_WITH]-(p2:Player)
RETURN p2.name, r.sessionCount, r.totalMinutes
ORDER BY r.sessionCount DESC
LIMIT 10;
```

**Who's on the same server but never met?**
```cypher
MATCH (p1:Player {name: 'PlayerX'})-[:PLAYS_ON]->(s:Server)<-[:PLAYS_ON]-(p2:Player)
WHERE NOT (p1)-[:PLAYED_WITH]-(p2)
RETURN p2.name, s.name
LIMIT 10;
```

## Monitoring Performance

### Profile a Query

Add `PROFILE` before any query to see execution details:

```cypher
PROFILE 
MATCH (p1:Player {name: 'PlayerX'})-[r:PLAYED_WITH]-(p2:Player)
RETURN p2.name
ORDER BY r.sessionCount DESC
LIMIT 10;
```

Look for:
- **DB Hits**: Lower is better
- **Rows**: Estimate vs actual
- **Index usage**: Should say "NodeIndexSeek" for indexed fields

### Check Index Usage

```cypher
SHOW INDEXES;
```

Make sure all indexes are "ONLINE".

## Troubleshooting

### Neo4j is slow

1. **Check indexes are created:**
   ```cypher
   SHOW INDEXES;
   ```

2. **Use PROFILE to analyze queries:**
   ```cypher
   PROFILE MATCH (p:Player {name: 'X'})...
   ```
   Look for "NodeIndexSeek" (good) vs "AllNodesScan" (bad)

3. **Check memory allocation:**
   ```cypher
   CALL dbms.queryJmx('java.lang:type=Memory') YIELD attributes 
   RETURN attributes.HeapMemoryUsage.value;
   ```

4. **Increase heap size** in docker-compose.dev.yml

### Connection refused

```bash
# Check if Neo4j is running
docker ps | grep neo4j

# Check logs
docker logs bf1942-neo4j

# Restart if needed
docker-compose -f docker-compose.dev.yml restart neo4j
```

### Out of memory

Reduce heap size or increase Docker memory limit:

```bash
# Check Docker memory limit
docker stats bf1942-neo4j

# Adjust in docker-compose.dev.yml
# Or increase Docker Desktop memory limit in Settings
```

## Next Steps

1. **Implement Neo4j driver integration** in `PlayerRelationshipEtlService`
2. **Create background job** to run ETL every 15 minutes
3. **Build API endpoints** for relationship queries
4. **Add caching layer** (Redis) for expensive queries
5. **Create UI components** to show player relationships

## Alternative: Stay in Postgres

If Neo4j is still too slow or complex, consider:

1. **Materialized view** in Postgres:
   ```sql
   CREATE MATERIALIZED VIEW player_relationships AS
   SELECT 
     ps1.player_name AS player1,
     ps2.player_name AS player2,
     COUNT(*) AS session_count,
     MIN(po1.timestamp) AS first_played,
     MAX(po1.timestamp) AS last_played
   FROM player_observations po1
   JOIN player_observations po2 
     ON po1.timestamp = po2.timestamp 
     AND po1.session_id < po2.session_id
   JOIN player_sessions ps1 ON po1.session_id = ps1.session_id
   JOIN player_sessions ps2 ON po2.session_id = ps2.session_id
   GROUP BY ps1.player_name, ps2.player_name;
   ```

2. **Apache AGE** (graph extension for Postgres)
   - Same Cypher queries
   - No new infrastructure
   - May be faster for smaller datasets

Let me know which direction you want to go!
