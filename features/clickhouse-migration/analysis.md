# ClickHouse Migration Analysis

## Overview

This document analyzes all ClickHouse read queries to understand what data transformations are required and plan migration strategies to alternative storage solutions (SQLite aggregates, blob storage, or hybrid approaches).

**Goal:** Retire ClickHouse to reduce production costs while maintaining equivalent functionality.

**Excluded from scope:** Team killer detection endpoint (confirmed unused).

---

## Current ClickHouse Tables

### 1. `player_rounds` (Primary Analytics Table)

**Purpose:** Pre-aggregated round-level statistics - the workhorse table for most queries.

**Schema:**
```sql
round_id String              -- SHA256 hash (primary key)
player_name String
server_guid String
map_name String
round_start_time DateTime
round_end_time DateTime
final_score Int32
final_kills UInt32
final_deaths UInt32
play_time_minutes Float64
team_label String
game_id String
is_bot UInt8
created_at DateTime
game String
average_ping Nullable(Float64)
```

**Current data flow:** SQLite `PlayerSessions` (completed) → Background sync → ClickHouse `player_rounds`

**Query frequency:** HIGH - used by most read services

---

### 2. `server_online_counts`

**Purpose:** Server population tracking for trends and forecasting.

**Schema:**
```sql
timestamp DateTime
server_guid String
server_name String
players_online UInt16
map_name String
game String
```

**Current data flow:** BFList API (30s polling) → Background sync → ClickHouse

**Query frequency:** MEDIUM - used for busy indicators and predictions

---

### 3. `player_metrics`

**Purpose:** Raw 30-second player snapshots. Mostly superseded by `player_rounds` but still used for ping data.

**Schema:**
```sql
timestamp DateTime
server_guid String
server_name String
player_name String
score Int32
kills UInt16
deaths UInt16
ping UInt16
team_name String
map_name String
game_type String
is_bot UInt8
game String
```

**Query frequency:** LOW - only used for ping averages in player comparison

---

### 4. `player_achievements_deduplicated`

**Purpose:** Gamification achievements, badges, milestones.

**Schema:**
```sql
player_name String
achievement_type String
achievement_id String
achievement_name String
tier String
value UInt32
achieved_at DateTime
processed_at DateTime
server_guid String
map_name String
round_id String
metadata String
version DateTime
game String
```

**Query frequency:** LOW - milestone achievements for player comparison

---

## Query Catalog

### Category 1: Simple Player Aggregations

These queries aggregate player statistics with straightforward GROUP BY operations.

#### 1.1 GetPlayerStatsAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:60-96`

**Query Pattern:**
```sql
SELECT
    player_name,
    COUNT(*) as total_rounds,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    SUM(play_time_minutes) as total_play_time_minutes,
    AVG(final_score) as avg_score_per_round,
    CASE WHEN SUM(final_deaths) > 0
         THEN round(SUM(final_kills) / SUM(final_deaths), 3)
         ELSE toFloat64(SUM(final_kills)) END as kd_ratio
FROM player_rounds
WHERE is_bot = 0
  AND player_name = ?
  AND round_start_time >= ?
  AND round_start_time <= ?
GROUP BY player_name
HAVING total_kills > 10
ORDER BY total_play_time_minutes DESC
```

**Filters:** player_name (optional), date range (optional), excludes bots
**Output:** Player aggregate stats
**Cardinality:** Returns 1 row per player matching filters

**Pre-computation strategy:**
- Maintain `player_stats_lifetime` table with all-time aggregates
- Optionally maintain `player_stats_monthly` for time-filtered queries
- Update on each round completion

---

#### 1.2 GetMostActivePlayersAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:101-138`

**Query Pattern:**
```sql
SELECT
    player_name,
    CAST(SUM(play_time_minutes) AS INTEGER) as minutes_played,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths
FROM player_rounds
WHERE server_guid = ?
  AND round_start_time >= ?
  AND round_end_time <= ?
  AND is_bot = 0
GROUP BY player_name
ORDER BY minutes_played DESC
LIMIT ?
```

**Filters:** server_guid, date range, excludes bots
**Output:** Top N players by playtime for a specific server
**Cardinality:** Returns up to LIMIT rows

**Pre-computation strategy:**
- Maintain `player_server_stats` table: (player_name, server_guid) → aggregates
- For time-filtered queries, either:
  - Accept staleness with periodic refresh
  - Compute on-demand from SQLite player_sessions

---

#### 1.3 GetTopScoresAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:143-187`

**Query Pattern:**
```sql
SELECT
    player_name,
    SUM(final_score) as total_score,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    COUNT() as total_rounds
FROM player_rounds
WHERE server_guid = ?
  AND round_start_time >= ?
  AND round_end_time <= ?
  AND is_bot = 0
GROUP BY player_name
HAVING COUNT() >= ?  -- minimum rounds threshold
ORDER BY total_score DESC
LIMIT ?
```

**Filters:** server_guid, date range, minimum rounds
**Output:** Leaderboard by total score
**Cardinality:** Returns up to LIMIT rows

**Pre-computation strategy:**
- Maintain `server_leaderboards` table with pre-computed rankings
- Partition by time period (weekly, monthly, all-time)
- Refresh periodically or on-demand

---

#### 1.4 GetTopKDRatiosAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:192-235`

**Query Pattern:**
```sql
SELECT
    player_name,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    CASE WHEN SUM(final_deaths) > 0
         THEN round(SUM(final_kills) / SUM(final_deaths), 3)
         ELSE toFloat64(SUM(final_kills)) END as overall_kd_ratio,
    COUNT() as total_rounds
FROM player_rounds
WHERE server_guid = ?
  AND round_start_time >= ?
  AND round_end_time <= ?
  AND is_bot = 0
  AND (final_kills > 0 OR final_deaths > 0)
GROUP BY player_name
HAVING COUNT() >= ?
ORDER BY overall_kd_ratio DESC
LIMIT ?
```

**Filters:** server_guid, date range, minimum rounds, excludes zero activity
**Output:** Leaderboard by K/D ratio
**Pre-computation strategy:** Same as GetTopScoresAsync - include K/D in leaderboard table

---

#### 1.5 GetTopKillRatesAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:240-287`

**Query Pattern:**
```sql
SELECT
    player_name,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    SUM(play_time_minutes) as total_play_time_minutes,
    CASE WHEN SUM(play_time_minutes) > 0
         THEN round(SUM(final_kills) / SUM(play_time_minutes), 3)
         ELSE 0.0 END as overall_kill_rate,
    COUNT() as total_rounds
FROM player_rounds
WHERE server_guid = ?
  AND round_start_time >= ?
  AND round_end_time <= ?
  AND is_bot = 0
  AND final_kills > 0
  AND play_time_minutes > 0
GROUP BY player_name
HAVING SUM(final_kills) > 0 AND SUM(play_time_minutes) > 0
  AND COUNT() >= ?
ORDER BY overall_kill_rate DESC
LIMIT ?
```

**Filters:** server_guid, date range, minimum rounds, positive kills/playtime
**Output:** Leaderboard by kills per minute
**Pre-computation strategy:** Same as above

---

#### 1.6 GetServerStats (ServerStatisticsService)
**Service:** `ServerStatisticsService`
**File:** `bf1942-stats/api/ClickHouse/ServerStatisticsService.cs:44-101`

**Query Pattern:**
```sql
SELECT
    map_name,
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    COUNT(*) AS sessions_played,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name = ?
  AND server_guid = ?  -- optional
  AND round_start_time >= ?  -- time period filter
GROUP BY map_name
ORDER BY total_kills DESC
```

**Filters:** player_name (required), server_guid (optional), time period (ThisYear/LastYear/Last30Days)
**Output:** Player's stats broken down by map
**Cardinality:** One row per map played

**Pre-computation strategy:**
- Maintain `player_map_stats` table: (player_name, map_name, server_guid) → aggregates
- Optionally partition by time period

---

#### 1.7 GetPlayerServerInsightsAsync
**Service:** `PlayerInsightsService`
**File:** `bf1942-stats/api/ClickHouse/PlayerInsightsService.cs:112-162`

**Query Pattern:**
```sql
SELECT
    server_guid,
    SUM(play_time_minutes) as total_minutes,
    SUM(final_kills) as total_kills,
    SUM(final_deaths) as total_deaths,
    MAX(final_score) as highest_score,
    argMax(round_id, final_score) as highest_score_round_id,
    argMax(map_name, final_score) as highest_score_map_name,
    argMax(round_start_time, final_score) as highest_score_start_time,
    COUNT(*) as total_rounds
FROM player_rounds
WHERE player_name = ?
GROUP BY server_guid
HAVING total_minutes >= 600  -- 10+ hours
ORDER BY total_minutes DESC
```

**Filters:** player_name, minimum 10 hours playtime
**Output:** Player's stats per server with highest score details
**Cardinality:** One row per server with 10+ hours

**Pre-computation strategy:**
- Maintain `player_server_stats` with aggregates
- Track highest score separately (update only if new score > current max)

---

### Category 2: Time-Bucketed Comparisons

These queries compare stats across multiple time periods.

#### 2.1 GetBucketTotals (PlayerComparisonService)
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:168-213`

**Query Pattern (executed 4 times, once per bucket):**
```sql
SELECT player_name,
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name IN (?, ?)
  AND {time_condition}  -- varies per bucket
  AND server_guid = ?   -- optional
GROUP BY player_name
```

**Time buckets:**
- Last30Days: `round_start_time >= now() - INTERVAL 30 DAY`
- Last6Months: `round_start_time >= now() - INTERVAL 6 MONTH`
- LastYear: `round_start_time >= now() - INTERVAL 1 YEAR`
- AllTime: `1=1`

**Filters:** 2 player names, optional server_guid
**Output:** Stats for each player in each time bucket

**Pre-computation strategy:**
- Maintain rolling aggregates for each time window
- Update daily via background job
- Store in `player_stats_rolling` table with columns for each bucket

---

#### 2.2 GetAveragePing
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:215-243`

**Query Pattern:**
```sql
SELECT player_name, avg(ping) as avg_ping
FROM player_metrics
WHERE player_name IN (?, ?)
  AND ping > 0
  AND ping < 1000
  AND timestamp >= now() - INTERVAL 7 DAY
  AND server_guid = ?  -- optional
GROUP BY player_name
```

**Note:** This is the only comparison query using `player_metrics` instead of `player_rounds`

**Pre-computation strategy:**
- Store rolling 7-day average ping per player per server
- Update during background sync
- Or compute from recent SQLite player observations

---

### Category 3: Time-Series & Trends

These queries analyze patterns over time.

#### 3.1 GetPlayerTimeSeriesTrendAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:294-331`

