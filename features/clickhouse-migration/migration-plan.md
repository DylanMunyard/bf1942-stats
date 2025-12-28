# ClickHouse Migration Plan

This document contains the specific implementation tasks for migrating from ClickHouse to SQLite. Based on the analysis in `analysis.md`.

**Goal:** Retire ClickHouse to reduce production costs while maintaining equivalent functionality.

---

## Summary

| Phase | Tasks | Est. Effort |
|-------|-------|-------------|
| Phase 1: Schema & Backfill | 12 tasks | Large |
| Phase 2: Dual-Write | 8 tasks | Medium |
| Phase 3: Read Migration | 14 tasks | Large |
| Phase 4: Decommission | 5 tasks | Small |

---

## Work Streams for Parallel Execution

Tasks are organized into independent work streams that can be assigned to different agents. **Stream A must complete first** as it creates the foundation (entities, migration). After that, Streams B, C, and D can run in parallel.

```
                    ┌─────────────────┐
                    │   STREAM A      │
                    │   Foundation    │
                    │   (Sequential)  │
                    └────────┬────────┘
                             │
         ┌───────────────────┼───────────────────┐
         │                   │                   │
         ▼                   ▼                   ▼
┌─────────────────┐ ┌─────────────────┐ ┌─────────────────┐
│   STREAM B      │ │   STREAM C      │ │   STREAM D      │
│   Backfill      │ │   Write Paths   │ │   Read Paths    │
│   (Parallel)    │ │   (Parallel)    │ │   (Parallel)    │
└─────────────────┘ └─────────────────┘ └─────────────────┘
```

### Stream A: Foundation (BLOCKING - Must Complete First)
**Owner:** Agent 1
**Directory scope:** `api/Data/Entities/`, `api/Telemetry/`

| Task | Description | Files |
|------|-------------|-------|
| 1.1.1 | EF Core entities for player aggregates | `api/Data/Entities/PlayerStatsLifetime.cs`, `PlayerServerStats.cs`, `PlayerMapStats.cs`, `PlayerDailyStats.cs`, `PlayerMilestone.cs`, `PlayerBestScore.cs` |
| 1.1.2 | EF Core entities for server analytics | `api/Data/Entities/ServerOnlineCount.cs`, `ServerHourlyPattern.cs`, `HourlyPlayerPrediction.cs`, `HourlyActivityPattern.cs` |
| 1.1.3 | EF Core entities for leaderboards | `api/Data/Entities/ServerLeaderboardEntry.cs`, `MapGlobalAverage.cs` |
| 1.1.4 | Generate EF migration | `api/Migrations/` |
| 1.3.1 | Telemetry ActivitySource | `api/Telemetry/ActivitySources.cs` |

**Deliverables:**
- All 12 entity classes created
- DbContext updated with DbSets and configurations
- Migration generated and tested
- ActivitySource for SQLite analytics

**Completion signal:** Migration applies successfully, `dotnet build` passes

---

### Stream B: Backfill Services (Parallel after Stream A)
**Owner:** Agent 2
**Directory scope:** `api/ClickHouse/BackfillServices/` (new directory)
**Dependency:** Stream A entities must exist

| Task | Description | Files |
|------|-------------|-------|
| 1.2.1 | Backfill server_online_counts | `ServerOnlineCountsBackfillService.cs` |
| 1.2.2 | Backfill player aggregate stats | `PlayerStatsBackfillService.cs` |
| 1.2.3 | Backfill player milestones | `PlayerMilestonesBackfillService.cs` |
| 1.2.4 | Backfill player best scores | `PlayerBestScoresBackfillService.cs` |
| 1.2.5 | Backfill server hourly patterns | `ServerHourlyPatternsBackfillService.cs` |
| 1.2.6 | Backfill hourly player predictions | `HourlyPlayerPredictionsBackfillService.cs` |
| 1.2.7 | Backfill orchestrator + CLI | `BackfillOrchestrator.cs`, `IBackfillService.cs` |

