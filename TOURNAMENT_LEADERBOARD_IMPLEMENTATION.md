# Tournament Leaderboard Implementation Plan

**Status**: Phase 1 & 2 COMPLETE - Database schema and core services implemented ✓

## Overview
Build a team ranking/leaderboard system for tournaments that calculates standings based on match results using a hierarchical ranking algorithm.

## Session Summary (Nov 2, 2025)

### What We Accomplished:
1. **Database Schema**: Created and migrated TournamentMatchResult and TournamentTeamRanking tables
2. **Missing Entities**: Discovered and created missing TournamentMatch and TournamentMatchMap entity definitions
3. **Core Services**: Implemented 3 complete service classes with interfaces:
   - TeamRankingCalculator: Hierarchical ranking with recalculation
   - TeamMappingService: Auto-detection via player roster matching
   - TournamentMatchResultService: Full CRUD with admin override
4. **Code Quality**: All services compile without errors, include comprehensive logging, error handling, and async/await patterns

### Next Steps:
- Phase 3: Integrate services into API endpoints (UpdateMatchMap, admin overrides, public leaderboard)
- Phase 4: Create background service for async ranking recalculation
- Phase 5: Add unit and integration tests

## Key Design Decisions

### 1. Ranking Algorithm
**Hierarchical Ranking Rules** (applied in order):
1. **Primary Metric**: Points (1 point per Round Won, 0 for ties/losses)
2. **Tier 1 Tiebreaker**: Rounds Tied vs Rounds Lost (tied rounds worth more than lost)
3. **Tier 2 Tiebreaker**: Ticket Differential (Tickets For - Tickets Against)
4. **Tier 3 Tiebreaker**: Administrative Penalties (TODO: Define with organizers - deferred for now)

### 2. Data Granularity
- Store results at the **finest granularity**: one record per completed round/map
- This allows flexible aggregation (by week, by tournament, cumulative, etc.)
- Schema: `TournamentMatchResult` table

### 3. Team Mapping Strategy
When organizer links a Round to a TournamentMatchMap:
1. **Auto-detect** tournament teams using player roster matching:
   - Extract players from `PlayerSession` records for that round (grouped by team label)
   - Check if any tournament team roster players appear in Team1 or Team2
   - If found, auto-map the tournament team to that round team
2. **Warn organizer** if auto-detection failed (return warning in API response)
3. **Admin override endpoint** to manually correct team assignments
4. **Background processing** for ranking calculations (can be deferred)

### 4. Rankings Storage Strategy
- Store **week-specific statistics** in `TournamentTeamRanking` table
- Can aggregate upward for:
  - Cumulative tournament standings (all weeks combined)
  - Weekly standings (single week filtered)
  - Intermediate aggregations as needed

### 5. Ranking Recalculation Trigger
- **Synchronous** in API: Create `TournamentMatchResult` and attempt auto-mapping (with warning)
- **Asynchronous** in background: Recalculate all affected `TournamentTeamRanking` records
- Background service monitors for completed rounds and triggers recalculation

---

## Database Schema

### New Entities

#### TournamentMatchResult
Fine-grained storage of individual round results.

```
- Id (PK, int)
- TournamentId (FK to Tournament)
- MatchId (FK to TournamentMatch)
- MapId (FK to TournamentMatchMap)
- RoundId (FK to Round)
- Week (string, denormalized from TournamentMatch.Week)

- Team1Id (FK to TournamentTeam) - Tournament team identified as Team1
- Team2Id (FK to TournamentTeam) - Tournament team identified as Team2
- WinningTeamId (FK to TournamentTeam) - Which team won this round

- Team1Tickets (int, from Round.Tickets1)
- Team2Tickets (int, from Round.Tickets2)

- CreatedAt (Instant)
- UpdatedAt (Instant)
```

**Constraints**:
- Foreign keys ensure referential integrity
- Index on (TournamentId, Week) for efficient filtering
- Index on (MatchId) for lookups

---

#### TournamentTeamRanking
Cumulative statistics per team per week (allows aggregation).

```
- Id (PK, int)
- TournamentId (FK to Tournament)
- TeamId (FK to TournamentTeam)
- Week (string, nullable - NULL means cumulative across all weeks)

- RoundsWon (int) - Total rounds won by this team
- RoundsTied (int) - Total rounds tied (should be 0 if rule is W/L only)
- RoundsLost (int) - Total rounds lost
- TicketDifferential (int) - Sum of (team tickets - opponent tickets)
- Rank (int) - Calculated position in standings

- UpdatedAt (Instant)
```