**Query Pattern:**
```sql
WITH player_rounds_daily AS (
    SELECT
        player_name,
        toDate(round_end_time) as day_date,
        SUM(final_kills) as daily_kills,
        SUM(final_deaths) as daily_deaths,
        SUM(play_time_minutes) as daily_minutes
    FROM player_rounds
    WHERE player_name = ?
      AND round_end_time >= ?
    GROUP BY player_name, toDate(round_end_time)
    HAVING daily_kills > 0 OR daily_deaths > 0
),
cumulative_daily AS (
    SELECT
        day_date,
        daily_kills,
        daily_deaths,
        daily_minutes,
        SUM(daily_kills) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_kills,
        SUM(daily_deaths) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_deaths,
        SUM(daily_minutes) OVER (ORDER BY day_date ROWS UNBOUNDED PRECEDING) as cumulative_minutes
    FROM player_rounds_daily
)
SELECT
    day_date as timestamp,
    CASE WHEN cumulative_deaths > 0
         THEN round(cumulative_kills / cumulative_deaths, 3)
         ELSE toFloat64(cumulative_kills) END as kd_ratio,
    CASE WHEN cumulative_minutes > 0
         THEN round(cumulative_kills / cumulative_minutes, 3)
         ELSE 0.0 END as kill_rate
FROM cumulative_daily
ORDER BY day_date
```

**Purpose:** Show player's K/D and kill rate trend over time
**Output:** Daily data points with cumulative ratios

**Pre-computation strategy:**
- Maintain `player_daily_stats` table with daily aggregates
- Compute cumulative values at query time (simple running sum in application code)
- Or pre-compute cumulative values if query performance is critical

---

#### 3.2 GetWeeklyActivityPatternsAsync
**Service:** `GameTrendsService`
**File:** `bf1942-stats/api/ClickHouse/GameTrendsService.cs:194-223`

**Query Pattern:**
```sql
SELECT
    toDayOfWeek(round_start_time) as day_of_week,
    toHour(round_start_time) as hour_of_day,
    COUNT(DISTINCT player_name) as unique_players,
    COUNT(*) as total_rounds,
    AVG(play_time_minutes) as avg_round_duration,
    CASE
        WHEN toDayOfWeek(round_start_time) IN (6, 7) THEN 'Weekend'
        ELSE 'Weekday'
    END as period_type
FROM player_rounds
WHERE round_start_time >= now() - INTERVAL ? DAY
  AND game = ?  -- optional
GROUP BY day_of_week, hour_of_day, period_type
ORDER BY day_of_week, hour_of_day
```

**Purpose:** Analyze weekly activity patterns (168 hour slots)
**Output:** Activity metrics per hour-of-week

**Pre-computation strategy:**
- Maintain `hourly_activity_patterns` table
- 168 rows (7 days × 24 hours) per game
- Update daily from last 30 days of data

---

#### 3.3 GetSmartPredictionInsightsAsync
**Service:** `GameTrendsService`
**File:** `bf1942-stats/api/ClickHouse/GameTrendsService.cs:229-373`

**Query Pattern:**
```sql
SELECT
    hour_of_day,
    day_of_week,
    AVG(hourly_total) as predicted_players,
    COUNT(*) as data_points
FROM (
    SELECT
        hour_of_day,
        day_of_week,
        date_key,
        SUM(latest_players_per_server) as hourly_total
    FROM (
        SELECT
            toHour(timestamp) as hour_of_day,
            toDayOfWeek(timestamp) as day_of_week,
            toDate(timestamp) as date_key,
            server_guid,
            argMax(players_online, timestamp) as latest_players_per_server
        FROM server_online_counts
        WHERE timestamp >= now() - INTERVAL 60 DAY
          AND game = ?  -- optional
          AND (toHour(timestamp), toDayOfWeek(timestamp)) IN (...)  -- 9 hour/day combos
        GROUP BY toHour(timestamp), toDayOfWeek(timestamp), toDate(timestamp), server_guid
    )
    GROUP BY hour_of_day, day_of_week, date_key
)
GROUP BY hour_of_day, day_of_week
ORDER BY hour_of_day, day_of_week
```

**Purpose:** 8-hour player count forecast based on historical patterns
**Output:** Predicted player counts for current + next 8 hours

**Pre-computation strategy:**
- Maintain `hourly_player_predictions` table
- 168 rows per game with average player counts
- Query just needs to lookup 9 specific hour/day combinations
- Update daily

---

#### 3.4 GetServerBusyIndicatorAsync
**Service:** `GameTrendsService`
**File:** `bf1942-stats/api/ClickHouse/GameTrendsService.cs:376-592`

**Query Pattern (Historical data):**
```sql
SELECT
    server_guid,
    groupArray(hourly_avg) as daily_averages
FROM (
    SELECT
        server_guid,
        toDate(timestamp) as date_key,
        AVG(players_online) as hourly_avg
    FROM server_online_counts
    WHERE timestamp >= now() - INTERVAL 60 DAY
        AND server_guid IN (...)
        AND toHour(timestamp) = ?
        AND toDayOfWeek(timestamp) = ?
    GROUP BY server_guid, date_key
    HAVING hourly_avg > 0
)
GROUP BY server_guid
```

**Query Pattern (Timeline data):**
```sql
SELECT
    server_guid,
    toHour(timestamp) as hour,
    AVG(players_online) as avg_players
FROM server_online_counts
WHERE timestamp >= now() - INTERVAL 30 DAY
    AND server_guid IN (...)
    AND toDayOfWeek(timestamp) = ?
    AND toHour(timestamp) IN (...)  -- ±4 hours from current
GROUP BY server_guid, toHour(timestamp)
ORDER BY server_guid, hour
```

**Purpose:** Google-style "how busy is it" indicator with percentile rankings
**Output:** Current busyness level, historical range, hourly timeline

**Pre-computation strategy:**
- Maintain `server_hourly_stats` with averages per server/hour/day-of-week
- Store percentile data (min, q25, median, q75, q90, max)
- Update daily

---

### Category 4: Point-in-Time Lookups

These queries find specific records or compute milestones.

#### 4.1 GetPlayerBestScoresAsync
**Service:** `PlayerRoundsReadService`
**File:** `bf1942-stats/api/ClickHouse/PlayerRoundsReadService.cs:336-433`

**Query Pattern:**
```sql
SELECT
    'ThisWeek' as period,
    final_score, final_kills, final_deaths,
    map_name, server_guid, round_end_time, round_id
FROM (
    SELECT ...
    FROM player_rounds
    WHERE player_name = ?
      AND final_score > 0
      AND round_end_time >= ?  -- this week start
    ORDER BY final_score DESC
    LIMIT 3
)
UNION ALL
SELECT 'Last30Days' as period, ...
UNION ALL
SELECT 'AllTime' as period, ...
```

**Purpose:** Get player's top 3 scores in 3 time periods
**Output:** Up to 9 best score records

**Pre-computation strategy:**
- Maintain `player_best_scores` table with top N scores per player per period
- Update when a round completes if score qualifies
- Periodically prune old "this week" entries

---

#### 4.2 GetPlayersKillMilestonesAsync
**Service:** `PlayerInsightsService`
**File:** `bf1942-stats/api/ClickHouse/PlayerInsightsService.cs:20-107`

**Query Pattern:**
```sql
WITH PlayerRoundsCumulative AS (
    SELECT
        player_name,
        round_end_time,
        final_kills,
        SUM(final_kills) OVER (PARTITION BY player_name ORDER BY round_end_time) as cumulative_kills,
        row_number() OVER (PARTITION BY player_name ORDER BY round_end_time) as row_num
    FROM player_rounds
    WHERE player_name IN (...)
),
PlayerRoundsWithPrevious AS (
    SELECT
        p1.player_name,
        p1.round_end_time,
        p1.cumulative_kills,
        COALESCE(p2.cumulative_kills, 0) as previous_cumulative_kills
    FROM PlayerRoundsCumulative p1
    LEFT JOIN PlayerRoundsCumulative p2
        ON p1.player_name = p2.player_name AND p1.row_num = p2.row_num + 1
),
MilestoneRounds AS (
    SELECT
        player_name,
        round_end_time,
        cumulative_kills,
        CASE
            WHEN cumulative_kills >= 5000 AND previous_cumulative_kills < 5000 THEN 5000
            WHEN cumulative_kills >= 10000 AND previous_cumulative_kills < 10000 THEN 10000
            -- ... 20k, 50k, 75k, 100k
            ELSE 0
        END as milestone
    FROM PlayerRoundsWithPrevious
)
SELECT
    m.player_name,
    m.milestone,
    m.round_end_time as achieved_date,
    m.cumulative_kills as total_kills_at_milestone,
    dateDiff('day', f.first_round_time, m.round_end_time) as days_to_achieve
FROM MilestoneRounds m
JOIN FirstRounds f ON m.player_name = f.player_name
WHERE m.milestone > 0
```

**Milestones:** 5k, 10k, 20k, 50k, 75k, 100k kills
**Purpose:** Track when players crossed kill milestones

**Pre-computation strategy:**
- Compute milestone achievements during round completion
- Store in `player_milestones` table when threshold crossed
- Never needs to be recomputed

---

### Category 5: Player Comparison Complex Queries

These queries support the player comparison feature.

#### 5.1 GetKillRates
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:142-166`

**Query Pattern:**
```sql
SELECT player_name,
    SUM(final_kills) / nullIf(SUM(play_time_minutes), 0) AS kill_rate
FROM player_rounds
WHERE player_name IN (?, ?)
  AND server_guid = ?  -- optional
GROUP BY player_name
```

**Pre-computation strategy:** Use pre-computed player stats

---

#### 5.2 GetMapPerformance
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:245-278`

**Query Pattern:**
```sql
SELECT map_name, player_name,
    SUM(final_score) AS total_score,
    SUM(final_kills) AS total_kills,
    SUM(final_deaths) AS total_deaths,
    SUM(play_time_minutes) AS total_play_time_minutes
FROM player_rounds
WHERE player_name IN (?, ?)
  AND server_guid = ?  -- optional
GROUP BY map_name, player_name
```

**Pre-computation strategy:** Use pre-computed `player_map_stats` table

---

#### 5.3 GetHeadToHeadData
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:280-345`

**Query Pattern:**
```sql
SELECT p1.round_start_time, p1.round_end_time, p1.server_guid, p1.map_name,
       p1.final_score, p1.final_kills, p1.final_deaths,
       p2.final_score, p2.final_kills, p2.final_deaths,
       p2.round_start_time, p2.round_end_time, p1.round_id
FROM player_rounds p1
JOIN player_rounds p2
    ON p1.server_guid = p2.server_guid
    AND p1.map_name = p2.map_name
    AND p1.round_start_time <= p2.round_end_time
    AND p2.round_start_time <= p1.round_end_time
WHERE p1.player_name = ? AND p2.player_name = ?
  AND p1.server_guid = ?  -- optional
ORDER BY p1.round_start_time DESC
LIMIT 50
```

**Purpose:** Find rounds where both players were on the same server at the same time
**Complexity:** Self-join with time overlap detection

**Pre-computation strategy:**
- This is challenging to pre-compute (N² relationships)
- Options:
  1. Compute on-demand from SQLite `PlayerSessions` table
  2. Accept latency and cache results
  3. Pre-compute for "active" player pairs only

---

#### 5.4 GetCommonServersData
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:348-389`

