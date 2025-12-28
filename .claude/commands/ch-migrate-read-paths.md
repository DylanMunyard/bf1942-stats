---
description: "ClickHouse Migration: Stream D - Create SQLite read services to replace ClickHouse queries"
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, mcp__serena__find_symbol, mcp__serena__get_symbols_overview, mcp__serena__list_dir, mcp__serena__search_for_pattern, mcp__serena__replace_symbol_body, mcp__serena__insert_after_symbol, mcp__serena__insert_before_symbol
---

# ClickHouse Migration - Stream D: Read Path Migrations

You are implementing **Stream D** of the ClickHouse to SQLite migration.

## Prerequisites

**Stream A must be complete first.** Verify by checking:
- Entities exist in `api/Data/Entities/` (PlayerStatsLifetime.cs, ServerOnlineCount.cs, etc.)
- Migration exists in `api/Migrations/`

If Stream A is not complete, STOP and inform the user.

## Reference Documents

Read these files first:
- @bf1942-stats/features/clickhouse-migration/migration-plan.md (see "Stream D: Read Paths" and "Phase 3: Read Migration" sections)
- @bf1942-stats/features/clickhouse-migration/analysis.md (see query patterns and response shapes)

## Your Scope

**Directory ownership:**
- `api/GameTrends/Sqlite*.cs` (new files)
- `api/PlayerStats/Sqlite*.cs` (new files)
- `api/Utils/FeatureFlags.cs` (new file)

**DO NOT modify files outside your scope.** Other agents handle other directories.

## Tasks

### 1. Create Feature Flag System (Task 3.6.1)

Create `api/Utils/FeatureFlags.cs`:

```csharp
public enum QuerySource { ClickHouse, SQLite }

public interface IQuerySourceSelector
{
    QuerySource GetSource(string endpointName);
}

public class QuerySourceSelector : IQuerySourceSelector
{
    private readonly IConfiguration _config;

    public QuerySource GetSource(string endpointName)
    {
        // Check config for per-endpoint overrides
        // Default to ClickHouse during migration
        var key = $"ClickHouseMigration:UseSqlite:{endpointName}";
        return _config.GetValue<bool>(key) ? QuerySource.SQLite : QuerySource.ClickHouse;
    }
}
```

### 2. Create SqliteGameTrendsService (Tasks 3.1.1-3.1.3, 3.5.1)

Create `api/GameTrends/SqliteGameTrendsService.cs`:

Implement these methods to query SQLite instead of ClickHouse:

**GetSmartPredictionInsightsAsync:**
```sql
SELECT day_of_week, hour_of_day, predicted_players
FROM HourlyPlayerPredictions
WHERE Game = @game
  AND (day_of_week, hour_of_day) IN (values...)
```

**GetServerBusyIndicatorAsync:**
```sql
SELECT server_guid, hour_of_day, avg_players, q25_players, median_players, q75_players, q90_players
FROM ServerHourlyPatterns
WHERE server_guid IN (@guids)
  AND day_of_week = @dow
  AND hour_of_day IN (@hours)
```

**GetPlayersOnlineHistoryAsync:**
```sql
SELECT hour_timestamp, SUM(avg_players) as total_players
FROM ServerOnlineCounts
WHERE game = @game
  AND hour_timestamp >= @start
GROUP BY hour_timestamp
ORDER BY hour_timestamp
```

**GetWeeklyActivityPatternsAsync:**
```sql
SELECT day_of_week, hour_of_day, unique_players_avg, total_rounds_avg, avg_round_duration, period_type
FROM HourlyActivityPatterns
WHERE game = @game
ORDER BY day_of_week, hour_of_day
```

**Requirements:**
- Match response shapes exactly to ClickHouse versions
- Emit telemetry using ActivitySources.SqliteAnalytics
- Include query.name, result.row_count, result.duration_ms tags

### 3. Create SqliteLeaderboardService (Tasks 3.2.1-3.2.4)

Create `api/PlayerStats/SqliteLeaderboardService.cs`:

**GetTopScoresAsync, GetTopKDRatiosAsync, GetTopKillRatesAsync, GetMostActivePlayersAsync:**

All use same pattern:
```sql
SELECT player_name, value, total_rounds
FROM ServerLeaderboardEntries
WHERE server_guid = @serverGuid
  AND period = @period
  AND ranking_type = @rankingType
ORDER BY rank
LIMIT @limit
```

