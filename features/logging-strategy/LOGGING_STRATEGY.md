# Tournament Leaderboard - Logging Strategy

## Overview
Both `TeamMappingService` and `TeamRankingCalculator` are instrumented with comprehensive structured logging to provide full visibility into decision-making flows. This document explains the logging approach to enable efficient debugging and auditing in production.

## Logging Design Principles

1. **Structured Logging**: All logs use key=value format with pipe delimiters for easy parsing by OTEL/Serilog
2. **Contextual Information**: Every log includes relevant identifiers (RoundId, TournamentId, TeamId, Week) for correlation
3. **Decision Tracking**: Logs capture the "why" behind each decision, not just the "what"
4. **Hierarchical Levels**: Information (happy path), Debug (verbose details), Warning (fallbacks), Error (failures)
5. **No PII**: Only IDs and aggregates are logged, never player names in non-matching contexts

## TeamMappingService Logging Flow

### Starting Detection
```
Starting team mapping detection | RoundId=xxx TournamentId=yyy
```
Entry point with context identifiers.

### Data Loading Phase
```
Round loaded | RoundId=xxx Team1Label=Team A Team2Label=Team B SessionCount=20
Tournament teams loaded | TournamentId=yyy TeamCount=3
Round teams parsed | Team1Players=10 Team2Players=10
```
Confirms data availability and structure.

### Player Matching Phase
```
Tournament team evaluated | TeamId=1 TeamName=ClanX Team1Matches=5 Team2Matches=1
  MatchedPlayers1=Player1, Player2, Player3 MatchedPlayers2=PlayerX
```
Detailed breakdown of matching logic for each tournament team.

**Example log chain**:
- ClanX: 5 players in Team1, 1 in Team2 → preference for Team1
- ClanY: 2 players in Team1, 7 in Team2 → preference for Team2
- ClanZ: 0 players in both → skipped

### Decision Phase
```
Viable teams after filtering | ViableTeamCount=2 MinRequired=2
Best match for RoundTeam1 selected | TournamentTeamId=1 Team1Matches=5 Team2Matches=1 Confidence=83%
Best match for RoundTeam2 selected | TournamentTeamId=2 Team1Matches=2 Team2Matches=7 Confidence=78%
```
Shows the confidence metric (matches for chosen team / total matches).

### Success/Failure Outcome
```
Team mapping detection SUCCESS | RoundId=xxx TournamentTeam1Id=1 -> RoundTeam1 TournamentTeam2Id=2 -> RoundTeam2
```
or
```
Team mapping detection failed | Reason=Only one tournament team matched players - need at least two teams
```

## TeamRankingCalculator Logging Flow

### Ranking Calculation Phase
```
Starting ranking calculation | TournamentId=1 Week=Week1
Match results loaded | TournamentId=1 Week=Week1 ResultCount=12
Unique teams identified | TournamentId=1 Week=Week1 TeamCount=2
```
Initial load and cardinality check.

### Per-Team Statistics
```
Team statistics calculated | TournamentId=1 Week=Week1 TeamId=1
  RoundsWon=6 RoundsTied=2 RoundsLost=4 TicketDiff=+342
Team statistics calculated | TournamentId=1 Week=Week1 TeamId=2
  RoundsWon=6 RoundsTied=2 RoundsLost=4 TicketDiff=+125
```
Individual team aggregations - shows why ranking order changes (identical W-T-L but different ticket differential).

### Sorting & Ranking
```
Teams sorted by ranking criteria | TournamentId=1 Week=Week1
  Criteria=(RoundsWon > RoundsTied > TicketDifferential)
Team ranking assigned | TournamentId=1 Week=Week1 TeamId=1 Rank=1 W-T-L=6-2-4 TicketDiff=+342
Team ranking assigned | TournamentId=1 Week=Week1 TeamId=2 Rank=2 W-T-L=6-2-4 TicketDiff=+125
```
Shows tiebreaker logic in action - why Team1 ranks above Team2 despite same W-T-L.

### Completion
```
Ranking calculation completed successfully | TournamentId=1 Week=Week1 RankingCount=2
```

### Full Recalculation
```
Starting full ranking recalculation | TournamentId=1
Distinct weeks identified for recalculation | TournamentId=1 WeekCount=3 Weeks=Week1, Week2, cumulative
Recalculating rankings for week | TournamentId=1 Week=Week1
Deleting old rankings | TournamentId=1 Week=Week1 OldRankingCount=2
Rankings persisted to database | TournamentId=1 Week=Week1 NewRankingCount=2 TotalUpdated=2
...
Full ranking recalculation completed successfully | TournamentId=1 TotalRankingsUpdated=6
```
Tracks the full recalculation across all weeks.

## Log Correlation with OTEL

The logs are structured to work seamlessly with OTEL:

1. **Fixed Field Names**: Using `|` delimiters and `Key=Value` format
2. **Request Correlation**: All logs include primary identifiers:
   - `RoundId` - uniquely identifies a round
   - `TournamentId` - uniquely identifies a tournament
   - `TeamId` - uniquely identifies a team within tournament
   - `Week` - week-specific calculations
3. **Structured Extractors**: OTEL can easily extract these for correlation:
   ```
   span.SetAttribute("tournament.id", 1)
   span.SetAttribute("round.id", "abc123")
   span.SetAttribute("team.id", 5)
   ```

## Usage in Production

### Debugging Team Mapping Issues
If a team mapping fails:
1. Search logs for `Team mapping detection failed`
2. Look at the "Reason" field to understand why
3. Trace backwards through the `Tournament team evaluated` logs to see which teams had matches
4. Examine player names in `Round player rosters` if needed

Example investigation:
```
RoundId=abc123: detected NO matches
→ Check "Tournament teams loaded" - are teams configured?
→ Check "Round teams parsed" - are players in the round?
→ Check "Tournament team evaluated" logs - which players matched?
```

### Debugging Ranking Issues
If a ranking seems incorrect:
1. Search for `Starting ranking calculation` with relevant TournamentId/Week
2. Review the `Team statistics calculated` logs to verify W-T-L counts
3. Check `Team ranking assigned` to see if sorting applied correctly
4. Use `TicketDiff` values to verify tiebreaker calculation

Example investigation:
```
TeamId=1: W=6, T=2, L=4, TicketDiff=+342
TeamId=2: W=6, T=2, L=4, TicketDiff=+125
→ Same W-T-L, so Rank=1 vs Rank=2 determined by TicketDiff (primary tiebreaker)
```

## Performance Considerations

- **Info Level**: ~5-10 logs per team mapping, ~15-20 per ranking calculation
- **Debug Level**: Adds player rosters (only shown at Debug level to avoid spam)
- **Error Level**: Includes full exception stack traces
- **Async**: All logging is fire-and-forget (non-blocking)
- **Structured Format**: Efficient for log aggregation systems

## Future Enhancements

When integrating with OTEL spans:
```csharp
// Option: Use Activity/Span for additional context
using var activity = new Activity("DetectTeamMapping").Start();
activity.SetTag("tournament.id", tournamentId);
activity.SetTag("round.id", roundId);
// logs will automatically be associated with this span
```

This maintains the current logging while adding distributed tracing capabilities.
