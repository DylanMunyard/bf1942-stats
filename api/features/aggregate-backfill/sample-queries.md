# Aggregate Backfill - Sample Queries

These are the SELECT portions of the backfill queries. Run these in production to test performance before executing the full backfill.

## Query Strategy

The backfill uses a **two-phase batched approach** for better performance:

1. **Phase 1**: Get distinct player names for a tier (filtered by LastSeenTime + AiBot)
2. **Phase 2**: Process players in batches of 100, using `PlayerName IN (...)` filters

This approach is faster because:
- `PlayerName IN (...)` uses the PlayerName index effectively (index seek)
- Avoids repeated JOINs to the Players table in aggregation queries
- Smaller, more predictable query execution times
- Better progress visibility and resumability

### Tier Definitions (for Phase 1 only)
- **Tier 1**: `ps.LastSeenTime >= '2025-01-04 00:00:00'` (last 7 days)
- **Tier 2**: `ps.LastSeenTime >= '2024-12-12 00:00:00' AND ps.LastSeenTime < '2025-01-04 00:00:00'` (8-30 days)
- **Tier 3**: `ps.LastSeenTime >= '2024-10-13 00:00:00' AND ps.LastSeenTime < '2024-12-12 00:00:00'` (31-90 days)
- **Tier 4**: `ps.LastSeenTime < '2024-10-13 00:00:00'` (older than 90 days)

---

## Phase 1: Get Player Names for Tier

First, fetch distinct player names for the tier. This query uses the tier WHERE clause.

```sql
-- SQLite: Get player names for a tier
SELECT DISTINCT ps.PlayerName
FROM PlayerSessions ps
INNER JOIN Players p ON ps.PlayerName = p.Name
WHERE ps.LastSeenTime >= '2025-01-04 00:00:00'  -- Tier 1 example
  AND p.AiBot = 0
ORDER BY ps.PlayerName;
```

---

## Phase 2: Batched Aggregation Queries

The following queries filter directly on `PlayerName IN (...)` for index efficiency.
Example uses `'skandia'` as the player name - in practice, this would be a batch of ~100 players.

---

## 1. PlayerStatsLifetime

Aggregates all sessions per player into lifetime stats.

```sql
-- SQLite (batched - filter by player names)
SELECT
    ps.PlayerName,
    COUNT(*) as TotalRounds,
    SUM(ps.TotalKills) as TotalKills,
    SUM(ps.TotalDeaths) as TotalDeaths,
    SUM(ps.TotalScore) as TotalScore,
    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes,
    CAST(SUM(ps.TotalScore) AS REAL) / COUNT(*) as AvgScorePerRound,
    CASE
        WHEN SUM(ps.TotalDeaths) > 0 THEN ROUND(CAST(SUM(ps.TotalKills) AS REAL) / SUM(ps.TotalDeaths), 3)
        ELSE SUM(ps.TotalKills)
    END as KdRatio,
    CASE
        WHEN SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) > 0
        THEN ROUND(SUM(ps.TotalKills) / SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60), 3)
        ELSE 0
    END as KillRate,
    MIN(ps.StartTime) as FirstRoundTime,
    MAX(ps.LastSeenTime) as LastRoundTime
FROM PlayerSessions ps
WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
GROUP BY ps.PlayerName;
```

---

## 2. PlayerServerStats

Aggregates sessions per player per server, including highest score tracking.