**Query Pattern:**
```sql
SELECT DISTINCT server_guid
FROM player_rounds
WHERE player_name = ? AND round_start_time >= now() - INTERVAL 6 MONTH
INTERSECT
SELECT DISTINCT server_guid
FROM player_rounds
WHERE player_name = ? AND round_start_time >= now() - INTERVAL 6 MONTH
```

**Purpose:** Find servers where both players have played
**Pre-computation strategy:** Use pre-computed `player_server_list` (list of servers per player)

---

### Category 6: Similarity/Alias Detection (Most Complex)

These are the most complex queries, primarily supporting the "find similar players" and alias detection features.

#### 6.1 FindPlayersBySimilarityWithGuids
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:797-1086`

**Query Pattern (abbreviated - full query is ~150 lines):**
```sql
WITH player_stats AS (
    SELECT player_name, SUM(final_kills), SUM(final_deaths),
           SUM(play_time_minutes), K/D ratio
    FROM player_rounds
    WHERE player_name != ? AND round_start_time >= now() - INTERVAL 6 MONTH
      AND server_guid IN (...)  -- target player's servers
    GROUP BY player_name
    HAVING total_play_time_minutes >= 30
),
server_playtime AS (...),
favorite_servers AS (...),
player_game_ids AS (...),
candidate_active_servers AS (...),
candidate_hour_thresholds AS (...),
candidate_online_hours AS (...),
-- Conditionally included:
candidate_server_pings AS (...),  -- only for AliasDetection mode
candidate_map_dominance AS (...)  -- only for AliasDetection mode
SELECT ...
ORDER BY similarity_score
LIMIT ?
```

**Purpose:** Find players with similar play patterns
**Modes:**
- Default: Match by K/D, playtime, server, online hours
- AliasDetection: Also match by ping patterns, temporal non-overlap

**Pre-computation strategy:**
- Pre-compute per-player feature vectors:
  - K/D ratio, kill rate, playtime
  - Favorite server(s)
  - Typical online hours (list of hours)
  - Server pings (server → avg ping)
  - Map dominance scores (map → performance ratio)
- Store in `player_features` table
- Query becomes simple feature vector comparison

---

#### 6.2 CalculateBulkTemporalOverlap
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:1089-1170`

**Query Pattern:**
```sql
WITH target_sessions AS (
    SELECT round_start_time, round_end_time, server_guid
    FROM player_rounds
    WHERE player_name = ? AND round_start_time >= now() - INTERVAL 3 MONTH
),
candidate_sessions AS (
    SELECT player_name, round_start_time, round_end_time, server_guid
    FROM player_rounds
    WHERE player_name IN (...) AND round_start_time >= now() - INTERVAL 3 MONTH
),
overlapping_sessions AS (
    SELECT
        c.player_name,
        SUM(
            CASE
                WHEN c.server_guid = t.server_guid
                     AND c.round_start_time < t.round_end_time
                     AND c.round_end_time > t.round_start_time
                THEN dateDiff('minute',
                    greatest(c.round_start_time, t.round_start_time),
                    least(c.round_end_time, t.round_end_time)
                )
                ELSE 0
            END
        ) as overlap_minutes
    FROM candidate_sessions c
    CROSS JOIN target_sessions t
    GROUP BY c.player_name
)
SELECT player_name, overlap_minutes FROM overlapping_sessions
```

**Purpose:** Calculate how many minutes two players were online simultaneously
**Use case:** Alias detection (aliases should have minimal overlap)

**Pre-computation strategy:**
- Expensive to pre-compute (O(N²) player pairs)
- Compute on-demand from SQLite `PlayerSessions`
- Cache results for frequently compared pairs

---

#### 6.3 GetPlayerTypicalOnlineHours
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:650-690`

**Query Pattern:**
```sql
WITH hourly_playtime AS (
    SELECT
        toHour(round_start_time) as hour_of_day,
        SUM(play_time_minutes) as total_minutes
    FROM player_rounds
    WHERE player_name = ?
      AND round_start_time >= now() - INTERVAL 6 MONTH
      AND server_guid IN (...)  -- optional
    GROUP BY hour_of_day
),
percentiles AS (
    SELECT quantile(0.95)(total_minutes) as p95_minutes
    FROM hourly_playtime
)
SELECT hour_of_day
FROM hourly_playtime, percentiles
WHERE total_minutes >= p95_minutes * 0.5
ORDER BY hour_of_day
```

**Purpose:** Find hours when player is typically active (≥50% of P95 activity)
**Pre-computation strategy:** Pre-compute and store in `player_features.typical_hours`

---

#### 6.4 GetPlayerServerPingsWithGuids
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:692-728`

**Query Pattern:**
```sql
SELECT
    server_guid,
    avg(ping) as avg_ping
FROM player_metrics
WHERE player_name = ?
  AND ping > 0
  AND ping < 1000
  AND server_guid IN (...)  -- optional
  AND timestamp >= now() - INTERVAL 30 DAY
GROUP BY server_guid
HAVING count(*) >= 10
```

**Purpose:** Get player's average ping to each server
**Pre-computation strategy:** Pre-compute and store in `player_features.server_pings`

---

#### 6.5 GetPlayerMapDominanceScores
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:730-788`

**Query Pattern:**
```sql
WITH player_map_stats AS (
    SELECT
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as player_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as player_score_rate,
        SUM(play_time_minutes) as total_play_time
    FROM player_rounds
    WHERE player_name = ?
      AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5
      AND server_guid IN (...)  -- optional
    GROUP BY map_name
    HAVING total_play_time >= 60  -- 1+ hour on map
),
map_averages AS (
    SELECT
        map_name,
        AVG(final_kills / nullIf(play_time_minutes, 0)) as avg_kill_rate,
        AVG(final_score / nullIf(play_time_minutes, 0)) as avg_score_rate
    FROM player_rounds
    WHERE is_bot = 0 AND round_start_time >= now() - INTERVAL 6 MONTH
      AND play_time_minutes > 5
      AND server_guid IN (...)  -- optional
    GROUP BY map_name
)
SELECT
    p.map_name,
    (p.player_kill_rate / a.avg_kill_rate + p.player_score_rate / a.avg_score_rate) / 2 as dominance_score
FROM player_map_stats p
JOIN map_averages a ON p.map_name = a.map_name
```

**Purpose:** Calculate player's relative performance vs average on each map
**Pre-computation strategy:**
- Pre-compute `map_averages` table (global averages per map)
- Pre-compute player dominance scores against these averages

---

#### 6.6 GetPlayerActivityHours (ComparePlayersActivityHoursAsync)
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:413-459`

**Query Pattern:**
```sql
SELECT
    toHour(round_start_time) as hour_of_day,
    SUM(play_time_minutes) as total_minutes
FROM player_rounds
WHERE player_name = ?
  AND round_start_time >= now() - INTERVAL 6 MONTH
  AND server_guid IN (...)  -- optional
GROUP BY hour_of_day
ORDER BY hour_of_day
```

**Purpose:** Get player's total playtime per hour of day
**Pre-computation strategy:** Pre-compute in `player_features.hourly_activity`

---

#### 6.7 GetPlayerMilestoneAchievements
**Service:** `PlayerComparisonService`
**File:** `bf1942-stats/api/ClickHouse/PlayerComparisonService.cs:1430-1466`

**Query Pattern:**
```sql
SELECT
    achievement_id,
    achievement_name,
    tier,
    value,
    achieved_at
FROM player_achievements_deduplicated
WHERE player_name = ?
  AND achievement_type IN ('milestone', 'badge')
  AND achievement_id NOT LIKE 'kill_streak_%'
ORDER BY achieved_at DESC
```

**Purpose:** Get player's milestone achievements (excluding kill streaks)
**Pre-computation strategy:** Already stored in achievements table - just needs migration

---

## Pre-Computed Data Structures (Implemented)

Based on the query analysis, the following SQLite tables have been implemented in `api/Data/Entities/`:

### 1. PlayerStatsMonthly (replaces player_stats_lifetime + player_stats_rolling)
**Entity:** `api/Data/Entities/PlayerStatsMonthly.cs`
```csharp
player_name TEXT
year INTEGER
month INTEGER  -- 1-12
total_rounds INTEGER
total_kills INTEGER
total_deaths INTEGER
total_score INTEGER
total_play_time_minutes REAL
avg_score_per_round REAL
kd_ratio REAL
kill_rate REAL  -- kills per minute
first_round_time TIMESTAMP
last_round_time TIMESTAMP
updated_at TIMESTAMP
PRIMARY KEY (player_name, year, month)
```
**Update trigger:** Hourly via `AggregateCalculationService`
**Query pattern:** SUM across months for lifetime stats, filter by date range for rolling stats

### 2. PlayerServerStats (weekly buckets for leaderboards)
**Entity:** `api/Data/Entities/PlayerServerStats.cs`
```csharp
player_name TEXT
server_guid TEXT
year INTEGER  -- ISO year
week INTEGER  -- ISO week 1-53
total_rounds INTEGER
total_kills INTEGER
total_deaths INTEGER
total_score INTEGER
total_play_time_minutes REAL
updated_at TIMESTAMP
PRIMARY KEY (player_name, server_guid, year, week)
```
**Update trigger:** Hourly via `AggregateCalculationService`
**Indexes:**
- `IX_PlayerServerStats_Year_Week`
- `IX_PlayerServerStats_ServerGuid_Year_Week`

**Query pattern (leaderboards):** `SqliteLeaderboardService` aggregates weekly buckets on-the-fly:
```csharp
// Example from GetTopScoresAsync
dbContext.PlayerServerStats
    .Where(pss => pss.ServerGuid == serverGuid &&
                 ((pss.Year > startYear || (pss.Year == startYear && pss.Week >= startWeek)) &&
                  (pss.Year < endYear || (pss.Year == endYear && pss.Week <= endWeek))))
    .GroupBy(pss => pss.PlayerName)
    .Select(g => new { TotalScore = g.Sum(pss => pss.TotalScore), ... })
    .OrderByDescending(x => x.TotalScore)
    .Take(limit)
```

**Design decision:** Removed pre-computed `ServerLeaderboardEntries` table. Weekly buckets provide sufficient granularity to compute leaderboards on-demand without the complexity of maintaining ranked snapshots.

### 3. PlayerMapStats (monthly buckets)
**Entity:** `api/Data/Entities/PlayerMapStats.cs`
```csharp
player_name TEXT
map_name TEXT
server_guid TEXT  -- empty string "" for global (cross-server) stats
year INTEGER
month INTEGER
total_rounds INTEGER
total_kills INTEGER
total_deaths INTEGER
total_score INTEGER
total_play_time_minutes REAL
updated_at TIMESTAMP
PRIMARY KEY (player_name, map_name, server_guid, year, month)
```
**Update trigger:** Hourly via `AggregateCalculationService`
**Note:** Uses sentinel value `""` (empty string) for global cross-server map stats via `MapGlobalAverage.GlobalServerGuid`

