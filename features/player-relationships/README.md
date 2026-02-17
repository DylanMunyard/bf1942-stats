# Player Relationships - Graph Database Integration

## Overview

Track player relationships and co-play patterns using Neo4j graph database. We sync data from PlayerObservations (source of truth in Postgres/SQLite) to Neo4j for efficient relationship queries.

## Goals

- Answer questions like:
  - Who plays with Player X the most?
  - Who plays on the same servers as Player X but never together?
  - Find player communities/clans based on co-play patterns
  - Detect new friendships forming over time
  - Find "bridge" players who connect different groups

## Architecture

### Data Flow

```
SQLite (Source of Truth)
  PlayerObservations (timestamp-based)
    ↓
  ETL Service (periodic sync)
    ↓
Neo4j (Query Optimized)
  Player nodes + PLAYED_WITH relationships
```

### Graph Schema

```cypher
// Nodes
(:Player {
  name: string,
  firstSeen: datetime,
  lastSeen: datetime,
  totalSessions: int
})

(:Server {
  guid: string,
  name: string,
  game: string
})

(:TimeWindow {
  year: int,
  month: int,
  week: int
})

// Relationships
(:Player)-[:PLAYED_WITH {
  sessionCount: int,           // How many sessions together
  totalMinutes: int,           // Total time played together
  lastPlayedTogether: datetime,
  firstPlayedTogether: datetime,
  servers: [string],          // List of server GUIDs where they played together
  avgScoreDiff: float         // Average score difference (skill gap indicator)
}]->(:Player)

(:Player)-[:PLAYS_ON {
  sessionCount: int,
  totalMinutes: int,
  lastPlayed: datetime
}]->(:Server)

(:Player)-[:ACTIVE_IN {
  sessionCount: int,
  totalMinutes: int
}]->(:TimeWindow)
```

## Performance Considerations

### Indexes (Critical!)

```cypher
// Player name lookup
CREATE INDEX player_name IF NOT EXISTS FOR (p:Player) ON (p.name);

// Time-based queries
CREATE INDEX player_last_seen IF NOT EXISTS FOR (p:Player) ON (p.lastSeen);

// Relationship metrics
CREATE INDEX played_with_count IF NOT EXISTS FOR ()-[r:PLAYED_WITH]-() ON (r.sessionCount);
CREATE INDEX played_with_last IF NOT EXISTS FOR ()-[r:PLAYED_WITH]-() ON (r.lastPlayedTogether);
```

### Query Optimization

1. **Pre-aggregate common queries** - don't traverse every observation
2. **Use LIMIT** - always limit results
3. **Index relationship properties** - sessionCount, lastPlayedTogether
4. **Batch writes** - use UNWIND for bulk inserts (1000+ at a time)

### ETL Strategy

**Incremental Updates:**
- Track last processed observation timestamp
- Only sync new/updated data
- Run every 5-15 minutes for near-real-time updates

**Full Rebuild:**
- Nightly job to ensure consistency
- Use LOAD CSV or batch import for speed
- Can rebuild entire graph in minutes with proper batching

## Sample Queries

### Most Frequent Co-players

```cypher
MATCH (p1:Player {name: 'PlayerX'})-[r:PLAYED_WITH]->(p2:Player)
RETURN p2.name AS player, 
       r.sessionCount AS sessions,
       r.totalMinutes AS minutes,
       r.lastPlayedTogether AS lastSeen
ORDER BY r.sessionCount DESC
LIMIT 20
```

### Same Server, Never Met

```cypher
MATCH (p1:Player {name: 'PlayerX'})-[:PLAYS_ON]->(s:Server)<-[:PLAYS_ON]-(p2:Player)
WHERE NOT (p1)-[:PLAYED_WITH]-(p2)
  AND p2.lastSeen > datetime() - duration('P30D')  // Active in last 30 days
RETURN p2.name AS player,
       s.name AS server,
       p2.lastSeen AS lastActive
ORDER BY p2.lastSeen DESC
LIMIT 20
```

### Find Communities (using APOC)

```cypher
CALL apoc.algo.community(
  'Player',
  'PLAYED_WITH',
  'sessionCount',
  10000  // iterations
)
YIELD community, nodes
RETURN community, size(nodes) AS size, nodes
ORDER BY size DESC
LIMIT 10
```

### Recent New Connections

```cypher
MATCH (p1:Player {name: 'PlayerX'})-[r:PLAYED_WITH]->(p2:Player)
WHERE r.firstPlayedTogether > datetime() - duration('P7D')
RETURN p2.name AS newConnection,
       r.firstPlayedTogether AS since,
       r.sessionCount AS sessions
ORDER BY r.firstPlayedTogether DESC
```

## Implementation Tasks

- [x] Add Neo4j to docker-compose
- [ ] Create ETL service to sync PlayerObservations → Neo4j
- [ ] Add indexes and constraints to Neo4j schema
- [ ] Create API endpoints for relationship queries
- [ ] Add caching layer (Redis) for expensive graph queries
- [ ] Background job for incremental sync
- [ ] Monitoring/metrics for sync health

## Neo4j Configuration

### Memory Tuning (adjust for your system)

**For 8GB RAM system:**
- Heap: 2GB (NEO4J_server_memory_heap_max__size=2G)
- Page cache: 1GB (NEO4J_server_memory_pagecache_size=1G)

**For 16GB RAM system:**
- Heap: 4GB
- Page cache: 4GB

**For 32GB RAM system:**
- Heap: 8GB
- Page cache: 12GB

### Access

- Web UI: http://localhost:7474
- Bolt: bolt://localhost:7687
- Credentials: neo4j / bf1942stats

## Expected Performance

With proper indexing and batching:
- Simple relationship lookup: <10ms
- Complex traversals (2-3 hops): <100ms
- Community detection: <1s for 10k players
- Full rebuild: <5 minutes for 1M observations

If queries are slow:
1. Check indexes are created
2. Use EXPLAIN/PROFILE to analyze query plan
3. Verify batch size (1000+ records per transaction)
4. Check memory allocation

## Alternative: Postgres + Apache AGE

If Neo4j is still slow, we can try Apache AGE (graph extension for Postgres):
- No new infrastructure
- Uses existing Postgres instance
- Cypher-compatible queries
- May be faster for your data size if it fits in shared_buffers
