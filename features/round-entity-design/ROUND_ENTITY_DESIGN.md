## Round entity design (SQLite as source of truth)

This document introduces a first-class `Round` concept anchored on SQLite (`PlayerSessions`) and clarifies integration with ClickHouse. It avoids assumptions that ClickHouse is authoritative and aligns with current code behavior.

### Ground truth and current state

- **Source of truth**: SQLite `PlayerSessions` in `PlayerTrackerDbContext`.
- **ClickHouse**: `player_rounds` is a derived, read-optimized store. In current code, `round_id` in ClickHouse identifies a single PlayerSession (it’s generated from session fields), not a server-wide round.
- **Round detection today**: `ServerStats/HistoricalRoundsService.cs` infers rounds by grouping sessions on `(ServerGuid, MapName)` and breaking on a gap of > 600 seconds.

### Goals

- Provide a canonical `Round` record to:
  - Replace heavy ad-hoc SQL round detection on read paths.
  - Enable consistent linking from UI, gamification, and notifications.
  - Support leaderboard/placements (top 1/2/3) and counts across time.
  - Keep ClickHouse optional for performance, not as the source of truth.

### Definitions

- **Round**: A continuous map rotation on a server. Begins when the server starts a map; ends when that map ends (or after inactivity threshold). A round encompasses all `PlayerSession` rows whose active time intersects the round interval.

### Data model (SQLite)

1) `Round` (new table)

- `RoundId` (TEXT, PK): Canonical identifier for the round (see Identifier section).
- `ServerGuid` (TEXT, FK → `GameServer.Guid`)
- `ServerName` (TEXT, denormalized for convenience)
- `MapName` (TEXT)
- `GameType` (TEXT)
- `StartTime` (DATETIME, UTC)
- `EndTime` (DATETIME, UTC, nullable while active)
- `IsActive` (BOOLEAN)
- `DurationMinutes` (INTEGER, convenience; can be computed)
- `ParticipantCount` (INTEGER, excludes bots)
- Optional: `CreatedFromEvent` (BOOLEAN) — true if created from a real-time map-change event vs heuristic.
 - `Tickets1` (INTEGER, nullable) — last observed value
 - `Tickets2` (INTEGER, nullable) — last observed value

Indexes:

- `(ServerGuid, EndTime DESC)`
- `(ServerGuid, StartTime DESC)`
- `(MapName)`
- `(IsActive)`
- Unique partial index: `(ServerGuid)` WHERE `IsActive = 1` — enforce one active round per server.
- Unique index on `(ServerGuid, StartTime, MapName)` — prevent duplicate rows for the same round.

Constraints:

- `CHECK (EndTime IS NULL OR EndTime >= StartTime)`

2) `RoundObservation` (new table)

- `Id` (INTEGER, PK)
- `RoundId` (TEXT, FK → `Round.RoundId`)
- `Timestamp` (DATETIME, UTC)
- `Tickets1` (INTEGER, nullable)
- `Tickets2` (INTEGER, nullable)
- `Team1Label` (TEXT, nullable)
- `Team2Label` (TEXT, nullable)

Indexes:

- `(RoundId, Timestamp)` to support time-series reads per round

3) `PlayerSession` (existing table – add FK)

- Add `RoundId` (TEXT, nullable, FK → `Round.RoundId`).

Indexes:

- `RoundId` (lookup sessions by round).
- `(RoundId, PlayerName)` (distinct participant counting and per-player fan-out in a round).

Notes:

- A player can have multiple sessions within a single round (disconnect/reconnect); those sessions each store the same `RoundId`.

### Identifier and boundaries

Identifier (`RoundId`):

- Deterministic hash of `(ServerGuid, MapName, RoundStartTimeNormalized)`, where `RoundStartTimeNormalized` is the canonical start for the round group (see below). A 16–20 char hex prefix of SHA-256 is sufficient.

Round start/end determination:

- **Preferred (real-time)**: On map-change events (e.g., via `ServerMapChangeNotificationHandler` or server info updates), close the previous active round for the server (set `EndTime`) and open a new `Round` with `StartTime = event timestamp`.
- **Fallback/retro**: Group `PlayerSessions` by `(ServerGuid, MapName)` using a gap > 600 seconds to start a new group. The canonical `StartTime` is the min `Session.StartTime` within the group; `EndTime` is the max `Session.LastSeenTime` (or `now` if still active). This mirrors the existing SQL in `HistoricalRoundsService`.

Normalization:

- Use UTC everywhere. Optionally round start to the nearest second/minute if necessary to ensure consistent hashing across event and heuristic paths. Persist the exact `StartTime` selected to avoid drift.

### Lifecycle and synchronization

Real-time path:

- On every BFList poll (even if zero players):
  - Ensure a single active `Round` per server/map. If the map changed, close the previous round (with `EndTime`) and open a new one.
  - Insert a `RoundObservation` capturing `Tickets1`, `Tickets2` and team labels (from `teams[0|1].label`).
- When a server map-change is detected:
  1. Close any `IsActive` round for the server (set `EndTime`, `IsActive = false`).
  2. Create a new `Round` row with `IsActive = true`.
  3. As `PlayerSession` rows start/stop, set `PlayerSession.RoundId` for sessions whose active window intersects the round.
    - Intersection predicate: `Session.StartTime < COALESCE(Round.EndTime, now)` AND `Session.LastSeenTime > Round.StartTime`.
  4. Periodically update `ParticipantCount` from distinct non-bot `PlayerSession.PlayerName` linked to the round.

Backfill path (idempotent):