### 4. PlayerBestScores
**Entity:** `api/Data/Entities/PlayerBestScore.cs`
```csharp
player_name TEXT
period TEXT  -- 'this_week', 'last_30_days', 'all_time'
rank INTEGER  -- 1, 2, or 3
final_score INTEGER
final_kills INTEGER
final_deaths INTEGER
map_name TEXT
server_guid TEXT
round_end_time TIMESTAMP
round_id TEXT
PRIMARY KEY (player_name, period, rank)
```
**Update trigger:** On each round completion (if qualifies)

### ~~5. PlayerMilestones~~ → REMOVED
**Status:** Removed - table dropped in migration `RemovePlayerMilestones`
**Reason:** The fixed milestone thresholds (5k, 10k, 20k, 50k, 75k, 100k kills) were too limited. The feature needs to be redesigned to support a more flexible achievement system.

### 6. HourlyActivityPatterns
**Entity:** `api/Data/Entities/HourlyActivityPattern.cs`
```csharp
game TEXT  -- 'bf1942', 'fh2', 'bfvietnam'
day_of_week INTEGER  -- 0=Sunday, 6=Saturday (SQLite convention)
hour_of_day INTEGER  -- 0-23
unique_players_avg REAL
total_rounds_avg REAL
avg_round_duration REAL
period_type TEXT  -- 'Weekend' or 'Weekday'
updated_at TIMESTAMP
PRIMARY KEY (game, day_of_week, hour_of_day)
```
**Update trigger:** Daily via `DailyAggregateRefreshJob` at 4 AM UTC

### 7. HourlyPlayerPredictions
**Entity:** `api/Data/Entities/HourlyPlayerPrediction.cs`
```csharp
game TEXT  -- 'bf1942', 'fh2', 'bfvietnam'
day_of_week INTEGER  -- 0=Sunday, 6=Saturday
hour_of_day INTEGER  -- 0-23
predicted_players REAL
data_points INTEGER
updated_at TIMESTAMP
PRIMARY KEY (game, day_of_week, hour_of_day)
```
**Update trigger:** Daily via `DailyAggregateRefreshJob`

### 8. ServerHourlyPatterns (replaces server_hourly_stats)
**Entity:** `api/Data/Entities/ServerHourlyPattern.cs`
```csharp
server_guid TEXT
day_of_week INTEGER  -- 0=Sunday, 6=Saturday
hour_of_day INTEGER  -- 0-23
avg_players REAL
min_players REAL
q25_players REAL
median_players REAL
q75_players REAL
q90_players REAL
max_players REAL
data_points INTEGER
updated_at TIMESTAMP
PRIMARY KEY (server_guid, day_of_week, hour_of_day)
```
**Update trigger:** Daily via `DailyAggregateRefreshJob`

### 9. ServerOnlineCounts (raw hourly data)
**Entity:** `api/Data/Entities/ServerOnlineCount.cs`
```csharp
server_guid TEXT
hour_timestamp TIMESTAMP  -- Truncated to hour
game TEXT  -- 'bf1942', 'fh2', 'bfvietnam'
avg_players REAL
peak_players INTEGER
sample_count INTEGER  -- Number of 30s samples averaged
PRIMARY KEY (server_guid, hour_timestamp)
```
**Collection:** `StatsCollectionBackgroundService` aggregates 30s BFList samples to hourly
**Volume:** ~4.3M rows for 180 days across all servers

### 10. MapGlobalAverages
**Entity:** `api/Data/Entities/MapGlobalAverage.cs`
```csharp
map_name TEXT
server_guid TEXT  -- empty string "" for global averages
avg_kill_rate REAL  -- kills per minute
avg_score_rate REAL  -- score per minute
sample_count INTEGER
updated_at TIMESTAMP
PRIMARY KEY (map_name, server_guid)
```
**Update trigger:** Daily via `DailyAggregateRefreshJob`

### 11. ServerMapStats (for server map insights)
**Entity:** `api/Data/Entities/ServerMapStats.cs`
```csharp
server_guid TEXT
map_name TEXT
year INTEGER
month INTEGER  -- 1-12
total_rounds INTEGER
total_play_time_minutes INTEGER
avg_concurrent_players REAL
peak_concurrent_players INTEGER
team1_victories INTEGER
team2_victories INTEGER
team1_label TEXT  -- Most common team 1 label
team2_label TEXT  -- Most common team 2 label
updated_at TIMESTAMP
PRIMARY KEY (server_guid, map_name, year, month)
```
**Update trigger:** Daily via `DailyAggregateRefreshJob.RefreshServerMapStatsAsync`
**Source:** Aggregated from `Rounds` table

### 12. PlayerDailyStats (for trend charts)
**Entity:** `api/Data/Entities/PlayerDailyStats.cs`
```csharp
player_name TEXT
date DATE  -- NodaTime LocalDate
daily_kills INTEGER
daily_deaths INTEGER
daily_score INTEGER
daily_play_time_minutes REAL
daily_rounds INTEGER
PRIMARY KEY (player_name, date)
```
**Update trigger:** On each round completion

---

## Removed Tables

The following tables from the original design were removed:

### ~~server_leaderboards~~ → REMOVED
**Reason:** Pre-computed leaderboard rankings add complexity without significant performance benefit.
**Replacement:** `SqliteLeaderboardService` computes leaderboards on-the-fly by aggregating `PlayerServerStats` weekly buckets.
The weekly bucket approach provides:
- Flexible date range queries (any start/end period)
- Fresh data without cache invalidation concerns
- Simpler update logic (just recalculate current week)

### ~~player_features~~ → REMOVED (Similarity feature dropped)
**Reason:** Similarity/alias detection feature was dropped entirely.
This also removes the need for:
- `server_pings` JSON storage
- `map_dominance` JSON storage
- `typical_hours` JSON storage

### ~~PlayerMilestones~~ → REMOVED (Needs redesign)
**Reason:** The fixed milestone thresholds (5k, 10k, 20k, 50k, 75k, 100k kills) were too limited to support the variety of milestones/achievements needed.
**Impact:** `GetPlayersKillMilestonesAsync` removed from `SqlitePlayerStatsService`. The `Player1KillMilestones` and `Player2KillMilestones` properties in player comparison now return empty lists.
**Migration:** `RemovePlayerMilestones` drops the table.

---

## Migration Priority (Updated)

### Stream A: Foundation (DONE ✅)
EF Core entities and migrations created:
1. ✅ **PlayerStatsMonthly** - Monthly player aggregates
2. ✅ **PlayerServerStats** - Weekly buckets for server leaderboards
3. ✅ **PlayerMapStats** - Monthly map stats per player
4. ✅ **ServerOnlineCounts** - Hourly player count samples

### Stream B: Backfill Services (DONE ✅)
Background services to populate SQLite from ClickHouse:
1. ✅ `AggregateCalculationService` - Calculates current period aggregates hourly
2. ✅ `AggregateBackfillBackgroundService` - One-time historical backfill
3. ✅ `ServerOnlineCountsBackfillBackgroundService` - Backfill 180 days of server counts

### Stream C: Write Paths (DONE ✅)
Update write paths to populate SQLite tables:
1. ❌ ~~Update round completion to populate `PlayerDailyStats`~~ - Table removed (see migration `RemovePlayerDailyStats`)
2. ✅ `PlayerBestScoresService` updates best scores when sessions close (called from `PlayerTrackingService.CloseAllTimedOutSessionsAsync`)
3. ✅ `StatsCollectionBackgroundService` writes `ServerOnlineCounts` - Upserts hourly aggregates every 30s

### Stream D: Read Services (DONE ✅)
Create SQLite read services to replace ClickHouse queries:
1. ✅ `SqliteLeaderboardService` - Server leaderboards from `PlayerServerStats`
2. ✅ `SqlitePlayerStatsService` - Player stats from aggregate tables
3. ✅ `SqliteGameTrendsService` - Predictions and busy indicators from `ServerOnlineCounts`/`ServerHourlyPatterns`
4. ✅ `SqlitePlayerComparisonService` - Player comparison using SQLite (head-to-head via RoundId, common servers, kill rates, map performance)

### Stream E: Daily Refresh Jobs (DONE ✅)
All run via `DailyAggregateRefreshJob` at 4 AM UTC:
5. ✅ **HourlyActivityPatterns** - `RefreshHourlyActivityPatternsAsync` computes from `PlayerSessions`
6. ✅ **HourlyPlayerPredictions** - `RefreshHourlyPlayerPredictionsAsync` computes from `ServerOnlineCounts`
7. ✅ **ServerHourlyPatterns** - `RefreshServerHourlyPatternsAsync` computes percentiles from `ServerOnlineCounts`
8. ✅ **MapGlobalAverages** - `RefreshMapGlobalAveragesAsync` computes from `PlayerMapStats`
9. ✅ **ServerMapStats** - `RefreshServerMapStatsAsync` computes from `Rounds`

### Dropped from Scope
- ~~player_features~~ - Similarity feature dropped
- ~~server_leaderboards~~ - Computed on-the-fly instead
- ~~player_stats_rolling~~ - Use monthly buckets with date filtering

### Deferred: Achievements System
The following achievement-related features have been deferred and hidden in the UI:

1. **PlayerMilestones** - Table dropped in migration `RemovePlayerMilestones`
   - Fixed thresholds (5k, 10k, 20k, 50k, 75k, 100k kills) were too limited
   - Needs redesign to support flexible achievement types
   - `Player1KillMilestones` and `Player2KillMilestones` in comparison return empty arrays

2. **Achievements & Streaks** - UI sections hidden
   - `PlayerDetails.vue` - Achievements & Streaks section hidden
   - `PlayerComparison.vue` - Milestone achievements section hidden (returns empty)
   - API still returns empty arrays for backward compatibility

3. **Future work** - When achievements are redesigned:
   - Design new flexible achievement schema
   - Implement achievement detection in round completion
   - Re-enable UI sections

---

## Data Volumes

| Table | Row Count | Notes |
|-------|-----------|-------|
| `player_rounds` | **3,799,408** | Pre-aggregated round stats |
| `player_metrics` | **52,437,489** | Raw 30s snapshots |
| `server_online_counts` | TBD | Server population samples |
| `player_achievements` | TBD | Gamification events |

**Key insight:** SQLite already contains the same raw data - the ClickHouse tables are populated via background sync from SQLite. This means:
1. No data loss risk - source of truth is SQLite
2. Pre-computed aggregates can be built directly from SQLite
3. Migration is about replacing query patterns, not moving data

---

## ClickHouse-Specific Query Patterns

These are the ClickHouse features used in the current queries that have no direct SQLite equivalent or would perform poorly.

### 1. Window Functions Over Large Datasets

**Used in:**
- `GetPlayerTimeSeriesTrendAsync` - cumulative sums over player's entire history
- `GetPlayersKillMilestonesAsync` - ROW_NUMBER + LAG to detect threshold crossings
- `FindPlayersBySimilarityWithGuids` - ROW_NUMBER for favorite server selection
- `GetPlayerTypicalOnlineHours` - quantile() calculation