**Constraints**:
- Composite unique index on (TournamentId, TeamId, Week) - one record per team per week
- Index on (TournamentId, Week) for leaderboard queries

---

## Implementation Tasks

### Phase 1: Database Schema & Migrations
- [ ] Create `TournamentMatchResult` entity in PlayerTrackerDbContext.cs
- [ ] Create `TournamentTeamRanking` entity in PlayerTrackerDbContext.cs
- [ ] Create EF migration: `AddTournamentLeaderboard`
- [ ] Apply migration to database

### Phase 2: Core Services

#### TeamRankingCalculator Service
- [ ] Create `ITeamRankingCalculator` interface
- [ ] Implement `TeamRankingCalculator` service
  - [ ] `CalculateRankingsAsync(tournamentId, week?)` - Calculate standings
  - [ ] `RecalculateAllRankingsAsync(tournamentId)` - Full recalculation
  - Implement hierarchical ranking logic
  - Return sorted list of rankings with calculated positions

#### TeamMappingService
- [ ] Create `ITeamMappingService` interface
- [ ] Implement `TeamMappingService`
  - [ ] `DetectTeamMappingAsync(roundId, match)` - Auto-detect tournament teams
  - [ ] Returns: (Team1Id, Team2Id, MappingConfidence, WarningMessage)
  - Use player roster matching from PlayerSession records

#### TournamentMatchResultService
- [ ] Create `ITournamentMatchResultService` interface
- [ ] Implement `TournamentMatchResultService`
  - [ ] `CreateOrUpdateMatchResultAsync(...)` - Record match result
  - [ ] `GetMatchResultAsync(resultId)` - Retrieve result
  - [ ] `OverrideTeamMappingAsync(resultId, team1Id, team2Id)` - Admin override
  - [ ] `DeleteMatchResultAsync(resultId)` - Clean up

### Phase 3: Background Processing

#### TournamentRankingRecalculationService
- [ ] Create background service
  - [ ] Monitor for completed `TournamentMatchResult` records
  - [ ] Trigger `TeamRankingCalculator.RecalculateAllRankingsAsync(tournamentId)`
  - [ ] Ensure idempotent (safe to run multiple times)
  - [ ] Optional: Use queue/event pattern for scalability

### Phase 4: API Endpoints

#### Enhanced UpdateMatchMap Endpoint
In `AdminTournamentController.cs`:
- [ ] Modify existing `UpdateMatchMap` to:
  - [ ] Call `TeamMappingService.DetectTeamMappingAsync(...)`
  - [ ] Create `TournamentMatchResult` record
  - [ ] Return warning if auto-mapping failed
  - [ ] Trigger async ranking recalculation

#### New Admin Endpoints
In `AdminTournamentController.cs`:
- [ ] `PUT {tournamentId}/matches/{matchId}/maps/{mapId}/team-mapping` - Override team assignment
  - Request: { Team1Id, Team2Id }
  - Response: Updated TournamentMatchResult
  - [ ] Updates TournamentMatchResult
  - [ ] Triggers ranking recalculation

- [ ] `DELETE {tournamentId}/matches/{matchId}/maps/{mapId}/result` - Delete result
  - Triggers ranking recalculation

- [ ] `GET {tournamentId}/match-results` - List all results for tournament
  - Filters, sorting, pagination

#### New Public Endpoints
In `PublicTournamentController.cs`:
- [ ] `GET /stats/tournaments/{idOrName}/leaderboard` - Get tournament leaderboard
  - Query params: `week` (optional), `format` (json/csv)
  - Returns:
    - If week specified: weekly standings for that week
    - If no week: cumulative standings across all weeks
    - Include: Rank, TeamName, RoundsWon, RoundsTied, RoundsLost, TicketDifferential, Points

- [ ] `GET /stats/tournaments/{idOrName}/leaderboard/history` - Get standings by week
  - Returns: Array of weekly leaderboards
  - Include week label and all standings for that week

---

## Response Models (DTOs)

### LeaderboardResponse
```csharp
public class LeaderboardResponse
{
    public int TournamentId { get; set; }
    public string TournamentName { get; set; }
    public string? Week { get; set; } // null = cumulative
    public List<LeaderboardEntry> Standings { get; set; }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public int TeamId { get; set; }
    public string TeamName { get; set; }
    public int RoundsWon { get; set; }
    public int RoundsTied { get; set; }
    public int RoundsLost { get; set; }
    public int TicketDifferential { get; set; }
    public int Points { get; set; } // RoundsWon (for display)
}
```

