# ClickHouse Performance Analysis

## Executive Summary

**Problem**: Queries using `player_achievements_deduplicated` view are slow (300-400ms) and reading excessive data (124MB, 897K rows).

**Root Cause**: The view uses `ROW_NUMBER()` window function which scans the entire table on every query, preventing index usage and partition pruning.

**Impact**: Affects both main endpoints:
- Player Stats endpoint (`/stats/players/{playerName}`)
- Server Stats endpoint (`/stats/servers/{serverName}`)

## Performance Metrics

### Current Performance (Baseline)

| Query | Duration | Rows Read | Bytes Read | Memory Used |
|-------|----------|-----------|------------|-------------|
| Placement Leaderboard (All Time) | 315-414ms | 897,457 | 123.95 MiB | 137.73 MiB |
| Placement Leaderboard (Week) | 315-399ms | 897,457 | 123.95 MiB | 137.73 MiB |
| Player Kill Streaks (View) | 315-376ms | 897,457 | 123.95 MiB | 137.73 MiB |
| Player Kill Streaks (Direct+FINAL) | 121ms | ~smaller | ~smaller | ~smaller |
| Most Active Players (player_rounds) | 68ms | ~much less | ~much less | ~much less |

### Key Findings

1. **View Queries Scan Entire Table**: All queries against `player_achievements_deduplicated` read all 897K rows
2. **No Index Usage**: The ROW_NUMBER() window function prevents index usage
3. **No Partition Pruning**: Even with date filters, all partitions are scanned
4. **3-5x Slower**: View queries are 3-5x slower than direct table queries

## Current Architecture

### player_achievements Table
- **Engine**: `ReplacingMergeTree(version)`
- **Partitioning**: `PARTITION BY toYYYYMM(achieved_at)`
- **Ordering**: `ORDER BY (player_name, achievement_type, achievement_id, round_id, achieved_at)`
- **Indexes**: Bloom filter on `(player_name, achievement_type)`
- **Size**: 30.52 MiB, 897,457 rows, 14 parts

### player_achievements_deduplicated View
```sql
CREATE VIEW player_achievements_deduplicated AS
SELECT
    player_name, achievement_type, achievement_id, achievement_name,
    tier, value, achieved_at, processed_at, server_guid,
    map_name, round_id, metadata, version, game
FROM (
    SELECT *,
           ROW_NUMBER() OVER (
               PARTITION BY player_name, achievement_type, achievement_id, round_id, achieved_at
               ORDER BY version DESC
           ) as rn
    FROM player_achievements
)
WHERE rn = 1;
```

**Problem**: Window function `ROW_NUMBER()` requires full table scan and prevents optimization.

## Optimization Strategy

### Option 1: Use FINAL Instead of VIEW (Quick Win)
**Pros**:
- ReplacingMergeTree already supports FINAL for deduplication
- No schema changes needed
- Immediate performance improvement

**Cons**:
- FINAL still requires more work than a materialized view
- Not as fast as pre-computed deduplication

**Implementation**:
```sql
-- Instead of: FROM player_achievements_deduplicated
-- Use: FROM player_achievements FINAL
```

### Option 2: Materialized View (Recommended)
**Pros**:
- Pre-computed deduplication (fastest)
- Supports indexes
- Enables partition pruning
- Incremental updates

**Cons**:
- Requires schema migration
- Uses additional disk space
- Existing data needs backfill

**Implementation**: See `clickhouse-optimization.sql`

### Option 3: Pre-Aggregated Tables
For frequently accessed queries (placement leaderboards), create summary tables:

**Pros**:
- Extremely fast queries
- Minimal data to scan

**Cons**:
- Additional maintenance
- Eventual consistency

## Recommended Implementation Plan

### Phase 1: Immediate Fixes (Day 1)
1. **Add Missing Indexes**:
   ```sql
   ALTER TABLE player_achievements
   ADD INDEX idx_server_achievement (server_guid, achievement_type) TYPE bloom_filter GRANULARITY 1;
   ```

2. **Use FINAL Instead of VIEW** (code changes):
   - Update all queries to use `player_achievements FINAL`
   - Test performance improvement (expect 30-50% faster)

### Phase 2: Materialized View Migration (Week 1)
1. Create materialized view with proper indexes
2. Backfill existing data
3. Update application to use materialized view
4. Monitor performance

### Phase 3: Pre-Aggregation (Optional - Week 2)
1. Create placement leaderboard summary tables
2. Update incrementally via triggers/materialized views
3. Modify queries to use summary tables

## Expected Performance Improvements

| Phase | Current | After Phase 1 | After Phase 2 | After Phase 3 |
|-------|---------|---------------|---------------|---------------|
| Placement Leaderboard | 400ms | 200-250ms | 50-100ms | <10ms |
| Player Achievements | 376ms | 120-150ms | 30-50ms | N/A |
| Memory Usage | 138MB | 70-90MB | 20-40MB | <5MB |
| Rows Scanned | 897K | 897K | 10-100K | <1K |

## Files Created

1. `profile-clickhouse-queries.sh` - Comprehensive profiling script
2. `profile-queries-v2.sh` - Quick performance check
3. `clickhouse-optimization.sql` - Migration script (to be created)
4. `PERFORMANCE_ANALYSIS.md` - This document

## Next Steps

1. Review and approve optimization strategy
2. Create clickhouse-optimization.sql with migration queries
3. Test in staging/development environment
4. Deploy Phase 1 (quick wins)
5. Plan Phase 2 deployment window