**ClickHouse syntax:**
```sql
SUM(final_kills) OVER (PARTITION BY player_name ORDER BY round_end_time ROWS UNBOUNDED PRECEDING)
quantile(0.95)(total_minutes) as p95_minutes
```

**SQLite limitation:** Window functions exist but performance degrades badly over millions of rows. Quantile functions don't exist natively.

**Alternative approach needed:** Pre-compute cumulative values at write time, or store snapshots.

---

### 2. argMax() Function

**Used in:**
- `GetPlayerServerInsightsAsync` - get round_id/map/time of highest score
- `GetSmartPredictionInsightsAsync` - get latest players_online per server per hour

**ClickHouse syntax:**
```sql
argMax(round_id, final_score) as highest_score_round_id
argMax(players_online, timestamp) as latest_players_per_server
```

**SQLite equivalent:** Requires correlated subquery or self-join, much slower:
```sql
SELECT round_id FROM player_rounds pr2
WHERE pr2.player_name = pr.player_name
  AND pr2.final_score = (SELECT MAX(final_score) FROM player_rounds WHERE player_name = pr.player_name)
LIMIT 1
```

**Alternative approach needed:** Store the "argMax" values as separate columns updated on write.

---

### 3. groupArray() Function

**Used in:**
- `GetServerBusyIndicatorAsync` - collect all daily averages into array for percentile calc
- `FindPlayersBySimilarityWithGuids` - collect active servers, typical hours, ping data

**ClickHouse syntax:**
```sql
groupArray(hourly_avg) as daily_averages
groupArray(DISTINCT server_guid) as active_servers
groupArray(concat(server_guid, ':', toString(avg_ping))) as ping_data
```

**SQLite equivalent:** GROUP_CONCAT exists but returns string, not typed array. Parsing required.

**Alternative approach needed:** Store arrays as JSON, or use separate lookup tables.

---

### 4. Time Extraction Functions

**Used in:** Most time-series queries

**ClickHouse syntax:**
```sql
toDayOfWeek(timestamp)  -- 1-7 (Monday-Sunday)
toHour(timestamp)       -- 0-23
toDate(timestamp)       -- date only
dateDiff('day', start, end)
```

**SQLite equivalent:**
```sql
strftime('%w', timestamp)  -- 0-6 (Sunday-Saturday) - DIFFERENT!
strftime('%H', timestamp)  -- 00-23 as string
date(timestamp)
julianday(end) - julianday(start)
```

**Issues:**
- Day-of-week numbering differs (Sunday=0 vs Monday=1)
- Returns strings not integers
- Timezone handling differs

**Alternative approach needed:** Standardize on one convention, add computed columns or handle in application code.

---

### 5. INTERVAL Syntax for Rolling Windows

**Used in:** Almost every query

**ClickHouse syntax:**
```sql
WHERE timestamp >= now() - INTERVAL 30 DAY
WHERE round_start_time >= now() - INTERVAL 6 MONTH
```

**SQLite equivalent:**
```sql
WHERE timestamp >= datetime('now', '-30 days')
WHERE round_start_time >= datetime('now', '-6 months')
```

**Issues:** Different syntax, need to rewrite all queries.

---

### 6. Complex Multi-CTE Queries

**Used in:**
- `FindPlayersBySimilarityWithGuids` - 10+ CTEs with conditional includes
- `GetPlayersKillMilestonesAsync` - 4 CTEs for cumulative calculation
- `GetSmartPredictionInsightsAsync` - nested subqueries 3 levels deep

**Problem:** These queries rely on ClickHouse's ability to execute complex analytical queries quickly over millions of rows. SQLite will struggle with:
- Multiple passes over large tables
- Complex joins between CTEs
- Lack of parallel execution

**Alternative approach needed:** Break into multiple simpler queries, or restructure data storage entirely.

---

### 7. Self-Joins for Overlap Detection

**Used in:**
- `GetHeadToHeadData` - find overlapping rounds between two players
- `CalculateBulkTemporalOverlap` - cross-join to find simultaneous sessions

**ClickHouse syntax:**
```sql
FROM player_rounds p1
JOIN player_rounds p2
  ON p1.server_guid = p2.server_guid
  AND p1.round_start_time <= p2.round_end_time
  AND p2.round_start_time <= p1.round_end_time
```

**SQLite limitation:** Self-join on 3.8M row table = potential billions of comparisons.

**Alternative approach needed:**
- Index heavily on (player_name, server_guid, round_start_time)
- Filter to recent data only
- Accept slower performance with caching
- Or pre-compute common pairs

---

### 8. Conditional Query Building

**Used in:** `FindPlayersBySimilarityWithGuids`

**Pattern:**
```csharp
var includePingData = mode == SimilarityMode.AliasDetection;
var query = $@"...
{(includePingData ? "candidate_server_pings AS (...)" : "")}
...";
```

**Issue:** Query is dynamically constructed based on mode. This works in ClickHouse because the optimizer handles it. In SQLite, each variant would need to be a separate, tested query path.

---

## Queries by Migration Difficulty

### Simple (Direct SQLite Translation)
These use basic SQL that works similarly in both databases:

| Query | Complexity | Notes |
|-------|------------|-------|
| `GetKillRates` | Simple | Basic SUM/GROUP BY |
| `GetBucketTotals` | Simple | Basic SUM/GROUP BY, run 4x |
| `GetMapPerformance` | Simple | Basic SUM/GROUP BY |
| `GetCommonServersData` | Simple | INTERSECT works in SQLite |
| `GetPlayerActiveServers` | Simple | DISTINCT query |

### Medium (Requires Query Rewrite)
These use ClickHouse features with SQLite equivalents:

| Query | Complexity | Blocking Issue |
|-------|------------|----------------|
| `GetPlayerStatsAsync` | Medium | Time functions, HAVING |
| `GetMostActivePlayersAsync` | Medium | Time range syntax |
| `GetTopScoresAsync` | Medium | Time range, min rounds |
| `GetTopKDRatiosAsync` | Medium | Time range, min rounds |
| `GetTopKillRatesAsync` | Medium | Time range, min rounds |
| `GetServerStats` | Medium | Time period conditions |
| `GetAveragePing` | Medium | INTERVAL syntax |
| `GetWeeklyActivityPatternsAsync` | Medium | Day-of-week extraction |

### Hard (Requires Data Model Change)
These rely on ClickHouse-specific features:

| Query | Complexity | Blocking Issue |
|-------|------------|----------------|
| `GetPlayerServerInsightsAsync` | Hard | argMax() function |
| `GetPlayerBestScoresAsync` | Hard | UNION ALL with subquery LIMIT |
| `GetPlayerTimeSeriesTrendAsync` | Hard | Window functions over history |
| `GetSmartPredictionInsightsAsync` | Hard | Nested aggregations, argMax |
| `GetServerBusyIndicatorAsync` | Hard | groupArray, percentile calc |
| `GetPlayersKillMilestonesAsync` | Hard | Window functions, cumulative |
| `GetPlayerTypicalOnlineHours` | Hard | quantile() function |

### Very Hard (Requires Architectural Change)
These need fundamentally different approaches:

| Query | Complexity | Blocking Issue |
|-------|------------|----------------|
| ~~`GetHeadToHeadData`~~ | ~~Very Hard~~ | **Now Easy** - use `RoundId` join instead of time overlap |
| `CalculateBulkTemporalOverlap` | Very Hard | Cross-join for session overlap (DROPPED - part of similarity feature) |
| `FindPlayersBySimilarityWithGuids` | Very Hard | 150-line multi-CTE query (DROPPED - similarity feature removed) |
| `GetPlayerMapDominanceScores` | Very Hard | Comparison against global averages (DROPPED - part of similarity feature) |

---

## Data Storage Alternatives

For the "Hard" and "Very Hard" queries, these are potential storage/retrieval strategies:

### Strategy A: Pre-Computed Snapshots
Store the query result, not the raw data.

**Example for `GetPlayerBestScoresAsync`:**
```
player_best_scores:
  player_name, period, rank -> score, kills, deaths, map, server, time, round_id
```
Update when: A round completes and score qualifies for top 3.

**Pros:** Fast reads, simple queries
**Cons:** Stale data if update logic has bugs, storage for rarely-accessed players

### Strategy B: Materialized Aggregates with Incremental Updates
Store running totals, update on each event.

**Example for `GetPlayerTimeSeriesTrendAsync`:**
```
player_cumulative_daily:
  player_name, date -> cumulative_kills, cumulative_deaths, cumulative_minutes
```
Update when: Round completes, add to previous day's cumulative values.

**Pros:** No window function needed at read time
**Cons:** Must backfill historical data, complex update logic

### Strategy C: Denormalized Event Log
Store events with pre-computed derived values.

**Example for milestone detection:**
```
player_rounds (extended):
  ..., cumulative_kills_after_round INTEGER
```
Check milestone threshold at insert time, write to `player_milestones` if crossed.

**Pros:** Detection happens once at write time
**Cons:** Adds columns to existing tables, migration of historical data

### Strategy D: Application-Level Computation
Move complex logic to C# code, accept higher latency.

**Example for `GetHeadToHeadData`:**
1. Query player1's recent rounds (indexed by player_name, time)
2. Query player2's recent rounds (indexed by player_name, time)
3. Compute overlap in application code

**Pros:** No complex SQL needed
**Cons:** More data transfer, slower, memory usage

### Strategy E: Hybrid with Caching
Keep expensive queries but cache aggressively.

**Example for similarity matching:**
1. Compute similarity once per day via background job
2. Store results in `player_similarity_cache`
3. Serve from cache, refresh on schedule

**Pros:** Defers the hard problem
**Cons:** Stale data, cache invalidation complexity

---

## Migration Strategies by Query

### Interim Sync Approach

Before decommissioning ClickHouse, we'll run a parallel sync:
1. New SQLite tables are created with the target schema
2. Background job populates SQLite from ClickHouse (one-time backfill)
3. New write paths update both ClickHouse and SQLite
4. Read paths gradually switch to SQLite
5. Once validated, ClickHouse writes are disabled
6. ClickHouse is decommissioned

---

### Very Hard Queries - Migration Plans

#### 1. FindPlayersBySimilarityWithGuids → SQLite Approach

**Current:** 150-line CTE query computing similarity across all players.

**New approach:** Pre-compute player feature vectors, compare in application code.

**SQLite schema:**
```sql
CREATE TABLE player_features (
    player_name TEXT PRIMARY KEY,
    total_kills INTEGER,
    total_deaths INTEGER,
    total_play_time_minutes REAL,
    kd_ratio REAL,
    kill_rate REAL,
    favorite_server_guid TEXT,
    typical_hours_json TEXT,  -- JSON array: [14, 15, 16, 20, 21]
    server_pings_json TEXT,   -- JSON object: {"server1": 45.2, "server2": 62.1}
    game_ids TEXT,            -- comma-separated
    updated_at TEXT
);
CREATE INDEX idx_player_features_kd ON player_features(kd_ratio);
CREATE INDEX idx_player_features_server ON player_features(favorite_server_guid);
```

