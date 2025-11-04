# Tournament Team Rankings System - Complete Analysis

## Data Structure Understanding

### Entity Relationships
```
TournamentMatch (1:N) TournamentMatchMap (1:N) TournamentMatchResult
    |                                               |
    ├─ Team1Id                                      ├─ Team1Id
    ├─ Team2Id                                      ├─ Team2Id
    ├─ Week                                         ├─ WinningTeamId
    └─ Maps (2+ per match)                          ├─ Team1Tickets
                                                    ├─ Team2Tickets
                                                    └─ RoundId (optional)
```

### Match Composition
- **1 Match** = multiple maps (typically 2)
- **1 Map** = 2 rounds (one per team perspective) = 2 TournamentMatchResult rows
- **Example:** Match with 2 maps = 4 TournamentMatchResult rows

### Match vs. Round in Context
- **Match:** Entire competition between two teams across all maps
  - Match-level outcome: determined by aggregating all round tickets
  - Match winner: team with most total tickets across all rounds
  - Match tie: both teams have equal total tickets
  
- **Round:** Single map iteration
  - Round-level outcome: WinningTeamId, Team1Tickets, Team2Tickets
  - Ticket differential: Team1Tickets - Team2Tickets

## Current Database Model (TournamentTeamRanking)

**Location:** `junie-des-1942stats/PlayerTracking/PlayerTrackerDbContext.cs:927`

**Currently Stored Fields:**
- `Id` - Primary key
- `TournamentId` - Foreign key
- `TeamId` - Foreign key
- `Week` - Optional week identifier (null = cumulative)
- `RoundsWon` - int (round wins)
- `RoundsTied` - int (round ties)
- `RoundsLost` - int (round losses)
- `TicketDifferential` - int (aggregate: tickets_for - tickets_against)
- `Rank` - int (calculated position)
- `UpdatedAt` - Instant (timestamp)

**Missing Fields (To Be Added):**
- `MatchesPlayed` - int (total matches a team participated in)
- `Victories` - int (match-level wins)
- `Ties` - int (match-level ties)
- `Losses` - int (match-level losses)
- `TicketsFor` - int (total tickets scored across all rounds)
- `TicketsAgainst` - int (total tickets allowed across all rounds)
- `Points` - int (= RoundsWon, primary ranking metric)

## Current Ranking Algorithm

**Location:** `junie-des-1942stats/Services/Tournament/TeamRankingCalculator.cs`

### CalculateTeamStatistics() Method (Current)
- Iterates through `TournamentMatchResult` rows (rounds)
- For each round involving the team:
  - Count if win (WinningTeamId == TeamId)
  - Count if tie (equal tickets)
  - Count if loss (opponent won)
  - Accumulate ticket differential
- Returns tuple: `(RoundsWon, RoundsTied, RoundsLost, TicketDifferential)`

### Current Sort Order
```csharp
rankedTeams
    .OrderByDescending(kvp => kvp.Value.RoundsWon)        // PRIMARY
    .ThenByDescending(kvp => kvp.Value.RoundsTied)        // TIER 1
    .ThenByDescending(kvp => kvp.Value.TicketDifferential) // TIER 2
```

## Implementation Requirements

### 1. New Ranking Calculation Algorithm

**Points Calculation:**
- `Points = RoundsWon` (1 point per round won, 0 points for ties/losses)

**Match-Level Statistics:**
To calculate `Victories`, `Ties`, `Losses`, we need to:
1. Group `TournamentMatchResult` rows by `(MatchId, MapId)` 
2. For each match:
   - Sum total tickets for team across all rounds in that match
   - Compare against opponent's total tickets
   - Count as: victory (higher tickets), tie (equal), or loss (lower tickets)

**Ticket Totals:**
- `TicketsFor = sum of Team1Tickets or Team2Tickets` where team is Team1 or Team2
- `TicketsAgainst = sum of Team2Tickets or Team1Tickets` (opponent's tickets)

### 2. New Sort Order (Per Documented Spec)

```csharp
rankedTeams
    .OrderByDescending(kvp => kvp.Value.Points)           // PRIMARY: RoundsWon
    .ThenByDescending(kvp => kvp.Value.RoundsTied)        // TIER 1a: Tied rounds (preferred over lost)
    .ThenByDescending(kvp => kvp.Value.RoundsLost)        // TIER 1b: Lost rounds
    .ThenByDescending(kvp => kvp.Value.TicketDifferential) // TIER 2: Ticket differential
```

Note: Current implementation doesn't explicitly sort by RoundsLost, which could be an issue.

### 3. API Response Updates

**PublicTeamRankingResponse** should include:
- Rank, TeamId, TeamName
- MatchesPlayed, Victories, Ties, Losses
- RoundsWon, RoundsTied, RoundsLost
- TicketsFor, TicketsAgainst, TicketDifferential
- Points

**TournamentTeamRankingResponse** should include same as above + Week field

## Implementation Plan

1. **Add fields to TournamentTeamRanking model**
   - Add 7 new int properties: MatchesPlayed, Victories, Ties, Losses, TicketsFor, TicketsAgainst, Points

2. **Update CalculateTeamStatistics() method**
   - Restructure to calculate match-level and ticket-level stats
   - Need to group rounds into matches for match-level aggregation
   - Return all new statistics in a more complex tuple or new type

3. **Update sort order in CalculateRankingsAsync()**
   - Change primary sort to Points (same as RoundsWon)
   - Add explicit sort by RoundsLost after RoundsTied
   - Add explicit sort by TicketDifferential as final tiebreaker

4. **Update API DTOs**
   - Add new fields to response classes
   - Consider backward compatibility (keep RoundsWon/Tied/Lost or replace?)

5. **Create EF Core migration**
   - Add 7 new nullable columns to TournamentTeamRankings table
   - Make nullable initially, then populate via recalculation

6. **Test ranking calculation**
   - Verify match-level statistics match expectations
   - Verify sort order produces correct rankings
   - Verify ticket totals are accurate
