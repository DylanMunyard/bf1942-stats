# Player Relationships Feature - Changelog

## [0.4.0] - 2026-02-17 - Admin UI Integration

### Added
- ✅ **Admin Cron Tab Integration**
  - Neo4j sync job in Admin Portal
  - Configurable sync window (1, 7, 30, 90 days)
  - Migration status badge
  - Auto-detection of Neo4j availability
  - Success/error messaging
  - Automatic token refresh
  
- ✅ **Frontend Services**
  - `triggerNeo4jSync()` - Trigger sync from UI
  - `getNeo4jMigrationStatus()` - Check schema status
  - Proper error handling and authentication
  
- ✅ **Visual Polish**
  - Green border for Neo4j item
  - Disabled state when Neo4j unavailable
  - Success metrics display (count, duration)
  - Migration status indicator

### Changed
- Admin portal now shows Neo4j sync alongside other background jobs
- UI automatically detects Neo4j availability on load

### Documentation
- `UI-INTEGRATION.md` - Complete UI integration guide
- Screenshots and usage examples

---

## [0.3.0] - 2026-02-17 - Automated Migration System

### Added
- ✅ **Neo4j Migration System**
  - Automatic schema migrations on app startup
  - Migration tracking in `:MigrationTracker` node
  - Embedded Cypher migration files
  - Idempotent execution (safe to re-run)
  - Migration status API endpoint
  
- ✅ **Migration Files**
  - `001_InitialSchema.cypher` - Indexes and constraints
  - Embedded as resources in assembly
  - Parsed and executed automatically
  
- ✅ **Admin Endpoints**
  - `GET /stats/admin/data/neo4j/migrations` - Check migration status
  - `POST /stats/admin/data/neo4j/migrations/run` - Manually run migrations
  
- ✅ **Documentation**
  - `MIGRATIONS.md` - Complete migration guide
  - Updated setup guides (no manual steps!)

### Changed
- ❌ **BREAKING**: No longer need to run `scripts/init-neo4j.cypher` manually
- ✅ Schema initialization is now automatic
- ✅ Migrations run on every app startup (pending only)

### Fixed
- ✅ Eliminated manual deployment step for schema setup
- ✅ Proper error handling when Neo4j unavailable
- ✅ Graceful degradation if migrations fail

---

## [0.2.0] - 2026-02-17 - Neo4j Integration Complete

### Added
- ✅ **Neo4j Driver Integration**
  - Added `Neo4j.Driver` 6.0.0 NuGet package
  - Created `Neo4jService` for connection management
  - Implemented connection pooling and async query execution

- ✅ **ETL Service Implementation**
  - `PlayerRelationshipEtlService.DetectCoPlaySessionsAsync()` - Finds players online together
  - `PlayerRelationshipEtlService.SyncToNeo4jAsync()` - Batch MERGE to Neo4j
  - `PlayerRelationshipEtlService.SyncPlayerServerRelationshipsAsync()` - Player-server edges
  - `PlayerRelationshipEtlService.SyncRelationshipsAsync()` - Full sync orchestration

- ✅ **Admin API Endpoint**
  - `POST /stats/admin/data/neo4j/sync` - Trigger sync for last N days
  - Request validation (1-365 days)
  - Graceful handling when Neo4j disabled
  - Response includes metrics (relationshipsProcessed, durationMs)

- ✅ **Configuration System**
  - `appsettings.json` - Neo4j connection settings
  - `appsettings.Development.json` - Disabled by default (opt-in)
  - Environment variable support: `Neo4j__Uri`, `Neo4j__Username`, etc.
  - Optional service registration (only when configured)

- ✅ **Docker Compose Setup**
  - Neo4j 5.15 Community Edition
  - Performance tuning (2GB heap, 1GB page cache)
  - APOC plugins enabled
  - Health checks and proper networking

- ✅ **Schema & Indexes**
  - `scripts/init-neo4j.cypher` - Complete schema initialization
  - Indexes on player names, relationship properties
  - Unique constraints on Player.name, Server.guid
  - Sample test data for validation

- ✅ **Documentation**
  - `README.md` - Architecture and design
  - `GETTING-STARTED.md` - Setup instructions
  - `TESTING.md` - Test scenarios and verification
  - `API-USAGE.md` - Endpoint documentation and examples
  - `QUICK-START.md` - 5-minute setup guide
  - `SUMMARY.md` - Feature overview
  - `scripts/neo4j-queries-reference.cypher` - Query examples

- ✅ **Scripts & Tools**
  - `scripts/start-neo4j.sh` - One-command Neo4j startup
  - Query reference with 10+ common patterns
  - Test examples in ETL service

### Technical Details

**Graph Schema:**
```
(:Player {name, firstSeen, lastSeen, totalSessions})
  -[:PLAYED_WITH {sessionCount, firstPlayedTogether, lastPlayedTogether, servers, avgScoreDiff}]->
  (:Player)

(:Player)
  -[:PLAYS_ON {sessionCount, lastPlayed}]->
  (:Server {guid, name, game})
```

