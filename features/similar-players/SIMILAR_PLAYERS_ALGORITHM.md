# Similar Players Algorithm (ClickHouse-era)

This document preserves the original Similar Players analysis behavior so it can be reimplemented on SQLite later.

## Goal

Given a target player, produce a list of similar players (and optional alias candidates) with:
- A similarity score in [0, 1]
- Human-readable reasons explaining why each player matched
- Comparable stats for side-by-side UI display

## API Contract (Legacy)

### Request
```
GET /stats/players/{playerName}/similar?mode=default|aliasdetection
```

### Response Shape
```
{
  "targetPlayer": "string",
  "targetPlayerStats": PlayerComparisonStats,
  "similarPlayers": SimilarPlayer[]
}
```

### PlayerComparisonStats (used for target + each similar player)
- `playerName`: string
- `totalKills`: number
- `totalDeaths`: number
- `totalPlayTimeMinutes`: number
- `killDeathRatio`: number
- `killsPerMinute`: number
- `favoriteServerName`: string
- `favoriteServerPlayTimeMinutes`: number
- `gameIds`: string[]
- `temporalOverlapMinutes`: number
- `typicalOnlineHours`: number[] (UTC hours 0-23)
- `serverPings`: Record<string, number> (server name -> avg ping)
- `mapDominanceScores`: Record<string, number> (map name -> score/weight)
- `temporalNonOverlapScore`: number (0-1)

### SimilarPlayer
Extends `PlayerComparisonStats` and adds:
- `similarityScore`: number (0-1)
- `similarityReasons`: string[]

## Comparison Dimensions

The similarity score was composed from multiple dimensions:
- **Performance**: K/D ratio and kills per minute proximity.
- **Time Played**: total playtime similarity.
- **Server Affinity**: overlap between favorite servers / common servers.
- **Map Affinity**: overlap between map dominance arrays.
- **Time-of-Day**: typical online hour overlap.
- **Temporal Non-Overlap** (alias mode): lack of overlap can be a positive signal.

Each dimension contributed a weighted sub-score; the sum was normalized to [0, 1].

## Suggested SQLite Reimplementation Notes

The UI expects `PlayerComparisonStats` for both the target and each similar player, and uses:
- `serverPings` to show shared servers and latency comparisons.
- `mapDominanceScores` for common map overlap.
- `typicalOnlineHours` for time-of-day overlap.
- `temporalNonOverlapScore` to surface alias candidates.

SQLite sources to derive these:
- `PlayerSessions` for totals, time windows, server and map affinity.
- `PlayerObservations` for temporal activity, ping averages, and per-hour activity.
- Precomputed aggregate tables (e.g., `PlayerServerStats`, `PlayerMapStats`) for faster lookups.

## UI Expectations (Legacy)

The UI rendered:
- Similarity % badge (score * 100).
- Reasons list for each match.
- "Common servers" and "Common maps" counts derived from overlap of the maps/pings dictionaries.
- Optional "alias detection" mode (high non-overlap + strong stat similarity).

If/when reintroducing the endpoint, keep the response schema above to avoid UI churn.