**Query approach:**
```sql
-- Step 1: Get target player's features
SELECT * FROM player_features WHERE player_name = ?;

-- Step 2: Find candidates on same servers with similar K/D
SELECT * FROM player_features
WHERE favorite_server_guid = ?
  AND kd_ratio BETWEEN ? AND ?  -- target ± tolerance
  AND player_name != ?
ORDER BY ABS(kd_ratio - ?) ASC
LIMIT 50;
```

**Step 3:** Compute similarity scores in C# using the feature vectors.

**Update strategy:** Background job refreshes player_features daily, or on significant stat changes.

**Interim sync:** Query ClickHouse `FindPlayersBySimilarityWithGuids` for all active players, extract feature vectors, insert into SQLite.

---

#### 2. CalculateBulkTemporalOverlap → SQLite Native

**Current:** Cross-join to find minutes of simultaneous play.

**SQLite can do this** with proper indexing since we filter to specific players first.

**Approach:**
```sql
-- Get overlap between target player and candidates
-- Filter to last 3 months, specific players only
WITH target_rounds AS (
    SELECT round_start_time, round_end_time, server_guid
    FROM PlayerSessions  -- or new completed_rounds table
    WHERE player_name = @target
      AND round_start_time >= datetime('now', '-3 months')
),
candidate_rounds AS (
    SELECT player_name, round_start_time, round_end_time, server_guid
    FROM PlayerSessions
    WHERE player_name IN (SELECT value FROM json_each(@candidate_list))
      AND round_start_time >= datetime('now', '-3 months')
)
SELECT
    c.player_name,
    SUM(
        MAX(0,
            (julianday(MIN(c.round_end_time, t.round_end_time)) -
             julianday(MAX(c.round_start_time, t.round_start_time))) * 24 * 60
        )
    ) as overlap_minutes
FROM candidate_rounds c
JOIN target_rounds t
  ON c.server_guid = t.server_guid
  AND c.round_start_time < t.round_end_time
  AND c.round_end_time > t.round_start_time
GROUP BY c.player_name;
```

**Key:** The WHERE clause filters to specific players first, making the join tractable.

**Required indexes:**
```sql
CREATE INDEX idx_sessions_player_time ON PlayerSessions(player_name, round_start_time);
CREATE INDEX idx_sessions_server_time ON PlayerSessions(server_guid, round_start_time);
```

**Performance note:** With ~100 candidates and 3 months of data per player (~50-200 rounds each), this is maybe 10,000-50,000 comparisons, not billions.

---

#### 3. GetHeadToHeadData → SQLite Native (Simplified with RoundId)

**Current ClickHouse approach:** Self-join on player_rounds with time overlap detection.

**Simplified SQLite approach:** `PlayerSession` now has a `RoundId` column. Players who participated in the same round share the same `RoundId`, eliminating the need for time overlap logic.

```sql
SELECT
    p1.StartTime as round_start_time,
    p1.LastSeenTime as round_end_time,
    p1.ServerGuid as server_guid,
    p1.MapName as map_name,
    p1.TotalScore as player1_score,
    p1.TotalKills as player1_kills,
    p1.TotalDeaths as player1_deaths,
    p2.TotalScore as player2_score,
    p2.TotalKills as player2_kills,
    p2.TotalDeaths as player2_deaths,
    p1.RoundId as round_id
FROM PlayerSessions p1
JOIN PlayerSessions p2 ON p1.RoundId = p2.RoundId
WHERE p1.PlayerName = @player1
  AND p2.PlayerName = @player2
  AND p1.RoundId IS NOT NULL
  AND p1.StartTime >= datetime('now', '-6 months')
ORDER BY p1.StartTime DESC
LIMIT 50;
```

**Why this is much simpler:**
- Direct join on `RoundId` instead of time overlap detection
- No need to check `server_guid` or `map_name` in join (RoundId already implies same round)
- Query complexity reduced from O(N×M) time comparisons to O(N) index lookups
- Existing index on `PlayerSessions(PlayerName, StartTime)` is sufficient

**Implementation:** Add to `SqlitePlayerStatsService` or create dedicated `SqlitePlayerComparisonService`.

---

#### 4. GetPlayerMapDominanceScores → Pre-compute

**Current:** Compare player's map performance against global averages.

**New approach:**
1. Maintain `map_global_averages` table (updated daily)
2. Compute dominance at read time with simple division

**SQLite schema:**
```sql
CREATE TABLE map_global_averages (
    map_name TEXT PRIMARY KEY,
    avg_kill_rate REAL,  -- kills per minute
    avg_score_rate REAL, -- score per minute
    sample_count INTEGER,
    updated_at TEXT
);

-- Player's map stats already exist in player_map_stats
```

**Query:**
```sql
SELECT
    pms.map_name,
    pms.total_kills / NULLIF(pms.total_play_time_minutes, 0) as player_kill_rate,
    mga.avg_kill_rate,
    (pms.total_kills / NULLIF(pms.total_play_time_minutes, 0)) / NULLIF(mga.avg_kill_rate, 0) as dominance_score
FROM player_map_stats pms
JOIN map_global_averages mga ON pms.map_name = mga.map_name
WHERE pms.player_name = ?
  AND pms.total_play_time_minutes >= 60;
```

**Update strategy:** Daily background job computes global averages from all recent rounds.

---

### Hard Queries - Migration Plans

#### 5. GetPlayerTypicalOnlineHours → Write-time computation

**Decision:** Calculate during stats collection, store as deduplicated value.

**SQLite schema:**
```sql
CREATE TABLE player_activity_hours (
    player_name TEXT PRIMARY KEY,
    typical_hours_json TEXT,  -- JSON array of hour integers: [14, 15, 16, 20, 21, 22]
    hourly_minutes_json TEXT, -- JSON object: {"0": 12, "1": 5, ..., "23": 45}
    updated_at TEXT
);
```

**Update logic (in background service):**
1. When collecting stats, track minutes played per hour
2. Periodically (hourly or daily), recalculate typical hours
3. Typical = hours with activity >= 50% of max hour's activity

**No query needed at read time** - just fetch the pre-computed JSON.

---

#### 6. GetPlayersKillMilestonesAsync → Write-time detection

**Decision:** Detect milestones when round completes, store immediately.

**SQLite schema:**
```sql
CREATE TABLE player_milestones (
    player_name TEXT,
    milestone INTEGER,  -- 5000, 10000, 20000, 50000, 75000, 100000
    achieved_at TEXT,
    total_kills_at_milestone INTEGER,
    days_to_achieve INTEGER,
    PRIMARY KEY (player_name, milestone)
);
```

**Update logic (when round completes):**
```csharp
// In round completion handler
var previousTotal = player.TotalKills;
var newTotal = previousTotal + round.FinalKills;

var milestones = new[] { 5000, 10000, 20000, 50000, 75000, 100000 };
foreach (var milestone in milestones)
{
    if (previousTotal < milestone && newTotal >= milestone)
    {
        // Record milestone achievement
        await InsertMilestone(player.Name, milestone, DateTime.UtcNow, newTotal, daysPlaying);
    }
}
```

**Interim sync:** Run ClickHouse query once to backfill existing milestones.

---

#### 7. GetPlayerTimeSeriesTrendAsync → DROPPED

**Decision:** Feature can be removed. No migration needed.

---

#### 8. GetSmartPredictionInsightsAsync → Pre-computed hourly patterns

**Decision:** Keep feature, pre-compute the patterns.

**SQLite schema:**
```sql
CREATE TABLE hourly_player_counts (
    game TEXT,
    day_of_week INTEGER,  -- 0=Sunday, 6=Saturday (SQLite convention)
    hour_of_day INTEGER,  -- 0-23
    avg_players REAL,
    sample_count INTEGER,
    updated_at TEXT,
    PRIMARY KEY (game, day_of_week, hour_of_day)
);
```

**Query (at read time):**
```sql
-- Get predictions for current hour and next 8 hours
SELECT day_of_week, hour_of_day, avg_players
FROM hourly_player_counts
WHERE game = @game
  AND (day_of_week, hour_of_day) IN (
    (@dow0, @hour0), (@dow1, @hour1), (@dow2, @hour2), ... -- 9 tuples
  );
```

**Current player count:** Query SQLite PlayerSessions directly (already done in current code).

**Update strategy:** Daily background job aggregates last 30-60 days of server_online_counts into averages.

**Interim sync:** Query ClickHouse for the aggregated patterns, insert into SQLite.

---

#### 9. GetServerBusyIndicatorAsync → Pre-computed percentiles

**Decision:** Keep feature, pre-compute the statistics.

**SQLite schema:**
```sql
CREATE TABLE server_hourly_patterns (
    server_guid TEXT,
    day_of_week INTEGER,
    hour_of_day INTEGER,
    avg_players REAL,
    min_players REAL,
    q25_players REAL,
    median_players REAL,
    q75_players REAL,
    q90_players REAL,
    max_players REAL,
    sample_count INTEGER,
    updated_at TEXT,
    PRIMARY KEY (server_guid, day_of_week, hour_of_day)
);
```

**Query:**
```sql
-- Get pattern for specific servers at specific hours
SELECT server_guid, hour_of_day, avg_players, q25_players, median_players, q75_players, q90_players
FROM server_hourly_patterns
WHERE server_guid IN (SELECT value FROM json_each(@server_guids))
  AND day_of_week = @current_dow
  AND hour_of_day IN (SELECT value FROM json_each(@hours));  -- current ± 4 hours
```

**Percentile calculation:** Done in background job using SQLite window functions or application code.

**Interim sync:** Query ClickHouse for pre-computed patterns, insert into SQLite.

---

#### 10. GetPlayerBestScoresAsync → Write-time tracking

**Decision:** Track best scores when rounds complete.

**SQLite schema:**
```sql
CREATE TABLE player_best_scores (
    player_name TEXT,
    period TEXT,  -- 'this_week', 'last_30_days', 'all_time'
    rank INTEGER, -- 1, 2, or 3
    final_score INTEGER,
    final_kills INTEGER,
    final_deaths INTEGER,
    map_name TEXT,
    server_guid TEXT,
    round_end_time TEXT,
    round_id TEXT,
    PRIMARY KEY (player_name, period, rank)
);
```

**Update logic (when round completes):**
```csharp
// Check if this score qualifies for top 3 in any period
foreach (var period in new[] { "this_week", "last_30_days", "all_time" })
{
    var currentBest = await GetBestScores(player, period);
    if (round.FinalScore > currentBest.MinScore || currentBest.Count < 3)
    {
        await UpdateBestScores(player, period, round);
    }
}
```

**Cleanup job:** Weekly job removes stale "this_week" entries, recalculates "last_30_days".

**Interim sync:** Query ClickHouse for current best scores, insert into SQLite.

---

#### 11. GetPlayerServerInsightsAsync → IMPLEMENTED ✅

**Implementation:** `SqlitePlayerStatsService.GetPlayerServerInsightsAsync()` queries `PlayerServerStats` weekly buckets.

**Toggle:** `UseSqlite("GetPlayerServerInsights")` - enabled in deployment.