**Core Algorithm:**
1. Query `PlayerObservations` grouped by `(ServerGuid, Timestamp)`
2. Create cartesian product of players in each group (co-play pairs)
3. Aggregate pairs into relationships with metrics
4. Batch MERGE to Neo4j (1000 records per transaction)
5. Incremental updates (ON MATCH SET sessionCount += ...)

**Performance Optimizations:**
- Batch processing (1000 relationships/tx)
- Indexed lookups (Player.name, relationship properties)
- Pre-aggregation (count at source, not in graph)
- Memory tuning (configurable heap/page cache)
- Lazy loading (only load when enabled)

### Configuration

**Enable Neo4j:**
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

**Disable Neo4j:**
```json
{
  "Neo4j": {
    "Uri": ""
  }
}
```

### API Usage

```bash
curl -X POST http://localhost:5555/stats/admin/data/neo4j/sync \
  -H "Authorization: Bearer $ADMIN_TOKEN" \
  -H "Content-Type: application/json" \
  -d '{"days": 7}'
```

### Known Limitations

- No automatic background job yet (manual trigger only)
- No query API endpoints (direct Neo4j Browser access only)
- No caching layer for expensive queries
- No UI components for visualization
- Sync is blocking (long syncs may timeout)

### Future Enhancements

See [Next Steps](#next-steps) below.

---

## [0.1.0] - 2026-02-17 - Initial Design

### Added
- Feature documentation and design
- Docker Compose configuration
- Query examples
- Data models (DTOs)
- Service interfaces

### Decisions

**Why Neo4j?**
- Purpose-built for relationship queries
- Efficient traversals (vs SQL self-joins)
- Rich query language (Cypher)
- Graph algorithms (community detection, centrality)

**Why Optional Integration?**
- Not all deployments need relationships
- Reduces infrastructure complexity when disabled
- Allows gradual rollout

**Why ETL Instead of Real-Time?**
- Observations already captured in SQLite
- Batch processing is more efficient
- Reduces load on game servers
- Can backfill historical data

---

## Next Steps

### Short Term (Ready to Implement)
1. **Implement PlayerRelationshipService**
   - Query endpoints for relationship data
   - Wrap common Cypher patterns in C# methods
   - Add to DI container

2. **Create Query API Endpoints**
   - `GET /stats/players/{name}/coPlayers` - Most frequent co-players
   - `GET /stats/players/{name}/potentialConnections` - Same server, never met
   - `GET /stats/players/{name}/network` - Relationship graph
   - `GET /stats/players/{name}/relationship/{otherName}` - Relationship details

3. **Add Background Job**
   - Scheduled job: Every 15 minutes, sync last 1 day
   - Use Hangfire or similar
   - Configurable schedule
   - Error handling and retries

4. **Caching Layer**
   - Redis cache for expensive queries
   - Cache player networks (1-2 hop traversals)
   - TTL: 5-15 minutes
   - Cache key: `neo4j:player:{name}:network`

### Medium Term
5. **UI Components**
   - Player relationship graph (D3.js, vis.js, or similar)
   - "Players you might know" widget
   - Clan/community detection display
   - Activity timeline

6. **Monitoring & Metrics**
   - Sync success/failure metrics
   - Query performance tracking
   - Graph size metrics (nodes, relationships)
   - Telemetry integration

7. **Advanced Queries**
   - Community detection (Louvain algorithm)
   - Bridge players (betweenness centrality)
   - Player influence scores
   - Clan membership detection

8. **Optimization**
   - Incremental sync tracking (last_synced timestamp)
   - Parallel batch processing
   - Graph data projection for repeated queries
   - Query result pagination

### Long Term
9. **Social Features**
   - Friend recommendations
   - Clan finder
   - Activity feeds based on relationships
   - Notifications for co-player activity

10. **Analytics**
   - Relationship strength scoring
   - Player retention by social connections
   - Community health metrics
   - Network growth over time

---

## Migration Notes

### From Previous Neo4j Attempts

**What's Different This Time:**
- ✅ Proper indexing from the start
- ✅ Memory configuration tuned for workload
- ✅ Batch processing (not row-by-row)
- ✅ Query limits to prevent unbounded results
- ✅ Optional integration (doesn't break if disabled)

**If You Had Slow Neo4j Before:**
1. Delete old data: `docker volume rm bf1942_neo4j-data`
2. Start fresh with new config
3. Run schema init first
4. Test with small sync (1 day)
5. Scale up gradually

### Breaking Changes

None - this is a new feature, doesn't affect existing APIs.

---

## References

- [Neo4j Cypher Manual](https://neo4j.com/docs/cypher-manual/current/)
- [Neo4j .NET Driver](https://neo4j.com/docs/dotnet-manual/current/)
- [Graph Data Science Library](https://neo4j.com/docs/graph-data-science/current/)
- [APOC Procedures](https://neo4j.com/labs/apoc/)