**Deliverables:**
- 6 backfill services implementing `IBackfillService`
- Orchestrator that runs all in correct order
- CLI command integration (`dotnet run -- backfill <name>`)
- Resume capability for large backfills

**Completion signal:** All backfill services can query ClickHouse and write to SQLite

---

### Stream C: Write Paths & Background Jobs (Parallel after Stream A)
**Owner:** Agent 3
**Directory scope:** `api/Services/`, `api/StatsCollectors/`
**Dependency:** Stream A entities must exist

| Task | Description | Files |
|------|-------------|-------|
| 2.1.1 | Aggregate update queue service | `api/Services/AggregateUpdateQueueService.cs` |
| 2.1.2 | Update PlayerRoundsWriteService | `api/ClickHouse/PlayerRoundsWriteService.cs` (modify) |
| 2.1.3 | Aggregate update processor | `api/Services/AggregateUpdateProcessor.cs` |
| 2.2.1 | SQLite collection in StatsCollectionBackgroundService | `api/StatsCollectors/StatsCollectionBackgroundService.cs` (modify) |
| 2.3.1 | Daily aggregate refresh job | `api/Services/BackgroundJobs/DailyAggregateRefreshJob.cs` |
| 2.3.2 | Weekly cleanup job | `api/Services/BackgroundJobs/WeeklyCleanupJob.cs` |
| 2.3.3 | Leaderboard refresh job | `api/Services/BackgroundJobs/LeaderboardRefreshJob.cs` |

**Deliverables:**
- Queue-based aggregate update system
- Round completion triggers SQLite updates
- Server online counts collected into SQLite
- 3 background jobs for periodic refresh

**Completion signal:** Round completion populates all aggregate tables, background jobs run on schedule

---

### Stream D: Read Path Migrations (Parallel after Stream A)
**Owner:** Agent 4 (or split between Agent 2/3 after their streams complete)
**Directory scope:** `api/GameTrends/`, `api/PlayerStats/`, `api/Utils/`
**Dependency:** Stream A entities must exist

| Task | Description | Files |
|------|-------------|-------|
| 3.1.1-3.1.3 | Landing page queries | `api/GameTrends/SqliteGameTrendsService.cs` |
| 3.2.1-3.2.4 | Leaderboard queries | `api/PlayerStats/SqliteLeaderboardService.cs` |
| 3.3.1-3.3.6 | Player stats queries | `api/PlayerStats/SqlitePlayerStatsService.cs` |
| 3.4.1-3.4.4 | Player comparison queries | `api/PlayerStats/SqlitePlayerComparisonService.cs` |
| 3.5.1-3.5.2 | Activity patterns | (add to SqliteGameTrendsService) |
| 3.6.1 | Feature flags | `api/Utils/FeatureFlags.cs`, `IQuerySourceSelector.cs` |

**Deliverables:**
- 4 new SQLite read services
- Feature flag system for gradual rollout
- All queries emit telemetry
- Response shapes match ClickHouse exactly

**Completion signal:** All endpoints can serve from SQLite with feature flag enabled

---

### Two-Agent Configuration

If only two agents are available, combine streams:

| Agent | Streams | Focus |
|-------|---------|-------|
| **Agent 1** | A → B | Data layer: entities, migration, all backfill services |
| **Agent 2** | (wait) → C + D | Service layer: write paths, background jobs, read services |

**Handoff point:** Agent 1 completes Stream A, signals Agent 2 to begin. Agent 1 continues with Stream B while Agent 2 works on C+D.

### Three-Agent Configuration

| Agent | Streams | Focus |
|-------|---------|-------|
| **Agent 1** | A → B | Foundation + Backfill |
| **Agent 2** | (wait) → C | Write paths + Background jobs |
| **Agent 3** | (wait) → D | Read path migrations |

---

### File Ownership Rules (Conflict Prevention)

To prevent agents from modifying the same files:

| Directory/File | Owner |
|----------------|-------|
| `api/Data/Entities/*.cs` | Stream A only |
| `api/Data/PlayerTrackerContext.cs` | Stream A only |
| `api/Migrations/*` | Stream A only |
| `api/ClickHouse/BackfillServices/*` | Stream B only |
| `api/Services/AggregateUpdate*.cs` | Stream C only |
| `api/Services/BackgroundJobs/*` | Stream C only |
| `api/StatsCollectors/*` | Stream C only |
| `api/GameTrends/Sqlite*.cs` | Stream D only |
| `api/PlayerStats/Sqlite*.cs` | Stream D only |
| `api/Utils/FeatureFlags.cs` | Stream D only |
| `api/ClickHouse/PlayerRoundsWriteService.cs` | Stream C only |

**Shared files (coordinate carefully):**
- `api/Program.cs` - DI registration (each stream adds their services)
- `api/api.csproj` - package references (rare)

---

## Phase 1: Schema & Backfill

Create new SQLite tables and populate with historical data from ClickHouse.

### 1.1 Core Infrastructure

#### Task 1.1.1: Create EF Core entities for new aggregate tables
**Files to create:**
- `api/Data/Entities/PlayerStatsLifetime.cs`
- `api/Data/Entities/PlayerServerStats.cs`
- `api/Data/Entities/PlayerMapStats.cs`
- `api/Data/Entities/PlayerDailyStats.cs`
- `api/Data/Entities/PlayerMilestone.cs`
- `api/Data/Entities/PlayerBestScore.cs`

**Schema (from analysis):**
```csharp
public class PlayerStatsLifetime
{
    public required string PlayerName { get; set; }
    public int TotalRounds { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public double TotalPlayTimeMinutes { get; set; }
    public double AvgScorePerRound { get; set; }
    public double KdRatio { get; set; }
    public double KillRate { get; set; }
    public Instant FirstRoundTime { get; set; }
    public Instant LastRoundTime { get; set; }
    public Instant UpdatedAt { get; set; }
}
```

**Acceptance criteria:**
- [ ] All entities use NodaTime Instant for timestamps
- [ ] DbContext updated with DbSet for each entity
- [ ] Appropriate indexes defined in OnModelCreating

---

#### Task 1.1.2: Create EF Core entities for server analytics tables
**Files to create:**
- `api/Data/Entities/ServerOnlineCount.cs`
- `api/Data/Entities/ServerHourlyPattern.cs`
- `api/Data/Entities/HourlyPlayerPrediction.cs`
- `api/Data/Entities/HourlyActivityPattern.cs`

**Schema for ServerOnlineCount:**
```csharp
public class ServerOnlineCount
{
    public required string ServerGuid { get; set; }
    public Instant HourTimestamp { get; set; }  // truncated to hour
    public required string Game { get; set; }
    public double AvgPlayers { get; set; }
    public int PeakPlayers { get; set; }
    public int SampleCount { get; set; }
}
```

**Acceptance criteria:**
- [ ] Composite primary keys configured correctly
- [ ] Indexes on (game, hour_timestamp) for prediction queries

---

#### Task 1.1.3: Create EF Core entities for leaderboard tables
**Files to create:**
- `api/Data/Entities/ServerLeaderboardEntry.cs`
- `api/Data/Entities/MapGlobalAverage.cs`

**Schema for ServerLeaderboardEntry:**
```csharp
public class ServerLeaderboardEntry
{
    public required string ServerGuid { get; set; }
    public required string Period { get; set; }      // 'weekly', 'monthly', 'all_time'
    public required string RankingType { get; set; } // 'score', 'kills', 'kd_ratio', 'kill_rate', 'playtime'
    public int Rank { get; set; }
    public required string PlayerName { get; set; }
    public double Value { get; set; }
    public int TotalRounds { get; set; }
    public Instant UpdatedAt { get; set; }
}
```

---

#### Task 1.1.4: Generate and run EF Core migration
**Commands:**
```bash
cd bf1942-stats/api
dotnet ef migrations add AddClickHouseMigrationTables
dotnet ef database update
```

**Acceptance criteria:**
- [ ] Migration applies cleanly
- [ ] All tables created with correct schema
- [ ] Indexes verified in SQLite

