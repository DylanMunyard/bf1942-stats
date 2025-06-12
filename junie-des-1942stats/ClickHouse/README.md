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