### 4. Create SqlitePlayerStatsService (Tasks 3.3.1-3.3.6)

Create `api/PlayerStats/SqlitePlayerStatsService.cs`:

**GetPlayerStatsAsync:**
```sql
SELECT * FROM PlayerStatsLifetime WHERE player_name = @playerName
```

**GetServerStats (map breakdown):**
```sql
SELECT * FROM PlayerMapStats
WHERE player_name = @playerName
  AND (@serverGuid IS NULL OR server_guid = @serverGuid)
```

**GetPlayerServerInsightsAsync:**
```sql
SELECT * FROM PlayerServerStats
WHERE player_name = @playerName
  AND total_play_time_minutes >= 600  -- 10+ hours
ORDER BY total_play_time_minutes DESC
```

**GetPlayerBestScoresAsync:**
```sql
SELECT * FROM PlayerBestScores
WHERE player_name = @playerName
ORDER BY period, rank
```

**GetPlayersKillMilestonesAsync:**
```sql
SELECT * FROM PlayerMilestones
WHERE player_name IN (@playerNames)
ORDER BY player_name, milestone
```

**GetAveragePing:**
Query existing PlayerSessions table:
```sql
SELECT player_name, AVG(AveragePing) as avg_ping
FROM PlayerSessions
WHERE player_name IN (@players)
  AND AveragePing > 0 AND AveragePing < 1000
  AND RoundStartTime >= datetime('now', '-7 days')
GROUP BY player_name
```

### 5. Create SqlitePlayerComparisonService (Tasks 3.4.1-3.4.4)

Create `api/PlayerStats/SqlitePlayerComparisonService.cs`:

**GetBucketTotals:**
- AllTime: Query PlayerStatsLifetime
- Rolling periods: Query PlayerStatsRolling or compute on-demand

**GetMapPerformance:**
```sql
SELECT * FROM PlayerMapStats
WHERE player_name IN (@player1, @player2)
```

**GetHeadToHeadData:**
```sql
SELECT
    p1.round_start_time, p1.round_end_time, p1.server_guid, p1.map_name,
    p1.final_score as player1_score, p1.final_kills as player1_kills, p1.final_deaths as player1_deaths,
    p2.final_score as player2_score, p2.final_kills as player2_kills, p2.final_deaths as player2_deaths,
    p1.round_id
FROM PlayerSessions p1
JOIN PlayerSessions p2
  ON p1.server_guid = p2.server_guid
  AND p1.map_name = p2.map_name
  AND p1.round_start_time < p2.round_end_time
  AND p2.round_start_time < p1.round_end_time
WHERE p1.player_name = @player1
  AND p2.player_name = @player2
  AND p1.round_start_time >= datetime('now', '-6 months')
ORDER BY p1.round_start_time DESC
LIMIT 50
```

**GetCommonServersData:**
```sql
SELECT DISTINCT server_guid FROM PlayerSessions WHERE player_name = @player1
INTERSECT
SELECT DISTINCT server_guid FROM PlayerSessions WHERE player_name = @player2
```

### 6. Update Controllers to Use Feature Flags

For each endpoint, inject IQuerySourceSelector and call appropriate service:

```csharp
public async Task<IActionResult> GetLeaderboard(...)
{
    var source = _querySourceSelector.GetSource("GetTopScores");
    var result = source == QuerySource.SQLite
        ? await _sqliteLeaderboardService.GetTopScoresAsync(...)
        : await _clickHouseLeaderboardService.GetTopScoresAsync(...);
    return Ok(result);
}
```

### 7. Register Services in DI

Add to `Program.cs`:
- IQuerySourceSelector
- SqliteGameTrendsService
- SqliteLeaderboardService
- SqlitePlayerStatsService
- SqlitePlayerComparisonService

## Completion Criteria

- [ ] Feature flag system implemented
- [ ] SqliteGameTrendsService with 4 methods
- [ ] SqliteLeaderboardService with 4 methods
- [ ] SqlitePlayerStatsService with 6 methods
- [ ] SqlitePlayerComparisonService with 4 methods
- [ ] All queries emit telemetry
- [ ] Services registered in DI
- [ ] Controllers updated to use feature flags
- [ ] `dotnet build` passes

## Testing

After implementation:
1. Enable SQLite for one endpoint via config
2. Compare responses between ClickHouse and SQLite
3. Verify telemetry is emitted
4. Gradually enable more endpoints