---

### 1.2 Backfill Jobs

#### Task 1.2.1: Create backfill service for server_online_counts
**File:** `api/ClickHouse/BackfillServices/ServerOnlineCountsBackfillService.cs`

**Implementation:**
1. Query ClickHouse for last 180 days of server_online_counts, aggregated to hourly
2. Batch insert into SQLite (use 1000-row batches)
3. Log progress and handle resume on failure

**ClickHouse query (from analysis):**
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

**Acceptance criteria:**
- [ ] Handles ~4.3M rows efficiently
- [ ] Resume capability (tracks last processed hour)
- [ ] Emits telemetry for monitoring

---

#### Task 1.2.2: Create backfill service for player aggregate stats
**File:** `api/ClickHouse/BackfillServices/PlayerStatsBackfillService.cs`

**Implementation:**
1. Query ClickHouse player_rounds for lifetime aggregates per player
2. Populate player_stats_lifetime table
3. Also compute player_server_stats and player_map_stats

**ClickHouse queries:**
```sql
-- Lifetime stats
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

-- Server stats (includes argMax for highest score)
SELECT
    player_name,
    server_guid,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    SUM(final_score) as total_score,
    SUM(play_time_minutes) as total_play_time_minutes,
    MAX(final_score) as highest_score,
    argMax(round_id, final_score) as highest_score_round_id,
    argMax(map_name, final_score) as highest_score_map_name,
    argMax(round_start_time, final_score) as highest_score_time
FROM player_rounds
WHERE is_bot = 0
GROUP BY player_name, server_guid
```

**Acceptance criteria:**
- [ ] Handles ~50k unique players
- [ ] Batched inserts for performance
- [ ] Validates row counts match ClickHouse

---

#### Task 1.2.3: Create backfill service for player milestones
**File:** `api/ClickHouse/BackfillServices/PlayerMilestonesBackfillService.cs`

**Implementation:**
1. Run GetPlayersKillMilestonesAsync query against ClickHouse for all players
2. Insert into player_milestones table

**Acceptance criteria:**
- [ ] All existing milestones captured
- [ ] Days-to-achieve calculated correctly

---

#### Task 1.2.4: Create backfill service for player best scores
**File:** `api/ClickHouse/BackfillServices/PlayerBestScoresBackfillService.cs`

**Implementation:**
1. Query top 3 scores per player per period from ClickHouse
2. Insert into player_best_scores table

**Note:** "this_week" entries will become stale - document that these get refreshed by write-time tracking.

---

#### Task 1.2.5: Create backfill service for server hourly patterns
**File:** `api/ClickHouse/BackfillServices/ServerHourlyPatternsBackfillService.cs`

**Implementation:**
1. Query ClickHouse for historical server patterns (last 60 days)
2. Calculate percentiles (min, q25, median, q75, q90, max) in C#
3. Insert into server_hourly_patterns table

---

#### Task 1.2.6: Create backfill service for hourly player predictions
**File:** `api/ClickHouse/BackfillServices/HourlyPlayerPredictionsBackfillService.cs`

**Implementation:**
1. Query ClickHouse GetSmartPredictionInsightsAsync patterns
2. Insert 168 rows per game (7 days × 24 hours)

---

#### Task 1.2.7: Create backfill orchestrator and CLI command
**File:** `api/ClickHouse/BackfillServices/BackfillOrchestrator.cs`

**Implementation:**
- Runs all backfill services in correct order
- Tracks completion status in a metadata table
- Allows re-running individual services

**CLI invocation:**
```bash
dotnet run -- backfill all
dotnet run -- backfill server-online-counts
dotnet run -- backfill player-stats
# etc.
```

---

### 1.3 Telemetry Setup

#### Task 1.3.1: Create ActivitySource for SQLite analytics
**File:** `api/Telemetry/ActivitySources.cs` (modify existing)

**Implementation:**
```csharp
public static readonly ActivitySource SqliteAnalytics = new("BfStats.SqliteAnalytics");
```