### MatchResultResponse
```csharp
public class MatchResultResponse
{
    public int Id { get; set; }
    public int MatchId { get; set; }
    public int MapId { get; set; }
    public string RoundId { get; set; }
    public string? Week { get; set; }

    public int Team1Id { get; set; }
    public string Team1Name { get; set; }
    public int Team2Id { get; set; }
    public string Team2Name { get; set; }

    public int WinningTeamId { get; set; }
    public string WinningTeamName { get; set; }

    public int Team1Tickets { get; set; }
    public int Team2Tickets { get; set; }
}
```

### UpdateMatchMapResponse (enhanced)
```csharp
public class UpdateMatchMapResponse
{
    public TournamentMatchMapResponse Map { get; set; }
    public MatchResultResponse MatchResult { get; set; }
    public string? WarningMessage { get; set; } // null if auto-mapping succeeded
}
```

---

## Implementation Notes

### Player Roster Matching Algorithm
1. Query `PlayerSession` records for the round
2. Group by `CurrentTeamLabel` to get Team1 and Team2 rosters
3. For each tournament team:
   - Check if any team player names appear in Team1 roster
   - Check if any team player names appear in Team2 roster
   - Record "confidence" (e.g., "3 players matched")
4. If clear match: auto-assign
5. If ambiguous/no match: return warning

### Ranking Calculation Details
- **Group** `TournamentMatchResult` by (TournamentId, TeamId, Week)
- **Aggregate**:
  - `RoundsWon` = Count where WinningTeamId = TeamId
  - `RoundsTied` = Count where tie result (Tickets1 == Tickets2?)
  - `RoundsLost` = Count where WinningTeamId != TeamId
  - `TicketDifferential` = Sum of (Team's Tickets - Opponent's Tickets)
- **Sort** by: RoundsWon DESC → RoundsTied DESC → TicketDifferential DESC → AdminPenalty
- **Assign** rank based on sorted position

### Error Handling
- Invalid team IDs: 400 Bad Request
- Round not found: 404 Not Found
- Tournament not found: 404 Not Found
- Authorization: 401 Unauthorized (admin endpoints)

---

## Testing Considerations

### Unit Tests
- [ ] TeamRankingCalculator with various scenarios
- [ ] TeamMappingService with roster matching edge cases
- [ ] Hierarchical tiebreaker logic

### Integration Tests
- [ ] Full flow: Create match → Link round → Auto-map → Calculate rankings
- [ ] Admin override workflow
- [ ] Public leaderboard retrieval

### Edge Cases
- [ ] Rounds with tied tickets (both teams same tickets)
- [ ] Teams with no matches
- [ ] Multiple weeks in tournament
- [ ] Partial match completion (some maps done, some pending)

---

## Deferred Items (Future)

1. **Administrative Tiebreaker (Tier 3)**
   - Determine data structure with organizers
   - Add penalty_points field to `TournamentTeamRanking`
   - Create admin UI to manage penalties

2. **Performance Optimizations**
   - Cache leaderboard results
   - Batch ranking calculations
   - Event-driven architecture for background service

3. **Advanced Features**
   - Export leaderboard to CSV/Excel
   - Historical snapshots of standings
   - Strength of schedule metrics
   - Head-to-head records

---

## Progress Tracking

### Phase 1: Database Schema & Migrations ✓ COMPLETE
- [x] Design TournamentMatchResult schema
- [x] Design TournamentTeamRanking schema
- [x] Added DbSet properties to PlayerTrackerDbContext
- [x] Create TournamentMatchResult entity class definition
- [x] Create TournamentTeamRanking entity class definition
- [x] Create TournamentMatch and TournamentMatchMap entity definitions (were missing)
- [x] Create database migration `20251101233044_AddTournamentLeaderboard`
- [x] Apply migration to database successfully

### Phase 2: Core Services ✓ COMPLETE
- [x] Create TeamRankingCalculator service (ITeamRankingCalculator interface + implementation)
  - [x] Implement CalculateRankingsAsync(tournamentId, week?)
  - [x] Implement RecalculateAllRankingsAsync(tournamentId)
  - [x] Implement hierarchical ranking logic (W > T > L > Differential)
  - [x] Comprehensive error handling and logging
- [x] Create TeamMappingService (ITeamMappingService interface + implementation)
  - [x] Implement DetectTeamMappingAsync(roundId, tournamentId)
  - [x] Implement player roster matching algorithm (simple contains matching)
  - [x] Returns warnings when auto-detection fails
- [x] Create TournamentMatchResultService (ITournamentMatchResultService interface + implementation)
  - [x] Implement CreateOrUpdateMatchResultAsync(...)
  - [x] Implement GetMatchResultAsync(resultId)
  - [x] Implement OverrideTeamMappingAsync(resultId, team1Id, team2Id)
  - [x] Implement DeleteMatchResultAsync(resultId)
  - [x] Implement GetMatchResultsAsync(...) with pagination