**Current behavior:**
- Aggregates player stats per server from `PlayerServerStats` weekly buckets
- Filters to servers with 10+ hours playtime
- Returns server name, total minutes, kills, deaths, rounds, kills per minute

**Highest score data:** Currently returns placeholder values (0, empty strings, DateTime.MinValue). The ClickHouse version used `argMax()` to track the round with highest score. If UI requires this data, options are:
1. Query `PlayerSessions` for max score per server at read time
2. Add highest score tracking to `PlayerServerStats` entity
3. Accept the limitation if UI doesn't prominently display this data

**TODO:** Verify with UI whether highest score data is displayed and needed.

---

## Interim Sync Jobs

To populate SQLite before decommissioning ClickHouse:

| Target Table | Source | One-time Backfill | Ongoing Sync |
|--------------|--------|-------------------|--------------|
| player_features | FindPlayersBySimilarity query | Query all active players | Daily refresh |
| player_milestones | GetPlayersKillMilestones | Query all players | Write-time only |
| hourly_player_counts | GetSmartPrediction patterns | Query aggregated data | Daily refresh |
| server_hourly_patterns | GetServerBusyIndicator | Query aggregated data | Daily refresh |
| player_best_scores | GetPlayerBestScores | Query all players | Write-time only |
| player_server_stats | Aggregate from rounds | Full aggregation | Write-time updates |
| player_activity_hours | GetPlayerTypicalOnlineHours | Query all active players | Background service |
| map_global_averages | GetPlayerMapDominance | Query global stats | Daily refresh |

---

## Performance Metrics Requirement

**Every migrated query must emit telemetry** to gauge SQLite performance in AKS (which runs on lesser hardware than local dev).

### Required Metrics Per Query

Each new SQLite-based service method must log/emit:

```csharp
using var activity = ActivitySources.SqliteAnalytics.StartActivity("MethodName");
activity?.SetTag("query.name", "GetPlayerStats");
activity?.SetTag("query.player_name", playerName);
activity?.SetTag("query.filters", filterDescription);

// After execution:
activity?.SetTag("result.row_count", results.Count);
activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);
activity?.SetTag("result.table", "player_server_stats");
```

### Metrics to Capture

| Metric | Purpose |
|--------|---------|
| `query.name` | Identify which query |
| `query.filters` | What parameters were used |
| `result.row_count` | Number of rows returned - critical for estimating AKS load |
| `result.duration_ms` | Execution time |
| `result.table` | Which table(s) queried |
| `result.cache_hit` | If Redis cache was used |

### Comparison Dashboard

During dual-write phase, compare:
- ClickHouse query time vs SQLite query time
- Row counts should match exactly
- Alert on >10% performance degradation

### AKS-Specific Concerns

- SQLite runs single-threaded - concurrent queries queue
- Disk I/O on AKS may be slower than local SSD
- Memory constraints affect query planning
- Monitor for lock contention on write-heavy tables

---

## Migration Phases (Updated)

### Phase 1: Schema & Backfill ✅ COMPLETE
1. ✅ Created EF Core entities in `api/Data/Entities/`
2. ✅ Created migrations for all aggregate tables
3. ✅ `AggregateCalculationService` populates current period data hourly
4. 🔄 Backfill services populate historical data from ClickHouse

### Phase 2: Dual-Read ✅ COMPLETE
1. ✅ `SqliteLeaderboardService` implements leaderboard queries
2. ✅ Added `v2` controllers that read from SQLite for landing summary, busy indicator, players-online history, server leaderboards, and player best scores
3. ✅ UI wired to `v2` endpoints for the above
4. ✅ Deployment config enables SQLite feature flags where selector is used
5. ✅ `SqlitePlayerComparisonService` implements full player comparison (bucket totals, map performance, head-to-head, common servers)
6. ✅ `SqliteGameTrendsService` implements prediction insights, busy indicators, activity patterns

**Update (Jan 2026):** All read paths have been migrated to SQLite. The deployment config includes all 12 `ClickHouseMigration__UseSqlite__*` flags set to `true`. ClickHouse collection stays enabled for validation only. Milestones remain out of scope until the redesigned achievement system lands.

### Phase 3: Write Path Migration
1. ✅ Update `StatsCollectionBackgroundService` for `ServerOnlineCounts`
2. ✅ Update round completion for `PlayerBestScores`
3. ✅ Validate write path data matches ClickHouse sync

### Phase 4: Read Path Cutover ✅ COMPLETE
1. ✅ Switch high-traffic endpoints to SQLite (LandingPage first)
2. ✅ Switch all remaining endpoints to SQLite via feature toggles
3. ✅ All 12 SQLite toggles enabled in deployment
4. ✅ ClickHouse read code kept as fallback (toggles can be flipped back)
5. ⏳ Monitor performance via telemetry (compare ClickHouse vs SQLite row counts + latency)

### Phase 5: Decommission (NEXT)
1. ⏳ Add telemetry comparison to verify parity between ClickHouse and SQLite results
2. ⏳ Disable ClickHouse sync jobs (set `ENABLE_*_SYNCING` env vars to `false`)
3. ⏳ Remove ClickHouse read services (`api/ClickHouse/*.cs`)
4. ⏳ Shut down ClickHouse infrastructure
5. ⏳ Archive ClickHouse data for backup

---

## Next Steps

### Recently Completed (Jan 2026)
- ✅ `GetPlayerServerInsightsAsync` - SQLite implementation via `SqlitePlayerStatsService`, toggle enabled
- ✅ `GetAveragePing` - SQLite implementation via `PlayerStatsService.GetAveragePingFromSessions()`, toggle enabled
- ✅ `GetHeadToHeadData` - SQLite implementation via `SqlitePlayerComparisonService.GetHeadToHeadDataAsync()`, toggle enabled
- ✅ `GetPlayerBestScoresAsync` - SQLite implementation in `SqlitePlayerStatsService`, toggle enabled in `PlayerStatsService`
- ✅ `GetServerStats` (map breakdown) - SQLite query via `SqlitePlayerStatsService.GetPlayerMapStatsAsync`, wired in `PlayersController`
- ✅ `SqlitePlayerComparisonService` - Full implementation with `ComparePlayersAsync`, `GetBucketTotalsAsync`, `GetMapPerformanceAsync`, `GetHeadToHeadDataAsync`, `GetCommonServersDataAsync`
- ✅ `SqliteGameTrendsService` - Full implementation with `GetSmartPredictionInsightsAsync`, `GetServerBusyIndicatorAsync`, `GetPlayersOnlineHistoryAsync`, `GetWeeklyActivityPatternsAsync`

### Current Status (Updated)

All read paths have been migrated to SQLite:
- **12 SQLite toggles enabled** in deployment config
- **ClickHouse collection still active** for validation (can be disabled once parity verified)

### Remaining Work
1. **Add telemetry comparison** - Compare ClickHouse vs SQLite latency + row count parity in production
2. **Verify UI usage of highest score data** in server insights (currently returns placeholders from SQLite)
3. **Disable ClickHouse collection** - Set `ENABLE_CLICKHOUSE_ROUND_SYNCING`, `ENABLE_PLAYER_METRICS_SYNCING`, `ENABLE_SERVER_ONLINE_COUNTS_SYNCING` to `false`
4. **Remove ClickHouse read services** - Delete `api/ClickHouse/*.cs` read services once validated
5. **Shut down ClickHouse infrastructure** - Decommission ClickHouse server

---

## Decisions Made

### Feature Decisions

| Decision | Outcome | Impact |
|----------|---------|--------|
| **Similarity/Alias Detection** | DROP entirely | Eliminates: `FindPlayersBySimilarityWithGuids`, `CalculateBulkTemporalOverlap`, `GetPlayerMapDominanceScores`, `GetPlayerTypicalOnlineHours` (for similarity), `player_features` table |
| **Historical depth** | Reduce where possible | Queries can use shorter windows (e.g., 3 months instead of 6) to improve performance |
| **Player ping in similarity** | DROP (part of similarity) | No longer need `server_pings_json` storage |

### Data Architecture Decisions

| Decision | Outcome |
|----------|---------|
| **PlayerServerStats weekly buckets** | Use ISO week buckets (Year, Week) instead of monthly. Provides finer granularity for leaderboard queries while keeping table size manageable. ~52 rows/year per player-server combination. |
| **Remove ServerLeaderboardEntries** | Compute leaderboards on-the-fly from `PlayerServerStats` weekly buckets via `SqliteLeaderboardService`. Eliminates complexity of maintaining ranked snapshots. |
| **PlayerStatsMonthly** | Monthly buckets (Year, Month) for player-wide stats. SUM across months for lifetime stats. |
| **PlayerMapStats monthly buckets** | Monthly buckets with empty string `""` as sentinel for global (cross-server) stats. |
| **PlayerSessions for head-to-head** | Use existing table directly, test performance |
| **server_online_counts** | Single SQLite table with hourly granularity (~4.3M rows). Backfill 180 days from ClickHouse, collect in `StatsCollectionBackgroundService` going forward |
| **Average ping** | KEEP - SQLite already has `PlayerSessions.AveragePing`, just rewrite `GetAveragePingFromClickHouse()` to query SQLite |
| **Player search** | Already SQLite (`dbContext.Players`) - no migration needed |
| **Aggregate updates** | Hourly recalculation via `AggregateCalculationService` (idempotent delete+insert pattern) |
| **Caching** | Redis everywhere for pre-computed tables |
| **Staleness** | 1 hour for current period aggregates, 1 day acceptable for historical |

### Queries After Dropping Similarity

**Removed queries (no longer needed):**
- `FindPlayersBySimilarityWithGuids`
- `CalculateBulkTemporalOverlap`
- `GetPlayerMapDominanceScores`
- `GetPlayerTypicalOnlineHours` (was primarily for similarity)
- `GetPlayerServerPingsWithGuids` (was for alias detection)

**Remaining queries to migrate:**

| Query | Difficulty | Strategy | Status |
|-------|------------|----------|--------|
| `GetTopScoresAsync` | Easy | `SqliteLeaderboardService` | ✅ Done |
| `GetTopKDRatiosAsync` | Easy | `SqliteLeaderboardService` | ✅ Done |
| `GetTopKillRatesAsync` | Easy | `SqliteLeaderboardService` | ✅ Done |
| `GetMostActivePlayersAsync` | Easy | `SqliteLeaderboardService` | ✅ Done |
| `GetHeadToHeadData` | Easy | Join on `RoundId` in PlayerSessions | ✅ Done |
| ~~`GetPlayersKillMilestonesAsync`~~ | ~~Easy~~ | ~~Dropped - needs redesign~~ | ❌ Removed |
| `GetSmartPredictionInsightsAsync` | Medium | Query `ServerOnlineCounts` SQLite table | ✅ Done |
| `GetServerBusyIndicatorAsync` | Medium | Query `ServerOnlineCounts` + `ServerHourlyPatterns` | ✅ Done |
| `GetPlayerBestScoresAsync` | Easy | Read from `PlayerBestScores` in `SqlitePlayerStatsService` | ✅ Done |
| `GetPlayerServerInsightsAsync` | Easy | `SqlitePlayerStatsService` | ✅ Done (toggle: `UseSqlite("GetPlayerServerInsights")`) |
| `GetAveragePing` | Easy | Query `PlayerSessions.AveragePing` | ✅ Done (toggle: `UseSqlite("GetAveragePing")`) |
| `GetPlayerStatsAsync` | Easy | Query `PlayerStatsMonthly` | ✅ Done |
| `GetServerStats` (map breakdown) | Easy | Query `PlayerMapStats` | ✅ Done |
| `GetServerMapsInsights` | Easy | Query `ServerMapStats` | ✅ Done (toggle: `UseSqlite("GetServerMapsInsights")`) |

