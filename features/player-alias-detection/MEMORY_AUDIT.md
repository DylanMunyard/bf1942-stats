# Memory Audit - Player Alias Detection

## Issue Summary

**CRITICAL ISSUES FOUND** - Two analyzers are loading large datasets into memory:

### 1. BehavioralPatternAnalyzer.cs (Lines 33-34)
```csharp
var sessions1 = await dbContext.PlayerSessions
    .Where(s => s.PlayerName == player1 && s.StartTime >= cutoff)
    .OrderBy(s => s.StartTime)
    .ToListAsync();  // ❌ LOADS ALL SESSIONS INTO MEMORY
```

**Problem**: Loads ALL sessions for a player into memory, then iterates in-memory:
- Line 85-89: Iterates to build hour-of-day histogram
- Line 119: Creates HashSet from all server guids
- Line 206-214: Iterates to calculate session patterns

**Impact**:
- Player with 1,000 sessions = ~1MB in memory (manageable)
- Player with 10,000 sessions = ~10MB (concerning)
- Player with 100,000+ sessions = **memory spike**

### 2. ActivityTimelineAnalyzer.cs (Lines 30-39)
```csharp
var sessions1 = await dbContext.PlayerSessions
    .Where(s => s.PlayerName == player1 && s.StartTime >= cutoff && !s.IsDeleted)
    .OrderBy(s => s.StartTime)
    .ToListAsync();  // ❌ LOADS ALL SESSIONS INTO MEMORY
```

**Problem**: Same issue, plus builds 180-day daily activity array:
- Line 112: Builds daily timelines from all sessions
- Creates dictionary with ~180 entries but iterates potentially thousands of times

**Impact**: Same memory concerns as above

---

## What's Working Correctly ✅

### StatSimilarityCalculator.cs
Uses pre-aggregated `PlayerStatsMonthly` table - **correct**

### Neo4jNetworkAnalyzer.cs
All queries happen on Neo4j server - **correct**

### BehavioralPatternAnalyzer - Ping Consistency (Lines 148-177)
```csharp
var pings1 = await dbContext.PlayerObservations
    .Where(o => o.Session.PlayerName == player1 && ...)
    .GroupBy(o => o.Session.ServerGuid)
    .Select(g => new { ServerGuid = g.Key, AvgPing = g.Average(o => o.Ping) })
    .ToListAsync();  // ✅ AGGREGATED IN DATABASE
```
Only returns aggregated data (1 row per server) - **correct**

---

## Refactoring Required

### BehavioralPatternAnalyzer - Use Raw SQL

Replace in-memory histogram with database query:

```csharp
// BEFORE: Load all sessions into memory
var sessions1 = await dbContext.PlayerSessions
    .Where(s => s.PlayerName == player1 && s.StartTime >= cutoff)
    .OrderBy(s => s.StartTime)
    .ToListAsync();

// AFTER: Aggregate in database
var playTimeByHour = await dbContext.Database.SqlQueryRaw<(int hour, int count)>("""
    SELECT CAST(strftime('%H', StartTime) AS INTEGER) as hour, COUNT(*) as count
    FROM PlayerSessions
    WHERE PlayerName = @playerName
      AND StartTime >= @cutoff
    GROUP BY hour
    """,
    new SqliteParameter("@playerName", player1),
    new SqliteParameter("@cutoff", cutoff)
).ToListAsync();
```

This returns only 24 rows (one per hour) instead of potentially thousands of session records.

### BehavioralPatternAnalyzer - Server Affinity with SQL

```csharp
// BEFORE: Load all sessions, create HashSet, compute intersection
var servers1 = sessions1.Select(s => s.ServerGuid).ToHashSet();
var servers2 = sessions2.Select(s => s.ServerGuid).ToHashSet();
var intersection = servers1.Intersect(servers2).Count();

// AFTER: Compute directly in database
var commonServers = await dbContext.Database.SqlQueryRaw<int>("""
    SELECT COUNT(DISTINCT s1.ServerGuid)
    FROM (
        SELECT DISTINCT ServerGuid FROM PlayerSessions
        WHERE PlayerName = @player1 AND StartTime >= @cutoff
    ) s1
    JOIN (
        SELECT DISTINCT ServerGuid FROM PlayerSessions
        WHERE PlayerName = @player2 AND StartTime >= @cutoff
    ) s2 ON s1.ServerGuid = s2.ServerGuid
    """,
    new SqliteParameter("@player1", player1),
    new SqliteParameter("@player2", player2),
    new SqliteParameter("@cutoff", cutoff)
).SingleAsync();
```