```sql
-- SQLite (batched - filter by player names)
WITH Aggregates AS (
    SELECT
        ps.PlayerName,
        ps.ServerGuid,
        COUNT(*) as TotalRounds,
        SUM(ps.TotalKills) as TotalKills,
        SUM(ps.TotalDeaths) as TotalDeaths,
        SUM(ps.TotalScore) as TotalScore,
        SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes
    FROM PlayerSessions ps
    WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
    GROUP BY ps.PlayerName, ps.ServerGuid
),
HighestScores AS (
    SELECT
        ps.PlayerName,
        ps.ServerGuid,
        ps.TotalScore as HighestScore,
        ps.RoundId as HighestScoreRoundId,
        ps.MapName as HighestScoreMapName,
        ps.LastSeenTime as HighestScoreTime,
        ROW_NUMBER() OVER (PARTITION BY ps.PlayerName, ps.ServerGuid ORDER BY ps.TotalScore DESC) as rn
    FROM PlayerSessions ps
    WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
)
SELECT
    a.PlayerName,
    a.ServerGuid,
    a.TotalRounds,
    a.TotalKills,
    a.TotalDeaths,
    a.TotalScore,
    a.TotalPlayTimeMinutes,
    h.HighestScore,
    h.HighestScoreRoundId,
    h.HighestScoreMapName,
    h.HighestScoreTime
FROM Aggregates a
LEFT JOIN HighestScores h ON a.PlayerName = h.PlayerName AND a.ServerGuid = h.ServerGuid AND h.rn = 1;
```

---

## 3. PlayerMapStats (Server-Specific)

Aggregates sessions per player per map per server.

```sql
-- SQLite (batched - filter by player names)
SELECT
    ps.PlayerName,
    ps.MapName,
    ps.ServerGuid,
    COUNT(*) as TotalRounds,
    SUM(ps.TotalKills) as TotalKills,
    SUM(ps.TotalDeaths) as TotalDeaths,
    SUM(ps.TotalScore) as TotalScore,
    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes
FROM PlayerSessions ps
WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
GROUP BY ps.PlayerName, ps.MapName, ps.ServerGuid;
```

---

## 4. PlayerMapStats (Global - Cross-Server)

Aggregates sessions per player per map across all servers.

```sql
-- SQLite (batched - filter by player names)
SELECT
    ps.PlayerName,
    ps.MapName,
    '' as ServerGuid,
    COUNT(*) as TotalRounds,
    SUM(ps.TotalKills) as TotalKills,
    SUM(ps.TotalDeaths) as TotalDeaths,
    SUM(ps.TotalScore) as TotalScore,
    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as TotalPlayTimeMinutes
FROM PlayerSessions ps
WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
GROUP BY ps.PlayerName, ps.MapName;
```

---

## 5. PlayerDailyStats

Aggregates sessions per player per day.

```sql
-- SQLite (batched - filter by player names)
SELECT
    ps.PlayerName,
    date(ps.LastSeenTime) as Date,
    SUM(ps.TotalKills) as DailyKills,
    SUM(ps.TotalDeaths) as DailyDeaths,
    SUM(ps.TotalScore) as DailyScore,
    SUM((julianday(ps.LastSeenTime) - julianday(ps.StartTime)) * 24 * 60) as DailyPlayTimeMinutes,
    COUNT(*) as DailyRounds
FROM PlayerSessions ps
WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
GROUP BY ps.PlayerName, date(ps.LastSeenTime);
```

---

## 6. PlayerBestScores (Top 3 per player per period)

Gets the top 3 highest-scoring rounds per player.

```sql
-- SQLite (batched - filter by player names, all_time period)
SELECT
    PlayerName, 'all_time' as Period, Rank, FinalScore, FinalKills, FinalDeaths,
    MapName, ServerGuid, RoundEndTime, RoundId
FROM (
    SELECT
        ps.PlayerName,
        ROW_NUMBER() OVER (PARTITION BY ps.PlayerName ORDER BY ps.TotalScore DESC) as Rank,
        ps.TotalScore as FinalScore,
        ps.TotalKills as FinalKills,
        ps.TotalDeaths as FinalDeaths,
        ps.MapName,
        ps.ServerGuid,
        ps.LastSeenTime as RoundEndTime,
        COALESCE(ps.RoundId, '') as RoundId
    FROM PlayerSessions ps
    WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
      AND ps.TotalScore > 0
) ranked
WHERE Rank <= 3;
```