### Phase 3: API Endpoint Modifications
- [ ] Modify UpdateMatchMap endpoint in AdminTournamentController.cs
  - [ ] Integrate TeamMappingService for auto-detection
  - [ ] Create TournamentMatchResult record
  - [ ] Return warning if auto-mapping failed
  - [ ] Trigger async ranking recalculation
- [ ] Add admin endpoints in AdminTournamentController.cs
  - [ ] PUT {tournamentId}/matches/{matchId}/maps/{mapId}/team-mapping
  - [ ] DELETE {tournamentId}/matches/{matchId}/maps/{mapId}/result
  - [ ] GET {tournamentId}/match-results
- [ ] Add public endpoints in PublicTournamentController.cs
  - [ ] GET /stats/tournaments/{idOrName}/leaderboard
  - [ ] GET /stats/tournaments/{idOrName}/leaderboard/history

### Phase 4: Background Processing
- [ ] Create TournamentRankingRecalculationService
  - [ ] Monitor for completed TournamentMatchResult records
  - [ ] Trigger ranking recalculation
  - [ ] Ensure idempotent operation
  - [ ] Register in dependency injection

### Phase 5: Testing
- [ ] Unit tests for TeamRankingCalculator
- [ ] Unit tests for TeamMappingService
- [ ] Integration tests for full workflow
- [ ] Edge case testing

### Completed ✓
- [x] Planning and design (comprehensive conversation with user on ranking rules, data granularity, team mapping strategy)
- [x] Design TournamentMatchResult schema (fine-grained, one per round/map)
- [x] Design TournamentTeamRanking schema (week-specific with composite unique index)
- [x] Added DbSet properties to PlayerTrackerDbContext (TournamentMatchResults, TournamentTeamRankings)
- [x] Created comprehensive implementation plan document (TOURNAMENT_LEADERBOARD_IMPLEMENTATION.md)
- [x] **Phase 1: Database Schema & Migrations**
  - [x] Created entity definitions for all 6 entities (including missing TournamentMatch/TournamentMatchMap)
  - [x] Generated and applied database migration successfully
  - [x] Verified database tables created with proper indexes
- [x] **Phase 2: Core Services Implementation**
  - [x] TeamRankingCalculator: Full hierarchical ranking algorithm with recalculation
  - [x] TeamMappingService: Player roster matching with auto-detection
  - [x] TournamentMatchResultService: CRUD operations with team override capability
  - [x] All services compile without errors, ready for integration

---

## Files to Modify/Create

### Existing Files - Modified ✓
- `junie-des-1942stats/PlayerTracking/PlayerTrackerDbContext.cs` ✓ MODIFIED
  - Added TournamentMatchResult entity class definition
  - Added TournamentTeamRanking entity class definition
  - Added TournamentMatch and TournamentMatchMap entity class definitions (were missing)
  - Added comprehensive EF Core configuration for all entities with proper relationships and indexes
  - Added DbSet properties for the new entities
- `junie-des-1942stats/Controllers/AdminTournamentController.cs` - TODO: Update UpdateMatchMap, add admin endpoints
- `junie-des-1942stats/Controllers/PublicTournamentController.cs` - TODO: Add leaderboard endpoint

### New Files Created ✓
- `junie-des-1942stats/Services/Tournament/ITeamRankingCalculator.cs` ✓ CREATED
- `junie-des-1942stats/Services/Tournament/TeamRankingCalculator.cs` ✓ CREATED
- `junie-des-1942stats/Services/Tournament/ITeamMappingService.cs` ✓ CREATED
- `junie-des-1942stats/Services/Tournament/TeamMappingService.cs` ✓ CREATED
- `junie-des-1942stats/Services/Tournament/ITournamentMatchResultService.cs` ✓ CREATED
- `junie-des-1942stats/Services/Tournament/TournamentMatchResultService.cs` ✓ CREATED
- `junie-des-1942stats/StatsCollectors/TournamentRankingRecalculationService.cs` - TODO: Create background service
- `junie-des-1942stats/Migrations/20251101233044_AddTournamentLeaderboard.cs` ✓ CREATED
- `junie-des-1942stats/Migrations/20251101233044_AddTournamentLeaderboard.Designer.cs` ✓ AUTO-GENERATED

---

## Resume Instructions

If resuming this work:
1. Check "Progress Tracking" section to see what's done
2. Review this document for design context
3. Continue from the next task in the checklist
4. Update progress as you complete items
