# ClickHouse Integration

This integration stores player metrics data into ClickHouse for high-volume analytics.

## Database Schema

```sql
CREATE TABLE player_metrics (
    timestamp DateTime,
    server_guid String,
    server_name String,
    player_name String,
    score UInt32,
    kills UInt16,
    deaths UInt16,
    ping UInt16,
    team UInt8,
    map_name String,
    game_type String
) ENGINE = MergeTree()
ORDER BY (server_guid, timestamp)
PARTITION BY toYYYYMM(timestamp);
```

## Performance Optimization: Pre-aggregated Round Data

### Suggested New Table: `player_rounds`

Instead of calculating rounds on-the-fly from `player_metrics` snapshots, create a dedicated table that stores completed rounds:

```sql
CREATE TABLE player_rounds (
    player_name String,
    server_guid String,
    map_name String,
    round_start_time DateTime,
    round_end_time DateTime,
    final_score Int32,
    final_kills UInt32,
    final_deaths UInt32,
    play_time_minutes Float64,
    round_id String,  -- UUID or hash of (player, server, map, start_time)
    team_label String,
    game_id String,
    created_at DateTime DEFAULT now()
) ENGINE = MergeTree()
ORDER BY (player_name, server_guid, round_start_time)
PARTITION BY toYYYYMM(round_start_time)
SETTINGS index_granularity = 8192;

-- Indexes for fast similarity searches
ALTER TABLE player_rounds ADD INDEX idx_player_time (player_name, round_start_time) TYPE minmax GRANULARITY 1;
ALTER TABLE player_rounds ADD INDEX idx_time_player (round_start_time, player_name) TYPE bloom_filter GRANULARITY 1;
```

### Benefits:
1. **10-100x faster queries** - No need for complex window functions and round detection
2. **Simpler queries** - Direct aggregation: `SUM(final_kills)`, `SUM(final_deaths)`
3. **Better caching** - Smaller result sets, more cacheable
4. **Easier analytics** - Round-based analysis becomes trivial

### Implementation Strategy:
1. ✅ Create the new table
2. ✅ Write a background service to populate it from existing SQLite `PlayerSessions` data  
3. ✅ Update real-time sync to populate from completed SQLite sessions
4. ✅ Create fast query endpoints using the new table
5. ⏳ Gradually migrate existing queries to use the new table

### ✅ IMPLEMENTED!

The `player_rounds` table has been implemented and is automatically synced from SQLite `PlayerSessions` data every 60 seconds. The sync process:

1. **Table Creation**: Automatically created with indexes on startup
2. **Data Sync**: Runs every 4th collection cycle (60s) via `StatsCollectionBackgroundService`
3. **Data Source**: Syncs from completed SQLite `PlayerSessions` (when `IsActive = false`)
4. **Incremental**: Only syncs sessions newer than the last synced timestamp
5. **Paged Processing**: Handles millions of records with configurable page sizes (default: 1000 records/page)
6. **API Access**: Available via `/stats/players/fast-stats` endpoint
7. **Manual Sync**: Available via `/stats/players/sync-rounds` POST endpoint for bulk operations

### Performance Comparison:

**Before (Complex query on player_metrics):**
```sql
-- This type of query would be VERY slow on player_metrics
SELECT 
    player_name,
    SUM(kills) as total_kills,
    SUM(deaths) as total_deaths,
    -- Complex round detection logic here...
FROM player_metrics 
WHERE timestamp >= now() - INTERVAL 6 MONTH
GROUP BY player_name;
```

**After (Simple query on player_rounds):**
```sql
-- This query is 10-100x faster!
SELECT 
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    SUM(play_time_minutes) as total_play_time_minutes
FROM player_rounds 
WHERE round_start_time >= now() - INTERVAL 6 MONTH
GROUP BY player_name;
```

### Example API Usage:

**Query Fast Stats:**
```bash
# Get fast aggregated stats for all players (last 6 months)
curl "http://localhost:5000/stats/players/fast-stats"

# Get stats for specific player
curl "http://localhost:5000/stats/players/fast-stats?playerName=SomePlayer"

# Get stats for date range
curl "http://localhost:5000/stats/players/fast-stats?fromDate=2024-01-01&toDate=2024-12-31"
```

**Manual Bulk Sync (for initial data load):**
```bash
# Sync all historical data (excludes recent data to avoid background service conflicts)
curl -X POST "http://localhost:5000/stats/players/sync-rounds"

# Sync from specific date with custom pagination (safe mode - excludes last 2 hours)
curl -X POST "http://localhost:5000/stats/players/sync-rounds?fromDate=2023-01-01&pageSize=2000&maxPages=500"

# Sync including ALL data (not recommended while background service is running)
curl -X POST "http://localhost:5000/stats/players/sync-rounds?fromDate=2024-01-01&excludeRecentData=false"

# Sync last year's data only (safe mode)
curl -X POST "http://localhost:5000/stats/players/sync-rounds?fromDate=2024-01-01"
```

