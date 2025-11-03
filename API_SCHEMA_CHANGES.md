# Tournament Match Results API Schema Changes

## Summary of Changes

**The key change: Maps can now have multiple match results instead of just one.**

Previously, a `TournamentMatchMap` had a single optional `MatchResult`. Now it has a collection of `MatchResults` because the same map can be played multiple times in a tournament (each round may have a different result).

---

## OLD API Schema (Breaking Change)

### TournamentMatchMapResponse (OLD)
```json
{
  "id": 1,
  "mapName": "Colditz",
  "mapOrder": 0,
  "teamId": 5,
  "teamName": "Team Alpha",
  "roundId": "round-123",
  "round": {
    "roundId": "round-123",
    "serverGuid": "server-guid",
    "serverName": "Server Name",
    "mapName": "Colditz",
    "startTime": "2025-01-01T10:00:00Z",
    "endTime": "2025-01-01T11:00:00Z",
    "tickets1": 200,
    "tickets2": 150,
    "team1Label": "Team A",
    "team2Label": "Team B"
  },
  "matchResult": {
    "id": 10,
    "team1Id": 5,
    "team1Name": "Team Alpha",
    "team2Id": 6,
    "team2Name": "Team Bravo",
    "winningTeamId": 5,
    "winningTeamName": "Team Alpha",
    "team1Tickets": 200,
    "team2Tickets": 100
  }
}
```

---

## NEW API Schema

### TournamentMatchMapResponse (NEW)
```json
{
  "id": 1,
  "mapName": "Colditz",
  "mapOrder": 0,
  "teamId": 5,
  "teamName": "Team Alpha",
  "matchResults": [
    {
      "id": 10,
      "team1Id": 5,
      "team1Name": "Team Alpha",
      "team2Id": 6,
      "team2Name": "Team Bravo",
      "winningTeamId": 5,
      "winningTeamName": "Team Alpha",
      "team1Tickets": 200,
      "team2Tickets": 100
    },
    {
      "id": 11,
      "team1Id": 6,
      "team1Name": "Team Bravo",
      "team2Id": 5,
      "team2Name": "Team Alpha",
      "winningTeamId": 6,
      "winningTeamName": "Team Bravo",
      "team1Tickets": 180,
      "team2Tickets": 120
    }
  ]
}
```

---

## Key Differences

| Property | OLD | NEW | Notes |
|----------|-----|-----|-------|
| `roundId` | ✅ Present (nullable) | ❌ Removed | RoundId is now only in TournamentMatchResult |
| `round` | ✅ Present (nullable object) | ❌ Removed | No longer at map level |
| `matchResult` | ✅ Present (single object, nullable) | ❌ Removed | Replaced by matchResults |
| `matchResults` | ❌ N/A | ✅ Present (array) | **NEW** - Always an array, can be empty |

---

## Breaking Changes for UI

1. **Access pattern changed from singular to plural**
   - OLD: `map.matchResult?.winningTeamName`
   - NEW: `map.matchResults[0]?.winningTeamName` or loop through array

2. **Round information moved**
   - OLD: Round details were at `map.round.*`
   - NEW: Round info is now in the `TournamentMatchResult` entity (database level)
   - For UI, you would need to fetch round details separately if needed
   - RoundId is available on each `TournamentMatchResult` if you need to link it

3. **Null handling**
   - OLD: `matchResult` was nullable (could be null for no result)
   - NEW: `matchResults` is always an array (empty array `[]` if no results)

---

## Complete Match Response Example

### Old Tournament Match Detail Response
```json
{
  "id": 100,
  "scheduledDate": "2025-01-01T19:00:00Z",
  "team1Id": 5,
  "team1Name": "Team Alpha",
  "team2Id": 6,
  "team2Name": "Team Bravo",
  "serverGuid": "server-123",
  "serverName": "BF1942 Tournament Server",
  "week": "Week 1",
  "createdAt": "2025-01-01T08:00:00Z",
  "maps": [
    {
      "id": 1,
      "mapName": "Colditz",
      "mapOrder": 0,
      "teamId": 5,
      "teamName": "Team Alpha",
      "roundId": "round-123",
      "round": {...},
      "matchResult": {...}
    }
  ]
}
```