**Standard tags for all queries:**
- `query.name` - method name
- `query.filters` - filter description
- `result.row_count` - rows returned
- `result.duration_ms` - execution time
- `result.table` - primary table queried
- `result.cache_hit` - boolean

---

## Phase 2: Dual-Write

Update write paths to populate both ClickHouse and new SQLite tables.

### 2.1 Round Completion Handler Updates

#### Task 2.1.1: Create aggregate update queue service
**File:** `api/Services/AggregateUpdateQueueService.cs`

**Implementation:**
- In-memory queue for aggregate updates
- Deduplication by (player_name, server_guid) key
- Background worker processes queue every N seconds
- Prevents write contention during round completion

**Interface:**
```csharp
public interface IAggregateUpdateQueueService
{
    void EnqueuePlayerUpdate(string playerName, string serverGuid, RoundCompletionData data);
    Task ProcessQueueAsync(CancellationToken ct);
}
```

---

#### Task 2.1.2: Update PlayerRoundsWriteService to queue aggregate updates
**File:** `api/ClickHouse/PlayerRoundsWriteService.cs` (modify)

**Implementation:**
After writing to ClickHouse, enqueue updates for:
- player_stats_lifetime
- player_server_stats
- player_map_stats
- player_daily_stats
- player_milestones (check threshold crossing)
- player_best_scores (check if qualifies)

---

#### Task 2.1.3: Implement aggregate update processor
**File:** `api/Services/AggregateUpdateProcessor.cs`

**Implementation:**
- Processes queued updates in batches
- Uses upsert patterns for all tables
- Handles milestone detection (previous < threshold <= new)
- Handles best score insertion (score > current min or count < 3)

**Critical: Milestone detection logic:**
```csharp
var milestones = new[] { 5000, 10000, 20000, 50000, 75000, 100000 };
foreach (var milestone in milestones)
{
    if (previousTotalKills < milestone && newTotalKills >= milestone)
    {
        await InsertMilestoneAsync(playerName, milestone, roundEndTime, newTotalKills, daysPlaying);
    }
}
```

---

### 2.2 Server Online Counts Collection

#### Task 2.2.1: Update StatsCollectionBackgroundService for SQLite collection
**File:** `api/StatsCollectors/StatsCollectionBackgroundService.cs` (modify)

**Implementation:**
- Add hourly accumulator per server
- On each 30-second tick, update running average
- Upsert to SQLite server_online_counts table

**Upsert pattern:**
```csharp
var hourTimestamp = timestamp.ToString("yyyy-MM-ddTHH:00:00Z");
await dbContext.Database.ExecuteSqlRawAsync(@"
    INSERT INTO ServerOnlineCounts (ServerGuid, HourTimestamp, Game, AvgPlayers, PeakPlayers, SampleCount)
    VALUES ({0}, {1}, {2}, {3}, {3}, 1)
    ON CONFLICT(ServerGuid, HourTimestamp) DO UPDATE SET
        AvgPlayers = (AvgPlayers * SampleCount + {3}) / (SampleCount + 1),
        PeakPlayers = MAX(PeakPlayers, {3}),
        SampleCount = SampleCount + 1",
    serverGuid, hourTimestamp, game, playersOnline);
```

---

### 2.3 Background Refresh Jobs

#### Task 2.3.1: Create daily aggregate refresh job
**File:** `api/Services/BackgroundJobs/DailyAggregateRefreshJob.cs`

**Implementation:**
- Runs once per day (e.g., 4 AM UTC)
- Refreshes rolling aggregates (player_stats_rolling for last_30_days, etc.)
- Refreshes server_hourly_patterns percentiles
- Refreshes hourly_player_predictions
- Refreshes map_global_averages

---

#### Task 2.3.2: Create weekly cleanup job
**File:** `api/Services/BackgroundJobs/WeeklyCleanupJob.cs`

**Implementation:**
- Removes stale "this_week" entries from player_best_scores
- Recalculates "last_30_days" best scores from player_rounds
- Prunes old server_online_counts (keep 180 days)

