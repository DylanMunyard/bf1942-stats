---
description: "ClickHouse Migration: Stream A - Create EF Core entities and migration (MUST RUN FIRST)"
allowed-tools: Read, Write, Edit, Glob, Grep, Bash, mcp__serena__find_symbol, mcp__serena__get_symbols_overview, mcp__serena__list_dir, mcp__serena__search_for_pattern, mcp__serena__replace_symbol_body, mcp__serena__insert_after_symbol, mcp__serena__insert_before_symbol
---

# ClickHouse Migration - Stream A: Foundation

You are implementing **Stream A** of the ClickHouse to SQLite migration. This stream MUST complete before other streams can begin.

## Reference Documents

Read these files first:
- @bf1942-stats/features/clickhouse-migration/migration-plan.md (see "Stream A: Foundation" section)
- @bf1942-stats/features/clickhouse-migration/analysis.md (see "Pre-Computed Data Structures" section for schemas)

## Your Scope

**Directory ownership:** `api/Data/Entities/`, `api/Data/PlayerTrackerContext.cs`, `api/Migrations/`, `api/Telemetry/`

**DO NOT modify files outside your scope.** Other agents will handle those.

## Tasks

### 1. Create EF Core Entities (Task 1.1.1-1.1.3)

Create these entity classes in `api/Data/Entities/`:

**Player Aggregates:**
- `PlayerStatsLifetime.cs` - lifetime stats per player
- `PlayerServerStats.cs` - stats per player per server (includes highest score tracking)
- `PlayerMapStats.cs` - stats per player per map
- `PlayerDailyStats.cs` - daily aggregates for trend charts
- `PlayerMilestone.cs` - kill milestone achievements
- `PlayerBestScore.cs` - top 3 scores per period

**Server Analytics:**
- `ServerOnlineCount.cs` - hourly player counts per server
- `ServerHourlyPattern.cs` - percentile data for busy indicators
- `HourlyPlayerPrediction.cs` - 168 rows per game (7 days x 24 hours)
- `HourlyActivityPattern.cs` - weekly activity patterns

**Leaderboards:**
- `ServerLeaderboardEntry.cs` - pre-computed leaderboard rankings
- `MapGlobalAverage.cs` - global map averages for comparison

**Requirements:**
- Use NodaTime `Instant` for all timestamps
- Use `required` keyword for required string properties
- Use record types where appropriate
- Follow existing entity patterns in the codebase

### 2. Update DbContext (Task 1.1.4)

Modify `PlayerTrackerContext.cs`:
- Add DbSet<T> for each new entity
- Configure composite primary keys in OnModelCreating
- Add appropriate indexes (see analysis.md for query patterns)
- Configure NodaTime Instant conversions using InstantPattern.ExtendedIso

### 3. Generate Migration (Task 1.1.4)

After entities are created:
```bash
cd bf1942-stats/api
dotnet ef migrations add AddClickHouseMigrationTables
```

Verify the migration looks correct before applying.

### 4. Add Telemetry ActivitySource (Task 1.3.1)

Modify `api/Telemetry/ActivitySources.cs`:
```csharp
public static readonly ActivitySource SqliteAnalytics = new("BfStats.SqliteAnalytics");
```

## Completion Criteria

- [ ] All 12 entity classes created with correct schema
- [ ] DbContext updated with DbSets and configurations
- [ ] Migration generated successfully
- [ ] `dotnet build` passes
- [ ] ActivitySource added for SQLite analytics

## Handoff

When complete, report:
1. List of entities created
2. Migration name
3. Any issues or design decisions made

Other agents (Stream B, C, D) are waiting for this to complete before they can begin.
