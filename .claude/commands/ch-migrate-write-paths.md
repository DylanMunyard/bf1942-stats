---
description: "ClickHouse Migration: Stream C - Update write paths to dual-write to SQLite"
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, mcp__serena__find_symbol, mcp__serena__get_symbols_overview, mcp__serena__list_dir, mcp__serena__search_for_pattern, mcp__serena__replace_symbol_body, mcp__serena__insert_after_symbol, mcp__serena__insert_before_symbol
---

# ClickHouse Migration - Stream C: Write Paths & Background Jobs

You are implementing **Stream C** of the ClickHouse to SQLite migration.

## Prerequisites

**Stream A must be complete first.** Verify by checking:
- Entities exist in `api/Data/Entities/` (PlayerStatsLifetime.cs, ServerOnlineCount.cs, etc.)
- Migration exists in `api/Migrations/`

If Stream A is not complete, STOP and inform the user.

## Reference Documents

Read these files first:
- @bf1942-stats/features/clickhouse-migration/migration-plan.md (see "Stream C: Write Paths" and "Phase 2: Dual-Write" sections)
- @bf1942-stats/features/clickhouse-migration/analysis.md (see update strategies)

## Your Scope

**Directory ownership:**
- `api/Services/AggregateUpdate*.cs` (new files)
- `api/Services/BackgroundJobs/` (new directory)
- `api/StatsCollectors/` (modify existing)
- `api/ClickHouse/PlayerRoundsWriteService.cs` (modify existing)

**DO NOT modify files outside your scope.** Other agents handle other directories.

## Tasks

### 1. Create Aggregate Update Queue (Task 2.1.1)

Create `api/Services/AggregateUpdateQueueService.cs`:

```csharp
public interface IAggregateUpdateQueueService
{
    void EnqueuePlayerUpdate(string playerName, string serverGuid, RoundCompletionData data);
    Task ProcessQueueAsync(CancellationToken ct);
}

public record RoundCompletionData(
    int Kills,
    int Deaths,
    int Score,
    double PlayTimeMinutes,
    string MapName,
    string RoundId,
    Instant RoundEndTime
);
```

**Implementation requirements:**
- In-memory ConcurrentQueue
- Deduplication by (playerName, serverGuid) - keep latest data
- Background worker processes queue every 5 seconds
- Handles shutdown gracefully

### 2. Create Aggregate Update Processor (Task 2.1.3)

Create `api/Services/AggregateUpdateProcessor.cs`:

Processes queued updates to populate:
- `PlayerStatsLifetime` - upsert lifetime totals
- `PlayerServerStats` - upsert per-server stats, track highest score
- `PlayerMapStats` - upsert per-map stats
- `PlayerDailyStats` - upsert daily aggregates
- `PlayerMilestone` - insert if threshold crossed
- `PlayerBestScore` - insert if qualifies for top 3

**Critical: Milestone detection:**
```csharp
var milestones = new[] { 5000, 10000, 20000, 50000, 75000, 100000 };
foreach (var milestone in milestones)
{
    if (previousTotalKills < milestone && newTotalKills >= milestone)
    {
        // Insert milestone record
    }
}
```

**Critical: Best score update:**
```csharp
// Check if this score qualifies for top 3 in any period
foreach (var period in new[] { "this_week", "last_30_days", "all_time" })
{
    var currentBest = await GetBestScoresAsync(playerName, period);
    if (score > currentBest.MinScore || currentBest.Count < 3)
    {
        await UpdateBestScoresAsync(playerName, period, roundData);
    }
}
```

### 3. Update PlayerRoundsWriteService (Task 2.1.2)

Modify `api/ClickHouse/PlayerRoundsWriteService.cs`:

After writing to ClickHouse, enqueue the update:
```csharp
_aggregateUpdateQueue.EnqueuePlayerUpdate(
    playerName,
    serverGuid,
    new RoundCompletionData(kills, deaths, score, playTime, mapName, roundId, roundEndTime)
);
```

### 4. Update StatsCollectionBackgroundService (Task 2.2.1)

Modify `api/StatsCollectors/StatsCollectionBackgroundService.cs`:

Add SQLite collection for ServerOnlineCounts:
```csharp
// On each 30-second tick, upsert to SQLite
var hourTimestamp = timestamp.TruncateToHour();
await UpsertServerOnlineCountAsync(serverGuid, hourTimestamp, game, playersOnline);
```

**Upsert pattern:**
```sql
INSERT INTO ServerOnlineCounts (ServerGuid, HourTimestamp, Game, AvgPlayers, PeakPlayers, SampleCount)
VALUES (@guid, @hour, @game, @players, @players, 1)
ON CONFLICT(ServerGuid, HourTimestamp) DO UPDATE SET
    AvgPlayers = (AvgPlayers * SampleCount + @players) / (SampleCount + 1),
    PeakPlayers = MAX(PeakPlayers, @players),
    SampleCount = SampleCount + 1
```

### 5. Create Background Refresh Jobs (Tasks 2.3.1-2.3.3)

Create `api/Services/BackgroundJobs/` directory with:

**DailyAggregateRefreshJob.cs:**
- Runs at 4 AM UTC daily
- Refreshes rolling aggregates (last_30_days, last_6_months, last_year)
- Refreshes ServerHourlyPatterns percentiles
- Refreshes HourlyPlayerPredictions
- Refreshes MapGlobalAverages

**WeeklyCleanupJob.cs:**
- Runs weekly
- Removes stale "this_week" entries from PlayerBestScores
- Prunes old ServerOnlineCounts (keep 180 days)

**LeaderboardRefreshJob.cs:**
- Runs hourly
- Refreshes ServerLeaderboardEntries for all active servers
- Computes rankings for each (server, period, ranking_type)

Use IHostedService or a scheduling library (check what's already in use in the project).

### 6. Register Services in DI

Add to `Program.cs`:
- IAggregateUpdateQueueService
- AggregateUpdateProcessor
- Background jobs as hosted services

## Completion Criteria

- [ ] Aggregate update queue implemented
- [ ] Aggregate update processor handles all tables
- [ ] PlayerRoundsWriteService queues updates
- [ ] StatsCollectionBackgroundService collects to SQLite
- [ ] 3 background jobs created and scheduled
- [ ] All services registered in DI
- [ ] `dotnet build` passes

## Testing

After implementation:
1. Create a test round completion
2. Verify aggregate tables are updated
3. Verify ServerOnlineCounts receives data every 30 seconds
4. Verify background jobs run on schedule