---

#### Task 2.3.3: Create leaderboard refresh job
**File:** `api/Services/BackgroundJobs/LeaderboardRefreshJob.cs`

**Implementation:**
- Runs hourly or daily
- Refreshes server_leaderboards for all active servers
- Computes rankings for each (server, period, ranking_type) combination

---

## Phase 3: Read Migration

Switch read paths from ClickHouse to SQLite, one endpoint at a time.

### 3.1 High Priority: Landing Page Queries

These power the highest-traffic page.

#### Task 3.1.1: Migrate GetSmartPredictionInsightsAsync
**Current:** `api/ClickHouse/GameTrendsService.cs:229-373`
**New service:** `api/GameTrends/SqliteGameTrendsService.cs`

**Implementation:**
1. Query hourly_player_predictions for current + next 8 hours
2. Get current player count from live server data
3. Return same response shape

**SQLite query:**
```sql
SELECT day_of_week, hour_of_day, avg_players as predicted_players
FROM HourlyPlayerPredictions
WHERE Game = @game
  AND (day_of_week, hour_of_day) IN ((@dow0, @hour0), (@dow1, @hour1), ...)
```

**Acceptance criteria:**
- [ ] Response matches ClickHouse exactly
- [ ] Telemetry emitted with query.name, duration_ms, etc.
- [ ] Feature flag to toggle between implementations

---

#### Task 3.1.2: Migrate GetServerBusyIndicatorAsync
**Current:** `api/ClickHouse/GameTrendsService.cs:376-592`
**New service:** `api/GameTrends/SqliteGameTrendsService.cs`

**Implementation:**
1. Query server_hourly_patterns for requested servers + hours
2. Get current player counts from live server data
3. Calculate busyness level from percentile data

**SQLite query:**
```sql
SELECT server_guid, hour_of_day, avg_players, q25_players, median_players, q75_players, q90_players
FROM ServerHourlyPatterns
WHERE server_guid IN (@guids)
  AND day_of_week = @dow
  AND hour_of_day IN (@hours)
```

---

#### Task 3.1.3: Migrate players-online-history endpoint
**Current:** Uses PlayersOnlineHistoryService with ClickHouse
**New:** Query ServerOnlineCounts SQLite table

**Implementation:**
- Query last N days of hourly data for requested game
- Aggregate to appropriate granularity for chart display

---

### 3.2 High Priority: Server Leaderboards

These power ServerDetails page.

#### Task 3.2.1: Migrate GetTopScoresAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:143-187`
**New service:** `api/PlayerStats/SqliteLeaderboardService.cs`

**Implementation:**
1. Query server_leaderboards where ranking_type = 'score'
2. Apply period and limit filters
3. Return same response shape

**SQLite query:**
```sql
SELECT player_name, value as total_score, total_rounds
FROM ServerLeaderboardEntries
WHERE server_guid = @serverGuid
  AND period = @period
  AND ranking_type = 'score'
ORDER BY rank
LIMIT @limit
```

---

#### Task 3.2.2: Migrate GetTopKDRatiosAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:192-235`

**Implementation:** Same pattern as GetTopScoresAsync but ranking_type = 'kd_ratio'

---

#### Task 3.2.3: Migrate GetTopKillRatesAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:240-287`

**Implementation:** Same pattern but ranking_type = 'kill_rate'

---

#### Task 3.2.4: Migrate GetMostActivePlayersAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:101-138`

**Implementation:** Same pattern but ranking_type = 'playtime'

---

### 3.3 Medium Priority: Player Stats

#### Task 3.3.1: Migrate GetPlayerStatsAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:60-96`
**New service:** `api/PlayerStats/SqlitePlayerStatsService.cs`

**Implementation:**
1. Query player_stats_lifetime for lifetime stats
2. For time-filtered queries, either:
   - Query player_stats_rolling if available
   - Compute on-demand from PlayerSessions

---

#### Task 3.3.2: Migrate GetServerStats (map breakdown)
**Current:** `api/ClickHouse/ServerStatisticsService.cs:44-101`