- For a given time range per server:
  1. Reproduce the grouping logic (gap > 600s) to generate `(ServerGuid, MapName, StartTime, EndTime)` groups.
  2. For each group, compute `RoundId`, upsert `Round`.
  3. Set `PlayerSession.RoundId` for all intersecting sessions (skip those already set correctly).
    - Use the same intersection predicate as real-time: `Session.StartTime < COALESCE(Round.EndTime, now)` AND `Session.LastSeenTime > Round.StartTime`.
  4. Update `ParticipantCount`, `DurationMinutes`.

### Read path changes

- Replace `HistoricalRoundsService.BuildRoundsQuery`’s derived SQL with a simple query of the `Round` table plus filters. This eliminates heavy windowing every request.
- Use `PlayerSession` filtered by `RoundId` when you need to fetch per-round player lists, session details, or compute metrics from `PlayerObservation`.

### Gamification and placements (1/2/3 finishes)

Authoritative computation in SQLite (no ClickHouse dependency):

- For a given `RoundId`, derive leaderboard by the app’s rule: highest score achieved during the round. Using existing tables:

  - Gather all `SessionId` for `RoundId` from `RoundSession`.
  - Join `PlayerObservation` → `PlayerSession` and compute `MAX(Score)` per `PlayerName` over those sessions within `[StartTime, EndTime]`.
  - Rank players by `MAX(Score)`; top 3 are placements.

Example (conceptual) SQL:

```sql
SELECT ps.PlayerName, MAX(po.Score) AS max_score
FROM PlayerObservation po
JOIN PlayerSession ps ON po.SessionId = ps.SessionId
WHERE ps.RoundId = @roundId
GROUP BY ps.PlayerName
ORDER BY max_score DESC
LIMIT 3;
```

- To count total 1st/2nd/3rd finishes per player across time, compute for each `RoundId` and aggregate. This can be precomputed in a background job and cached into a small table if needed for UI speed.

Optional performance integration with ClickHouse:

- Add a new column `group_round_id` to `player_rounds` representing the canonical `RoundId` (not the session-scoped `round_id`). Populate it when syncing sessions to ClickHouse by using `PlayerSession.RoundId`. This enables fast CH ranking queries by `group_round_id` while keeping SQLite authoritative.
- This is an optimization, not a requirement. If omitted, SQLite-only computation remains correct.

### Zero-player tracking

- Previously the system only recorded data when at least one player was online. With `Round` + `RoundObservation`, every BFList poll records the server’s map, game type, tickets, and team labels, enabling accurate round boundaries and tickets time-series even with zero players.

### Why `RoundId` on `PlayerSession` instead of a cross-reference table?

- Current session model is per-map and is closed on map change or inactivity, so a session cannot span multiple rounds. The relationship is naturally one round to many sessions.
- Simpler schema and queries: avoid an extra join table; fetch sessions with `WHERE RoundId = @id`.
- Idempotent backfill and real-time paths: just set or update a single FK on `PlayerSession`.
- Still supports multiple sessions per player in a round (each session carries the same `RoundId`).
- Prefer a join table only if sessions may be allowed to span multiple rounds in the future or if you need many-to-many for another reason.

### Decisions and defaults

- Inactivity threshold for round boundary: 600s default; configurable per game.
- Minimum round quality for leaderboards: `ParticipantCount >= 4` and `DurationMinutes >= 5`.
- Tie-breaking for placements: higher kills, then fewer deaths, then earliest achievement time.
- Persist `ServerName` denorm on `Round`: yes, for convenience and stability in historical views.
- `RoundId` length: 20 hex characters (SHA-256 prefix); collision risk negligible.
- New round on every map-change event, even if `MapName` repeats consecutively.

### Edge cases and idempotency

- Duplicate or out-of-order map-change events: close existing active round if present and open the next by computed `RoundId`; operations are idempotent.
- Server clock skew or invalid timestamps: prefer ingestion time when event timestamps are clearly invalid (e.g., in the future by > 5 minutes).
- Long inactivity without explicit map-change: inactivity threshold closes the prior round; a new round opens when activity resumes or a map-change event arrives.
- Multiple reconnects by the same player: multiple `PlayerSession` rows link to the same `Round` via `RoundSession` without duplication.

### Open questions to finalize

- Minimum round quality thresholds (e.g., `ParticipantCount >= N`, `DurationMinutes >= M`) for inclusion in leaderboards/achievements.
- Tie-breaking for placements (e.g., higher kills, earlier achievement time, fewer deaths).
- Inactivity threshold value (currently 600s in code) — keep or adjust per game.
- Whether to persist `ServerName` denorm vs always join `GameServer`.

### Rollout plan

1) Add `Round` and `RoundSession` entities and migration (unique + covering indexes as above).
  - Include partial unique index to allow only one active round per `ServerGuid` and a `CHECK` on `EndTime >= StartTime`.
2) Implement backfill job using the existing grouping logic to populate tables.
  - Batch by server and time range; safe to re-run (idempotent upserts and links).
  - Use the intersection predicate to link sessions to rounds.
3) Update `HistoricalRoundsService` to read from `Round` and drop heavy windowing from hot paths.
4) Implement placements computation (SQLite), optionally add a small cache table or CH `group_round_id` for high-traffic endpoints.
5) Wire real-time updates from map-change notifications to open/close rounds and maintain `RoundSession` links.
  - Handle duplicate/out-of-order events idempotently; ensure at most one active round per server.

This approach keeps SQLite as the definitive system of record, introduces stable round identity, and makes round-centric features (leaderboards, achievements, notifications) simple and fast.