### BehavioralPatternAnalyzer - Session Patterns with SQL

```csharp
// BEFORE: Load all sessions, calculate duration stats in-memory
var avgDuration1 = sessions1.Average(s => (s.LastSeenTime - s.StartTime).TotalMinutes);

// AFTER: Calculate in database
var sessionStats = await dbContext.Database.SqlQueryRaw<(int sessionCount, long totalMinutes)>("""
    SELECT
        COUNT(*) as sessionCount,
        SUM(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS INTEGER)) as totalMinutes
    FROM PlayerSessions
    WHERE PlayerName = @playerName AND StartTime >= @cutoff
    """,
    new SqliteParameter("@playerName", player1),
    new SqliteParameter("@cutoff", cutoff)
).SingleAsync();
```

### ActivityTimelineAnalyzer - Daily Activity with SQL

```csharp
// BEFORE: Load all sessions, iterate to build 180-day timeline
var timeline = new Dictionary<DateTime, DailyActivity>();
foreach (var session in sessions)
{
    var date = session.StartTime.Date;
    timeline[date].SessionCount++;
    // ... more in-memory processing
}

// AFTER: Aggregate in database
var dailyActivity = await dbContext.Database.SqlQueryRaw<(DateTime date, int sessions, long minutes, double kd)>("""
    SELECT
        DATE(StartTime) as date,
        COUNT(*) as sessions,
        SUM(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS INTEGER)) as minutes,
        CASE WHEN SUM(TotalDeaths) > 0
             THEN CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths)
             ELSE SUM(TotalKills)
        END as kd
    FROM PlayerSessions
    WHERE PlayerName = @playerName AND StartTime >= @cutoff AND IsDeleted = 0
    GROUP BY DATE(StartTime)
    ORDER BY date DESC
    """,
    new SqliteParameter("@playerName", player1),
    new SqliteParameter("@cutoff", cutoff)
).ToListAsync();
```

Returns only ~180 rows max instead of potentially thousands.

---

## Memory Reduction Summary

| Scenario | Before | After |
|----------|--------|-------|
| 1,000 sessions | 1MB in memory | < 100KB returned |
| 10,000 sessions | 10MB in memory | < 100KB returned |
| 100,000 sessions | 100MB+ spike | < 100KB returned |
| Player with 5 years data | Huge lists | Only aggregates needed |

---

## Refactoring Checklist

- [ ] BehavioralPatternAnalyzer - Replace CalculatePlayTimeOverlap with SQL
- [ ] BehavioralPatternAnalyzer - Replace CalculateServerAffinity with SQL
- [ ] BehavioralPatternAnalyzer - Replace CalculateSessionPatternSimilarity with SQL
- [ ] ActivityTimelineAnalyzer - Replace BuildDailyTimeline with SQL
- [ ] ActivityTimelineAnalyzer - Replace CalculateActivityPeriod with SQL
- [ ] Test with large player datasets (10k+ sessions)
- [ ] Profile memory usage before/after

---

## SQLite Raw SQL Notes

For SQLite LINQ to work with raw queries:
```csharp
using var command = dbContext.Database.GetDbConnection().CreateCommand();
command.CommandText = "SELECT ...";
using var reader = await command.ExecuteReaderAsync();
```

OR use EF Core's `.FromSqlInterpolated()` for LINQ integration:
```csharp
var results = await dbContext.PlayerSessions
    .FromSqlInterpolated($"""
        SELECT ... WHERE PlayerName = {playerName}
        """)
    .ToListAsync();
```

---

## Current Status
🚨 **REQUIRES REFACTORING** before production use with large player datasets
