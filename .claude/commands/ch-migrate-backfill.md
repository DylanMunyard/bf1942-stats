---
description: "ClickHouse Migration: Stream B - Create backfill services to populate SQLite from ClickHouse"
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, mcp__serena__find_symbol, mcp__serena__get_symbols_overview, mcp__serena__list_dir, mcp__serena__search_for_pattern, mcp__serena__replace_symbol_body, mcp__serena__insert_after_symbol, mcp__serena__insert_before_symbol
---

# ClickHouse Migration - Stream B: Backfill Services

You are implementing **Stream B** of the ClickHouse to SQLite migration.

## Prerequisites

**Stream A must be complete first.** Verify by checking:
- Entities exist in `api/Data/Entities/` (PlayerStatsLifetime.cs, ServerOnlineCount.cs, etc.)
- Migration exists in `api/Migrations/`

If Stream A is not complete, STOP and inform the user.

## Reference Documents

Read these files first:
- @bf1942-stats/features/clickhouse-migration/migration-plan.md (see "Stream B: Backfill Services" and "Task 1.2.x" sections)
- @bf1942-stats/features/clickhouse-migration/analysis.md (see ClickHouse queries for each table)

## Your Scope

**Directory ownership:** `api/ClickHouse/BackfillServices/` (create this directory)

**DO NOT modify files outside your scope.** Other agents handle other directories.

## Tasks

### 1. Create Backfill Interface

Create `api/ClickHouse/BackfillServices/IBackfillService.cs`:
```csharp
public interface IBackfillService
{
    string Name { get; }
    Task<BackfillResult> ExecuteAsync(CancellationToken ct);
    Task<BackfillProgress> GetProgressAsync();
}

public record BackfillResult(bool Success, int RowsProcessed, string? Error);
public record BackfillProgress(int TotalRows, int ProcessedRows, bool IsComplete);
```

### 2. Create Backfill Services (Tasks 1.2.1-1.2.6)

Each service should:
- Query ClickHouse for source data (see analysis.md for exact queries)
- Batch insert into SQLite (1000 rows per batch)
- Track progress for resume capability
- Emit telemetry using ActivitySources.SqliteAnalytics

**Services to create:**

| File | Source Table | Target Entity |
|------|--------------|---------------|
| `ServerOnlineCountsBackfillService.cs` | server_online_counts | ServerOnlineCount |
| `PlayerStatsBackfillService.cs` | player_rounds | PlayerStatsLifetime, PlayerServerStats, PlayerMapStats |
| `PlayerMilestonesBackfillService.cs` | player_rounds (computed) | PlayerMilestone |
| `PlayerBestScoresBackfillService.cs` | player_rounds | PlayerBestScore |
| `ServerHourlyPatternsBackfillService.cs` | server_online_counts | ServerHourlyPattern |
| `HourlyPlayerPredictionsBackfillService.cs` | server_online_counts | HourlyPlayerPrediction |

### 3. Create Backfill Orchestrator (Task 1.2.7)

Create `api/ClickHouse/BackfillServices/BackfillOrchestrator.cs`:
- Runs all backfill services in dependency order
- Tracks which backfills have completed (use a simple metadata table or file)
- Allows re-running individual services
- Reports progress and errors

### 4. Add CLI Integration

The orchestrator should be invocable via command line. Check how other CLI commands are implemented in the project (likely in Program.cs or a dedicated CLI handler).

Target commands:
```bash
dotnet run -- backfill all
dotnet run -- backfill server-online-counts
dotnet run -- backfill player-stats
```

### 5. Register Services in DI

Add the backfill services to `Program.cs` DI container.

## Key ClickHouse Queries (from analysis.md)

**ServerOnlineCounts (180 days, hourly aggregation):**
```sql
SELECT
    server_guid,
    toStartOfHour(timestamp) as hour_timestamp,
    game,
    AVG(players_online) as avg_players,
    MAX(players_online) as peak_players,
    COUNT(*) as sample_count
FROM server_online_counts
WHERE timestamp >= now() - INTERVAL 180 DAY
GROUP BY server_guid, toStartOfHour(timestamp), game
```

**PlayerStats (lifetime aggregates):**
```sql
SELECT
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    SUM(final_score) as total_score,
    SUM(play_time_minutes) as total_play_time_minutes,
    MIN(round_start_time) as first_round_time,
    MAX(round_end_time) as last_round_time
FROM player_rounds
WHERE is_bot = 0
GROUP BY player_name
```

See migration-plan.md for complete query list.

## Completion Criteria

- [ ] IBackfillService interface created
- [ ] 6 backfill services implemented
- [ ] BackfillOrchestrator working
- [ ] CLI commands functional
- [ ] Services registered in DI
- [ ] `dotnet build` passes

## Notes

- Expect ~4.3M rows for ServerOnlineCounts (180 days)
- Expect ~50k unique players for PlayerStats
- Use batched inserts to avoid memory issues
- Log progress every 10,000 rows