### New Tournament Match Detail Response
```json
{
  "id": 100,
  "scheduledDate": "2025-01-01T19:00:00Z",
  "team1Id": 5,
  "team1Name": "Team Alpha",
  "team2Id": 6,
  "team2Name": "Team Bravo",
  "serverGuid": "server-123",
  "serverName": "BF1942 Tournament Server",
  "week": "Week 1",
  "createdAt": "2025-01-01T08:00:00Z",
  "maps": [
    {
      "id": 1,
      "mapName": "Colditz",
      "mapOrder": 0,
      "teamId": 5,
      "teamName": "Team Alpha",
      "matchResults": [
        {
          "id": 10,
          "team1Id": 5,
          "team1Name": "Team Alpha",
          "team2Id": 6,
          "team2Name": "Team Bravo",
          "winningTeamId": 5,
          "winningTeamName": "Team Alpha",
          "team1Tickets": 200,
          "team2Tickets": 100
        },
        {
          "id": 11,
          "team1Id": 6,
          "team1Name": "Team Bravo",
          "team2Id": 5,
          "team2Name": "Team Alpha",
          "winningTeamId": 6,
          "winningTeamName": "Team Bravo",
          "team1Tickets": 180,
          "team2Tickets": 120
        }
      ]
    }
  ]
}
```

---

## Affected Endpoints

All tournament detail/list endpoints return this new structure:

- `GET /api/tournaments/{tournamentId}` - Admin
- `GET /api/tournaments/{tournamentId}/matches/{matchId}` - Admin
- `GET /api/public/tournaments/{tournamentId}` - Public
- Any tournament or match listing endpoint

---

## Migration Guide for UI

### Pattern 1: Getting a single result (assumed first/only result)
```javascript
// OLD
const result = map.matchResult;
if (result) {
  console.log(result.winningTeamName);
}

// NEW
const result = map.matchResults?.[0];
if (result) {
  console.log(result.winningTeamName);
}
```

### Pattern 2: Iterating through results
```javascript
// OLD - Not applicable (single result)

// NEW - Loop through all results
map.matchResults.forEach(result => {
  console.log(`${result.team1Name} vs ${result.team2Name}: ${result.winningTeamName} won`);
});
```

### Pattern 3: Checking if results exist
```javascript
// OLD
if (map.matchResult) { ... }

// NEW
if (map.matchResults?.length > 0) { ... }
```

### Pattern 4: Accessing round information
```javascript
// OLD - From map object
const roundId = map.roundId;
const roundDetails = map.round;

// NEW - From database (not returned in API)
// You'll need to fetch round details separately if needed
// Each result has: result.roundId (if linked)
const roundId = map.matchResults[0]?.roundId;
// But round details are NOT returned - you'd need a separate API call
```

---

## TypeScript Type Definitions

### Old Types
```typescript
interface TournamentMatchMapResponse {
  id: number;
  mapName: string;
  mapOrder: number;
  teamId?: number;
  teamName?: string;
  roundId?: string;
  round?: TournamentRoundResponse;
  matchResult?: TournamentMatchResultResponse;
}

interface TournamentMatchResultResponse {
  id: number;
  team1Id?: number;
  team1Name?: string;
  team2Id?: number;
  team2Name?: string;
  winningTeamId?: number;
  winningTeamName?: string;
  team1Tickets: number;
  team2Tickets: number;
}
```

### New Types
```typescript
interface TournamentMatchMapResponse {
  id: number;
  mapName: string;
  mapOrder: number;
  teamId?: number;
  teamName?: string;
  matchResults: TournamentMatchResultResponse[];
}

interface TournamentMatchResultResponse {
  id: number;
  team1Id?: number;
  team1Name?: string;
  team2Id?: number;
  team2Name?: string;
  winningTeamId?: number;
  winningTeamName?: string;
  team1Tickets: number;
  team2Tickets: number;
}
```

---

## Database Note

The underlying data hasn't changed - `TournamentMatchResult` still has:
- `roundId` (optional) - Links to the actual game round if available
- `team1Id`, `team2Id` - Team assignments for this specific result
- `winningTeamId` - Winner of this result
- `team1Tickets`, `team2Tickets` - Final ticket counts for this result

The only structural change is at the API layer: results are now grouped as a collection under each map, rather than maps having a single result with a round reference.
