# Enhanced Logging for Tournament Ranking Algorithm

## Overview
Comprehensive logging has been added to the `CalculateTeamStatistics()` method to show the detailed thought process of the ranking algorithm. This helps with debugging and understanding how rankings are calculated.

## Logging Levels Used
- **LogInformation**: High-level progress (start, completion, summary statistics per team)
- **LogDebug**: Detailed algorithm step-by-step processing (individual rounds, match aggregations)
- **LogWarning**: Data quality issues (no results found)
- **LogError**: Failures with exception details

## Detailed Logging in CalculateTeamStatistics()

### 1. Round-Level Analysis (Debug Level)
For each round in the match results:
- Identifies if team participated (Team1 or Team2)
- Logs round outcome: WIN, TIE, or LOSS
- Logs ticket comparison: team tickets vs opponent tickets
- Shows per-round differential

**Example log output:**
```
Round-level results for TeamId=5 | Total Rounds Processed=8 | 
Details: MatchId=1 MapId=12 WIN Tickets=45v40 | 
         MatchId=1 MapId=13 LOSS Tickets=38v42 | 
         MatchId=2 MapId=12 WIN Tickets=48v35 | 
         MatchId=2 MapId=13 TIE Tickets=40v40 | ...
```

### 2. Match Aggregation (Debug Level)
For each match (group of rounds):
- Shows individual round tickets for each map
- Calculates total tickets for team and opponent
- Determines match outcome (WIN/TIE/LOSS) based on total tickets
- Logs detailed breakdown

**Example log output:**
```
Match aggregation for TeamId=5 MatchId=1 | Outcome=LOSS | 
Total Tickets=83v82 | Rounds: Round1(Map=12):45v40,Round2(Map=13):38v42
```

### 3. Match-Level Summary (Debug Level)
After processing all matches:
- Lists all matches with aggregated outcomes
- Shows total tickets per match
- Organizes by match ID for easy reference

**Example log output:**
```
Match-level results for TeamId=5 | Total Matches=4 | 
Details: MatchId=1 LOSS Rounds=[Round1(Map=12):45v40,Round2(Map=13):38v42] Total=83v82 | 
         MatchId=2 WIN Rounds=[Round1(Map=12):48v35,Round2(Map=13):40v40] Total=88v75 | ...
```

### 4. Final Statistics Summary (Debug Level)
- Points calculation: `Points = RoundsWon`
- Round-level totals: Wins, Ties, Losses
- Match-level totals: Matches Played, Victories, Ties, Losses
- Ticket totals: For, Against, Differential

**Example log output:**
```
Statistics summary for TeamId=5 | Points=4 RoundsWon=4 RoundsTied=1 RoundsLost=3 | 
MatchesPlayed=4 Victories=2 Ties=1 Losses=1 | 
TicketsFor=342 TicketsAgainst=298 Differential=44
```

## Complete Information Flow

### Calculation Phase (Information Level)
1. `Starting ranking calculation | TournamentId=1 Week=cumulative`
2. `Match results loaded | TournamentId=1 Week=cumulative ResultCount=32`
3. `Unique teams identified | TournamentId=1 Week=cumulative TeamCount=8`

### Per-Team Statistics (Information Level)
For each team:
```
Team statistics calculated | TournamentId=1 Week=cumulative TeamId=5 
Points=4 RoundsWon=4 RoundsTied=1 RoundsLost=3 
Matches=4 Victories=2 Ties=1 Losses=1 
TicketsFor=342 TicketsAgainst=298 TicketDiff=44
```

### Ranking Assignment (Information Level)
After sort order is applied:
```
Team ranking assigned | TournamentId=1 Week=cumulative TeamId=5 Rank=2 
Points=4 Matches=4 (V-T-L=2-1-1) 
Rounds=(W-T-L=4-1-3) 
Tickets=(342-298=44)
```

### Final Completion (Information Level)
```
Ranking calculation completed successfully | TournamentId=1 Week=cumulative RankingCount=8
```

## How to Use These Logs

### For Debugging
1. Enable Debug logging in appsettings.json
2. Run ranking recalculation
3. Look for team-specific entries in logs
4. Trace from round-level through match-level to final ranking

### For Understanding Rankings
1. Look at Information-level logs to see final standings
2. Enable Debug logs to understand why one team ranked higher than another
3. Check match aggregation logs to see how ticket totals were calculated
4. Review round-level logs to see individual performance per map

### For Auditing
1. All intermediate calculations are logged
2. Ticket totals are shown at round and match levels
3. Outcome determination logic is explicit in logs
4. No "magic" in calculation - every step is visible

## Example Scenario

If Team A and Team B have the same points but Team A ranks higher:
1. Information logs show: Points tie
2. Debug logs show: Team A has more rounds tied (tiebreaker #1)
3. If still tied, next Debug logs show: Team A has better ticket differential (tiebreaker #2)

This complete transparency makes it easy to understand the ranking algorithm's decision-making process.