**Response format for manual sync:**
```json
{
  "totalProcessed": 50000,
  "totalPages": 50,
  "totalDuration": 125000.5,
  "completedSuccessfully": true,
  "results": [
    {
      "page": 0,
      "processedCount": 1000,
      "hasMorePages": true,
      "duration": 2500.1,
      "success": true,
      "errorMessage": null
    }
  ]
}
```

### Direct ClickHouse Queries:

```sql
-- Top players by play time (last 6 months)
SELECT 
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths, 
    SUM(play_time_minutes) as total_play_time_minutes,
    round(SUM(final_kills) / SUM(final_deaths), 3) as kd_ratio
FROM player_rounds 
WHERE round_start_time >= now() - INTERVAL 6 MONTH
GROUP BY player_name
HAVING total_kills > 10 
ORDER BY total_play_time_minutes DESC;

-- Server popularity by unique players per day
SELECT 
    server_guid,
    toDate(round_start_time) as date,
    uniq(player_name) as unique_players,
    COUNT(*) as total_rounds
FROM player_rounds 
WHERE round_start_time >= now() - INTERVAL 30 DAY
GROUP BY server_guid, date
ORDER BY date DESC, unique_players DESC;

-- Map performance analysis
SELECT 
    map_name,
    AVG(final_kills) as avg_kills_per_round,
    AVG(play_time_minutes) as avg_round_duration,
    COUNT(*) as total_rounds
FROM player_rounds 
WHERE round_start_time >= now() - INTERVAL 7 DAY
GROUP BY map_name
ORDER BY total_rounds DESC;
```

## Sync Implementation

### Overview
The sync process uses two different strategies depending on the context:

**1. Incremental Sync (Background Service):**
- **Automatic**: Runs every 60 seconds via background service
- **Timestamp-based**: Only syncs new data since last successful sync
- **Batch Size**: Processes up to 5000 records per cycle to avoid memory issues
- **No Paging**: Uses timestamp-based filtering instead of explicit pagination

**2. Manual Bulk Sync (API Endpoint):**
- **Explicit Paging**: Default 1000 records per page (configurable)
- **Historical Data**: Can process entire database from any date range
- **Memory Management**: Processes data in chunks to avoid memory issues
- **Manual Control**: Can process up to 100 pages per request (configurable)
- **Ordering**: Uses `SessionId` for consistent pagination across requests

### Key Components

1. **SyncResult Class**: Returns detailed information about each page processed
2. **Paged Queries**: Uses `Skip()` and `Take()` with consistent ordering
3. **Progress Tracking**: Tracks processed count, duration, and success status
4. **Error Handling**: Graceful failure handling with detailed error messages

### Usage Recommendations

- **Initial Bulk Load**: Use manual sync endpoint (`POST /stats/players/sync-rounds`) with appropriate `maxPages` setting
- **Regular Operations**: Background service handles incremental sync automatically (no intervention needed)
- **Large Historical Sync**: Use manual endpoint with increased `pageSize` for better throughput
- **Monitoring**: Check logs for sync progress and any errors
- **No Duplicate Processing**: Incremental sync prevents reprocessing of already synced data
- **Conflict Avoidance**: Manual sync excludes recent data (last 2 hours) by default to avoid conflicts with background service

## Avoiding Sync Conflicts

When running manual bulk sync while the background service is active, there's a potential for duplicate processing. To prevent this:

### Default Safe Mode (Recommended)
```bash
# This excludes data newer than 2 hours, letting background service handle recent data
curl -X POST "http://localhost:5000/stats/players/sync-rounds"
```

### How It Works:
- **Background Service**: Handles incremental sync of recent data (based on last sync timestamp)
- **Manual Sync**: Processes historical data but excludes last 2 hours by default
- **No Overlap**: Each service handles its own time range, preventing duplicates

### Force All Data (Use with Caution)
```bash
# Only use this if background service is stopped or you want to risk duplicates
curl -X POST "http://localhost:5000/stats/players/sync-rounds?excludeRecentData=false"
```

### Recommended Approach for Bulk Historical Sync:
1. Let background service handle ongoing incremental sync
2. Use manual sync with `excludeRecentData=true` (default) for historical data
3. The 2-hour buffer ensures no conflicts between the two processes

## Configuration

Set the `CLICKHOUSE_URL` environment variable to override the default ClickHouse endpoint:

```bash
export CLICKHOUSE_URL="http://your-clickhouse-server:8123"
```

Default: `http://clickhouse.home.net`

## Collection Behavior

- **Collection Interval**: 30 seconds (changed from 60 seconds)
- **ClickHouse Storage**: Every cycle (30s)
- **SQLite Storage**: Every 2nd cycle (60s) - for backward compatibility

## Data Flow

1. **Every 30 seconds**: Collect server data from BFList API
2. **Always**: Store player metrics to ClickHouse
3. **Every 60 seconds**: Store session tracking data to SQLite

This approach allows for high-frequency data collection while maintaining the existing session tracking functionality. 