```sql
-- SQLite (batched - filter by player names, last_30_days period)
SELECT
    PlayerName, 'last_30_days' as Period, Rank, FinalScore, FinalKills, FinalDeaths,
    MapName, ServerGuid, RoundEndTime, RoundId
FROM (
    SELECT
        ps.PlayerName,
        ROW_NUMBER() OVER (PARTITION BY ps.PlayerName ORDER BY ps.TotalScore DESC) as Rank,
        ps.TotalScore as FinalScore,
        ps.TotalKills as FinalKills,
        ps.TotalDeaths as FinalDeaths,
        ps.MapName,
        ps.ServerGuid,
        ps.LastSeenTime as RoundEndTime,
        COALESCE(ps.RoundId, '') as RoundId
    FROM PlayerSessions ps
    WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
      AND ps.TotalScore > 0
      AND ps.LastSeenTime >= '2024-12-12 00:00:00'  -- 30 days ago
) ranked
WHERE Rank <= 3;
```

---

## 7. Milestone Candidates

Identifies players who have enough kills to potentially have milestones (5000+).

```sql
-- SQLite (batched - filter by player names)
WITH PlayerTotalKills AS (
    SELECT
        ps.PlayerName,
        SUM(ps.TotalKills) as TotalKills,
        MIN(ps.StartTime) as FirstRoundTime
    FROM PlayerSessions ps
    WHERE ps.PlayerName IN ('skandia')  -- Batch of player names
    GROUP BY ps.PlayerName
    HAVING SUM(ps.TotalKills) >= 5000
),
ExistingMilestones AS (
    SELECT PlayerName, GROUP_CONCAT(Milestone) as Achieved
    FROM PlayerMilestones
    GROUP BY PlayerName
)
SELECT ptk.PlayerName, ptk.TotalKills, ptk.FirstRoundTime
FROM PlayerTotalKills ptk
LEFT JOIN ExistingMilestones em ON ptk.PlayerName = em.PlayerName
WHERE em.Achieved IS NULL
   OR NOT (em.Achieved LIKE '%5000%' AND em.Achieved LIKE '%10000%'
       AND em.Achieved LIKE '%20000%' AND em.Achieved LIKE '%50000%'
       AND em.Achieved LIKE '%75000%' AND em.Achieved LIKE '%100000%');
```

---

## Quick Count Queries

Use these to estimate the size of each backfill operation:

```sql
-- SQLite: Count distinct players in tier
SELECT COUNT(DISTINCT ps.PlayerName) as PlayerCount
FROM PlayerSessions ps
INNER JOIN Players p ON ps.PlayerName = p.Name
WHERE ps.LastSeenTime >= '2025-01-04 00:00:00'
  AND p.AiBot = 0;

-- SQLite: Count total sessions in tier
SELECT COUNT(*) as SessionCount
FROM PlayerSessions ps
INNER JOIN Players p ON ps.PlayerName = p.Name
WHERE ps.LastSeenTime >= '2025-01-04 00:00:00'
  AND p.AiBot = 0;

-- SQLite: Count player+server combinations (for PlayerServerStats)
SELECT COUNT(*) as CombinationCount
FROM (
    SELECT DISTINCT ps.PlayerName, ps.ServerGuid
    FROM PlayerSessions ps
    INNER JOIN Players p ON ps.PlayerName = p.Name
    WHERE ps.LastSeenTime >= '2025-01-04 00:00:00'
      AND p.AiBot = 0
);
```

---

## Performance Testing Tips

1. Run the count queries first to understand data volume
2. Add `LIMIT 100` to SELECT queries to test without full execution
3. Use `EXPLAIN QUERY PLAN` (SQLite) to analyze query plans
4. Time each query: `time sqlite3 database.db "SELECT ..."`
5. Consider adding indexes if queries are slow:
   ```sql
   CREATE INDEX IF NOT EXISTS idx_sessions_lastseen ON PlayerSessions(LastSeenTime);
   CREATE INDEX IF NOT EXISTS idx_sessions_player_server ON PlayerSessions(PlayerName, ServerGuid);
   ```