**Implementation:**
1. Query player_map_stats for player's stats by map
2. Apply optional server_guid filter

---

#### Task 3.3.3: Migrate GetPlayerServerInsightsAsync
**Current:** `api/ClickHouse/PlayerInsightsService.cs:112-162`

**Implementation:**
1. Query player_server_stats for player's stats per server
2. Includes highest_score_* columns for insights

---

#### Task 3.3.4: Migrate GetPlayerBestScoresAsync
**Current:** `api/ClickHouse/PlayerRoundsReadService.cs:336-433`

**Implementation:**
1. Query player_best_scores for player's top 3 scores per period
2. Return up to 9 records (3 periods × 3 scores)

---

#### Task 3.3.5: Migrate GetPlayersKillMilestonesAsync
**Current:** `api/ClickHouse/PlayerInsightsService.cs:20-107`

**Implementation:**
1. Query player_milestones for player's achieved milestones
2. Simple SELECT with filter on player_name

---

#### Task 3.3.6: Migrate GetAveragePing (player comparison)
**Current:** `api/ClickHouse/PlayerComparisonService.cs:215-243`

**Implementation:**
1. Query PlayerSessions table (already in SQLite)
2. Filter to last 7 days, calculate average of AveragePing column

**SQLite query:**
```sql
SELECT player_name, AVG(AveragePing) as avg_ping
FROM PlayerSessions
WHERE player_name IN (@player1, @player2)
  AND AveragePing > 0 AND AveragePing < 1000
  AND RoundStartTime >= datetime('now', '-7 days')
GROUP BY player_name
```

---

### 3.4 Medium Priority: Player Comparison

#### Task 3.4.1: Migrate GetBucketTotals
**Current:** `api/ClickHouse/PlayerComparisonService.cs:168-213`

**Implementation:**
- For "AllTime" bucket: Query player_stats_lifetime
- For rolling buckets: Query player_stats_rolling or compute on-demand

---

#### Task 3.4.2: Migrate GetMapPerformance
**Current:** `api/ClickHouse/PlayerComparisonService.cs:245-278`

**Implementation:**
- Query player_map_stats for both players
- Group by map_name

---

#### Task 3.4.3: Migrate GetHeadToHeadData
**Current:** `api/ClickHouse/PlayerComparisonService.cs:280-345`

**Implementation:**
- Query PlayerSessions table directly (self-join)
- Already analyzed as feasible in SQLite

**SQLite query:**
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

---

#### Task 3.4.4: Migrate GetCommonServersData
**Current:** `api/ClickHouse/PlayerComparisonService.cs:348-389`

**Implementation:**
- Query distinct server_guids from PlayerSessions for each player
- Use INTERSECT or application-level intersection

---

### 3.5 Low Priority: Activity Patterns

#### Task 3.5.1: Migrate GetWeeklyActivityPatternsAsync
**Current:** `api/ClickHouse/GameTrendsService.cs:194-223`

**Implementation:**
- Query hourly_activity_patterns table
- Return 168 rows (7 days × 24 hours)

---

#### Task 3.5.2: Migrate GetPlayerActivityHours
**Current:** `api/ClickHouse/PlayerComparisonService.cs:413-459`

**Implementation:**
- Query PlayerSessions, aggregate by hour
- Or pre-compute in player_activity_hours table

---

### 3.6 Feature Flags & A/B Testing

#### Task 3.6.1: Implement ClickHouse/SQLite toggle
**File:** `api/Utils/FeatureFlags.cs`

**Implementation:**
- Per-endpoint feature flags
- Allows gradual rollout
- Easy rollback per endpoint

```csharp
public interface IQuerySourceSelector
{
    QuerySource GetSource(string endpointName); // ClickHouse or SQLite
}
```

---

## Phase 4: Decommission

Remove ClickHouse dependencies once SQLite is validated.

### 4.1 Cleanup