---

## Open Questions (Remaining)

~~All questions resolved - see Resolved Questions below.~~

---

## Resolved Questions

### 1. Average ping in player comparison ✅ IMPLEMENTED
- SQLite `PlayerSessions.AveragePing` column stores per-session average ping
- Toggle `UseSqlite("GetAveragePing")` switches between ClickHouse and SQLite
- **Implementation:** `PlayerStatsService.GetAveragePingFromSessions()` queries last 6 months of sessions
- **Deployment:** Toggle enabled in `deploy/app/deployment.yaml`

### 2. Percentile calculation location ✅ Background Job
- Compute percentiles in C# during background job execution
- Store pre-computed values in `server_hourly_patterns` table
- Keeps transactional writes fast

### 3. Cache strategy ✅ Redis Everywhere
- Cache all pre-computed tables in Redis
- Includes: leaderboards, player stats, server patterns, busy indicators

### 4. Write contention ✅ Queue Updates
- Queue aggregate table updates for background processing
- Keeps round completion handler fast
- Background worker processes queue and updates aggregate tables

### 5. server_online_counts schema ✅ Single Hourly Table
- See detailed schema below

### 6. Query frequency ✅ Answered
- **Highest traffic pages**: LandingPageV2 > ServerDetails > PlayerDetails
- See API-to-ClickHouse mapping below

### 7. Staleness tolerance ✅ 1 Day Acceptable
- Rolling aggregates (last_30_days, last_6_months) can be daily refresh
- Only exception: most recent rounds need live data (already provided)

### 8. Player search ✅ Already SQLite
- `/stats/Players/search` endpoint uses `dbContext.Players` directly
- No ClickHouse dependency, no migration needed

### 9. Performance baseline ✅ 150-200ms
- ClickHouse queries average 150-200ms
- Slightly slower acceptable for SQLite tradeoff

---

## ServerOnlineCounts SQLite Schema (Implemented)

**Entity:** `api/Data/Entities/ServerOnlineCount.cs`

```csharp
public class ServerOnlineCount
{
    public required string ServerGuid { get; set; }
    public Instant HourTimestamp { get; set; } // Truncated to hour
    public required string Game { get; set; } // bf1942, fh2, bfvietnam
    public double AvgPlayers { get; set; }
    public int PeakPlayers { get; set; }
    public int SampleCount { get; set; } // Number of 30s samples averaged
}
```

**Primary key:** `(ServerGuid, HourTimestamp)`

**Volume estimate**: ~1000 servers × 180 days × 24 hours = **~4.3M rows**

**Collection strategy**:
- `StatsCollectionBackgroundService` already runs every 30 seconds
- Upsert hourly aggregates using running average formula
- `ServerOnlineCountsBackfillBackgroundService` handles historical backfill from ClickHouse

**Backfill query (ClickHouse → SQLite)**:
```sql
-- One-time query to aggregate existing ClickHouse data
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

---

## API-to-ClickHouse Query Mapping (High-Traffic Pages)

### LandingPageV2.vue (Highest Traffic)

| API Endpoint | ClickHouse Query | Migration Priority |
|--------------|------------------|-------------------|
| `/stats/liveservers/{game}/servers` | None (BFList cache) | N/A |
| `/stats/Players/search` | None (SQLite) | N/A |
| `/stats/GameTrends/landing-summary` | `GetSmartPredictionInsightsAsync` | HIGH |
| `/stats/GameTrends/busy-indicator` | `GetServerBusyIndicatorAsync` | HIGH |
| `/stats/LiveServers/{game}/players-online-history` | `server_online_counts` aggregation | HIGH |

### ServerDetails.vue (2nd Highest)

| API Endpoint | ClickHouse Query | Migration Priority |
|--------------|------------------|-------------------|
| `/stats/servers/{name}` | Mixed | MEDIUM |
| `/stats/servers/{name}/leaderboards` | `GetTopScoresAsync`, `GetTopKDRatiosAsync`, `GetTopKillRatesAsync`, `GetMostActivePlayersAsync` | HIGH |
| `/stats/servers/{name}/insights` | Player history from `server_online_counts` | MEDIUM |
| `/stats/servers/{name}/insights/maps` | Map analytics | MEDIUM |
| `/stats/GameTrends/busy-indicator` | Same as landing page | HIGH |

### PlayerDetails.vue

| API Endpoint | ClickHouse Query | Migration Priority |
|--------------|------------------|-------------------|
| `/stats/players/{name}` | ~~`GetAveragePingFromClickHouse`~~ | ✅ Done - `UseSqlite("GetAveragePing")` enabled |
| `/stats/players/{name}/similar` | DROPPED (similarity feature removed) | N/A |

---

## Implementation Checklist

1. ~~Get data volume estimates~~ ✅ Done
2. ~~Prioritize features by usage~~ ✅ Done (see API mapping above)
3. ~~Design update triggers and background jobs~~ ✅ Done (see collection strategy)
4. ~~Create EF Core entities~~ ✅ Done (see `api/Data/Entities/`)
5. ~~Create aggregate calculation service~~ ✅ Done (see `AggregateCalculationService`)
6. ~~Create SQLite leaderboard service~~ ✅ Done (see `SqliteLeaderboardService`)
7. ~~Create SQLite player stats service~~ ✅ Done (see `SqlitePlayerStatsService`)
8. ~~Migrate GetAveragePing~~ ✅ Done (toggle: `UseSqlite("GetAveragePing")`)
9. ~~Migrate GetPlayerServerInsights~~ ✅ Done (toggle: `UseSqlite("GetPlayerServerInsights")`)
10. ~~Complete backfill services for historical data~~ ✅ Done (`AggregateBackfillBackgroundService`, `ServerOnlineCountsBackfillBackgroundService`)
11. ~~Implement GetHeadToHeadData SQLite version~~ ✅ Done (`SqlitePlayerComparisonService.GetHeadToHeadDataAsync`)
12. ~~Create SqlitePlayerComparisonService~~ ✅ Done (full implementation)
13. ~~Create SqliteGameTrendsService~~ ✅ Done (full implementation)
14. ~~Enable all SQLite toggles in deployment~~ ✅ Done (12 toggles enabled)
15. ⏳ Add telemetry comparison between ClickHouse and SQLite
16. ⏳ Verify UI usage of highest score data in server insights
17. ⏳ Disable ClickHouse collection once parity verified
18. ⏳ Remove ClickHouse read services
19. ⏳ Decommission ClickHouse infrastructure

---

## Remaining ClickHouse Dependencies (Full Deprecation Analysis - Jan 2026)

Analysis of what ClickHouse features remain when all 12 `UseSqlite` toggles are enabled.

### Data Mapping Clarification

| ClickHouse Table | SQLite Equivalent | Notes |
|------------------|-------------------|-------|
| `player_metrics` | `PlayerObservations` + joins | 1:1 mapping. Join with `PlayerSessions`, `Servers`, `Players` to reconstruct full context. |
| `player_rounds` | `PlayerSessions` | Already migrated. |
| `server_online_counts` | `ServerOnlineCounts` | Already migrated. |
| `player_achievements_deduplicated` | **No SQLite equivalent** | Used by placement leaderboards and gamification. Needs new table design. |

### Endpoints Without SQLite Toggles

| Endpoint | Controller | ClickHouse Usage | Action |
|----------|------------|------------------|--------|
| `/stats/players/{name}/similar` | `PlayersController.GetSimilarPlayers` | `playerComparisonService.FindSimilarPlayersAsync()` | **DROP from UI** |
| `/stats/players/compare/activity-hours` | `PlayersController.ComparePlayersActivityHours` | `playerComparisonService.ComparePlayersActivityHoursAsync()` | **DROP from UI** |
| `/stats/GameTrends/current-activity` | `GameTrendsController.GetCurrentActivityStatus` | `gameTrendsService.GetCurrentActivityAsync()` | Verify if used; add toggle or drop |
| `/stats/servers/{guid}/leaderboards/placement` | `ServersV2Controller` | Direct ClickHouse query on `player_achievements_deduplicated` | **Migrate** - needs achievements table in SQLite |

### Gamification System (Entire Feature Depends on ClickHouse)

The gamification system (`Gamification/`) uses ClickHouse for:
- Achievement queries
- Badge calculations
- Kill streak tracking
- Tournament rankings

**All gamification endpoints** require ClickHouse until an achievements table is added to SQLite.

### Background Write Services

These services write to ClickHouse and are controlled by environment variables:

| Service | Env Var | Current State | Action |
|---------|---------|---------------|--------|
| Player metrics sync | `ENABLE_PLAYER_METRICS_SYNCING` | Enabled | Disable once reads migrated |
| Server online counts sync | `ENABLE_SERVER_ONLINE_COUNTS_SYNCING` | Enabled | Disable once reads migrated |
| Round syncing | `ENABLE_CLICKHOUSE_ROUND_SYNCING` | Enabled | Disable once reads migrated |

### Full Deprecation Path

#### Phase 1: Drop Features from UI (Immediate)
- [ ] Remove "Similar Players" section from `PlayerDetails.vue` (calls `/stats/players/{name}/similar`)
- [ ] Remove "Activity Hours Comparison" from `PlayerComparison.vue` (calls `/stats/players/compare/activity-hours`)

#### Phase 2: Migrate Placement Leaderboards
- [ ] Design `PlayerAchievements` SQLite table for round placements (gold/silver/bronze)
- [ ] Backfill from `player_achievements_deduplicated` ClickHouse table
- [ ] Update `ServersV2Controller` to query SQLite
- [ ] Add `UseSqlite("PlacementLeaderboards")` toggle

#### Phase 3: Decide on Gamification
- [ ] Evaluate if gamification system stays or gets redesigned
- [ ] If keeping: Design SQLite schema for achievements, badges, kill streaks
- [ ] If dropping: Remove gamification endpoints and UI

#### Phase 4: Disable ClickHouse Writes
- [ ] Set `ENABLE_PLAYER_METRICS_SYNCING=false`
- [ ] Set `ENABLE_SERVER_ONLINE_COUNTS_SYNCING=false`
- [ ] Set `ENABLE_CLICKHOUSE_ROUND_SYNCING=false`

#### Phase 5: Remove ClickHouse Code
- [ ] Delete `api/ClickHouse/*.cs` read services
- [ ] Remove ClickHouse connection configuration
- [ ] Shut down ClickHouse infrastructure
