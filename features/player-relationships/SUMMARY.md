# Player Relationships Feature - Summary

## What We Built

A **Neo4j graph database integration** for tracking player relationships and co-play patterns in BF1942.

## Files Created

### Documentation
- `features/player-relationships/README.md` - Architecture and design
- `features/player-relationships/GETTING-STARTED.md` - Setup instructions
- `features/player-relationships/SUMMARY.md` - This file

### Infrastructure
- `docker-compose.dev.yml` - Updated with Neo4j service (tuned for performance)
- `scripts/init-neo4j.cypher` - Schema initialization (indexes, constraints)
- `scripts/neo4j-queries-reference.cypher` - Common query patterns

### Code (Stubs for Implementation)
- `api/PlayerRelationships/Models/CoPlaySession.cs` - Data models
- `api/PlayerRelationships/IPlayerRelationshipService.cs` - Service interface
- `api/PlayerRelationships/PlayerRelationshipEtlService.cs` - ETL logic (detection implemented, Neo4j sync TODO)

## How It Works

### Data Flow

```
PlayerObservations (SQLite/Postgres)
  ↓
ETL Service (detects co-play by timestamp)
  ↓
Neo4j Graph Database
  ↓
Relationship Queries (API)
```

### Core Concept

Players with **observations at the same timestamp** on the same server = playing together.

The ETL service:
1. Groups observations by `(ServerGuid, Timestamp)`
2. Creates pairs of all players in each group
3. Aggregates pairs into relationships with metrics
4. Syncs to Neo4j as `PLAYED_WITH` edges

### Graph Schema

```
(:Player)-[:PLAYED_WITH {sessionCount, totalMinutes, ...}]->(:Player)
(:Player)-[:PLAYS_ON {sessionCount, totalMinutes}]->(:Server)
```

## What You Can Query

✅ **Who plays with PlayerX the most?**
✅ **Who plays on same servers but never together?** (potential connections)
✅ **Find player communities/clans**
✅ **Recent new friendships**
✅ **Players who stopped playing together**
✅ **Bridge players** (connecting different groups)

See `scripts/neo4j-queries-reference.cypher` for examples.

## Performance Tuning

### Memory Settings (Critical!)

Configured in `docker-compose.dev.yml`:
- **Heap**: 2GB (default for 8GB system)
- **Page cache**: 1GB
- Adjust based on your RAM (see GETTING-STARTED.md)

### Indexes (Critical!)

Run `scripts/init-neo4j.cypher` to create:
- Player name index (unique constraint)
- Relationship property indexes (sessionCount, lastPlayedTogether)
- Server indexes

**Without these, Neo4j will be slow!**

### Query Optimization

- Always use `LIMIT`
- Use `PROFILE` to analyze query plans
- Batch writes (1000+ at a time)
- Pre-aggregate common queries

## Next Steps (TODOs)

### ✅ Completed (v0.3 - Automated Migrations)
1. ✅ **Install Neo4j driver NuGet package** - Added Neo4j.Driver 6.0.0
2. ✅ **Implement `SyncToNeo4jAsync` in PlayerRelationshipEtlService** - Batch MERGE with proper updates
3. ✅ **Create Neo4j service wrapper** - Neo4jService with connection pooling
4. ✅ **Add admin endpoint** - POST /stats/admin/data/neo4j/sync
5. ✅ **Configuration system** - appsettings.json with optional enable/disable
6. ✅ **Player-server relationships** - PLAYS_ON edges synced alongside PLAYED_WITH
7. ✅ **Automated migration system** - No manual schema setup!
8. ✅ **Migration tracking** - Applied migrations stored in Neo4j
9. ✅ **Migration status endpoint** - GET /stats/admin/data/neo4j/migrations
10. ✅ **Embedded migration files** - Compiled into assembly

### Immediate (Ready to Test!)
1. **Start Neo4j**
   ```bash
   ./scripts/start-neo4j.sh
   ```

2. **Enable Neo4j in API** - Update appsettings.Development.json
   ```json
   {"Neo4j": {"Uri": "bolt://localhost:7687", "Username": "neo4j", "Password": "bf1942stats"}}
   ```

3. **Start API** - Schema created automatically!
   ```bash
   cd api && dotnet run
   ```

4. **Run your first sync** - See API-USAGE.md for examples

### Short Term
4. **Create background job** to run ETL every 15 minutes
5. **Implement `PlayerRelationshipService`** with Neo4j queries
6. **Add API endpoints** for relationship queries
7. **Add caching** (Redis) for expensive traversals

### Medium Term
8. **UI components** to visualize player networks
9. **Notifications** for new connections
10. **Community detection** using graph algorithms (APOC/GDS)

## Starting Neo4j

```bash
# Start
docker-compose -f docker-compose.dev.yml up -d neo4j

# Access browser
open http://localhost:7474
# Login: neo4j / bf1942stats

# Initialize schema
# Copy/paste contents of scripts/init-neo4j.cypher

# Test with sample data
MATCH (n) RETURN n LIMIT 25;
```

## Why Neo4j (vs Alternatives)

### ✅ Pros
- Purpose-built for relationship queries
- Fast traversals (if tuned properly)
- Cypher query language is intuitive
- Rich ecosystem (APOC, Graph Data Science)
- Scales well for complex relationships

### ⚠️ Cons
- **Needs proper tuning** (memory, indexes)
- Extra infrastructure to maintain
- Learning curve for Cypher

### Alternatives Considered

1. **Apache AGE** (Postgres graph extension)
   - No new infrastructure
   - Cypher-compatible
   - May be simpler for your scale

2. **Materialized views in Postgres**
   - Simplest option
   - Limited to simple queries
   - No graph algorithms

3. **Redis Graph (FalkorDB)**
   - Lightweight
   - In-memory = fast
   - Smaller ecosystem

**Recommendation:** Try Neo4j with proper tuning. If still slow, fall back to Apache AGE or materialized views.

## Expected Performance (with tuning)

- Simple lookups: **<10ms**
- 2-3 hop traversals: **<100ms**
- Community detection (10k players): **<1s**
- Full ETL rebuild (1M observations): **<5 min**

## Resetting Expectations

Your previous Neo4j experience was likely slow due to:
1. ❌ No indexes created
2. ❌ Default memory settings (too low)
3. ❌ Row-by-row inserts instead of batching
4. ❌ Unbounded queries (no LIMIT)

This setup addresses all of these:
- ✅ Creates indexes automatically
- ✅ Tuned memory for your system
- ✅ Batch ETL design
- ✅ All example queries use LIMIT

## Questions?

See:
- `features/player-relationships/README.md` for architecture details
- `features/player-relationships/GETTING-STARTED.md` for setup steps
- `scripts/neo4j-queries-reference.cypher` for query examples