#### Task 4.1.1: Remove ClickHouse write paths
**Files to modify:**
- `api/ClickHouse/PlayerRoundsWriteService.cs` - remove ClickHouse writes
- `api/ClickHouse/PlayerMetricsWriteService.cs` - deprecate entirely
- `api/StatsCollectors/*` - remove ClickHouse publishing

---

#### Task 4.1.2: Remove ClickHouse read services
**Files to delete:**
- `api/ClickHouse/PlayerRoundsReadService.cs`
- `api/ClickHouse/PlayerComparisonService.cs`
- `api/ClickHouse/GameTrendsService.cs`
- `api/ClickHouse/ServerStatisticsService.cs`
- `api/ClickHouse/PlayerInsightsService.cs`
- `api/ClickHouse/PlayersOnlineHistoryService.cs`

---

#### Task 4.1.3: Remove ClickHouse infrastructure
**Files to modify:**
- `docker-compose.dev.yml` - remove ClickHouse service
- `api/api.csproj` - remove ClickHouse NuGet packages
- `api/Program.cs` - remove ClickHouse DI registration

---

#### Task 4.1.4: Remove migration/backfill services
**Files to delete:**
- `api/ClickHouse/BackfillServices/*`
- `api/ClickHouse/*MigrationService.cs`

---

#### Task 4.1.5: Update deployment configuration
**Files to modify:**
- Kubernetes manifests - remove ClickHouse deployment
- Update connection strings
- Remove ClickHouse secrets

---

## Implementation Order

Recommended sequence based on traffic impact and dependencies:

### Sprint 1: Foundation
1. Task 1.1.1-1.1.4 (entities and migration)
2. Task 1.3.1 (telemetry)
3. Task 1.2.1 (server_online_counts backfill)

### Sprint 2: Server Analytics
1. Task 2.2.1 (dual-write server_online_counts)
2. Task 1.2.5-1.2.6 (patterns backfill)
3. Task 3.1.1-3.1.3 (landing page migrations)

### Sprint 3: Player Stats Foundation
1. Task 1.2.2-1.2.4 (player stats backfill)
2. Task 2.1.1-2.1.3 (aggregate update queue)
3. Task 3.2.1-3.2.4 (leaderboard migrations)

### Sprint 4: Player Features
1. Task 2.3.1-2.3.3 (background refresh jobs)
2. Task 3.3.1-3.3.6 (player stats migrations)
3. Task 3.4.1-3.4.4 (player comparison migrations)

### Sprint 5: Final Migration
1. Task 3.5.1-3.5.2 (activity patterns)
2. Task 3.6.1 (feature flags)
3. Validation and testing

### Sprint 6: Cleanup
1. Task 4.1.1-4.1.5 (decommission)

---

## Validation Checklist

Before decommissioning ClickHouse:

### Data Integrity
- [ ] Row counts match between ClickHouse and SQLite for all tables
- [ ] Spot-check specific player stats match
- [ ] Leaderboard rankings match

### Performance
- [ ] SQLite query times within 2x of ClickHouse
- [ ] No timeouts under normal load
- [ ] AKS memory usage acceptable

### Functionality
- [ ] All endpoints return correct data
- [ ] Player comparison works correctly
- [ ] Leaderboards update on round completion
- [ ] Busy indicators show reasonable values

### Monitoring
- [ ] Telemetry capturing all queries
- [ ] Alerting configured for slow queries
- [ ] Dashboard shows SQLite metrics

---

## Rollback Plan

If issues arise after migration:

1. **Per-endpoint rollback:** Use feature flags to switch individual endpoints back to ClickHouse
2. **Full rollback:** Disable all SQLite read paths, re-enable ClickHouse writes
3. **Data recovery:** ClickHouse data preserved until final decommission

---

## Risk Mitigation

| Risk | Mitigation |
|------|------------|
| SQLite lock contention | Queue aggregate updates, process in background |
| Query performance degradation | Pre-compute everything possible, heavy caching |
| Data inconsistency during migration | Dual-write period with validation queries |
| AKS resource constraints | Monitor closely, scale if needed |
| Missing edge cases | Comprehensive testing with production data samples |
