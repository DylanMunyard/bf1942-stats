# Tournament Match Result & Round Linking API (Final)

## Endpoints for Match Results & Round Linking

### 1. Create Match Result (optionally link round in ONE CALL)
**Endpoint:**
```
POST /stats/admin/tournaments/{tournamentId}/matches/{matchId}/maps/{mapId}/result
```

**Request Body (without round link):**
```json
{
  "mapId": 5,
  "team1Id": 10,
  "team2Id": 11,
  "team1Tickets": 456,
  "team2Tickets": 234,
  "winningTeamId": 10
}
```

**Request Body (WITH round link - ONE CALL):**
```json
{
  "mapId": 5,
  "team1Id": 10,
  "team2Id": 11,
  "team1Tickets": 456,
  "team2Tickets": 234,
  "winningTeamId": 10,
  "roundId": "round-abc123"  // ← OPTIONAL - creates AND links in one call
}
```

**Response:** `TournamentMatchResultAdminResponse`
```json
{
  "id": 42,
  "tournamentId": 1,
  "matchId": 5,
  "mapId": 12,
  "roundId": "round-abc123",  // Set if roundId provided
  "week": "Week 1",           // Set if round linked
  "team1Id": 10,
  "team1Name": "Team A",
  "team2Id": 11,
  "team2Name": "Team B",
  "winningTeamId": 10,
  "winningTeamName": "Team A",
  "team1Tickets": 456,
  "team2Tickets": 234,
  "teamMappingWarning": null,  // Set if auto-detection had conflicts
  "updatedAt": "2024-11-03T14:30:00Z"
}
```

**Behavior:**
- If `roundId` omitted: Creates result without round
- If `roundId` provided: Creates result AND links round + auto-detects teams from round
- Returns `teamMappingWarning` if auto-detection had team conflicts

---

### 2. Link/Unlink Round to Existing Result
**Endpoint:**
```
PUT /stats/admin/tournaments/{tournamentId}/match-results/{resultId}/round
```

**Request Body (Link Round):**
```json
{
  "roundId": "round-abc123"
}
```

**Request Body (Unlink Round):**
```json
{
  "roundId": null
}
```

**Response:** `TournamentMatchResultAdminResponse` with updated result

**Behavior:**
- If `roundId` provided: Links round and auto-detects teams from round data
- If `roundId` is null: Unlinks round (result remains, just removes round reference)

---

### 3. Update Match Result (Scores & Teams Only)
**Endpoint:**
```
PUT /stats/admin/tournaments/{tournamentId}/match-results/{resultId}/manual-update
```

**Request Body:**
```json
{
  "team1Id": 10,
  "team2Id": 11,
  "team1Tickets": 400,
  "team2Tickets": 300,
  "winningTeamId": 10
}
```

**Response:** `TournamentMatchResultAdminResponse` with updated details

**Behavior:**
- Updates the manual result's scores and team assignments
- Does NOT affect round linking (use `/round` endpoint for that)

---

## Usage Scenarios

### Scenario A: Create + Link Round in ONE CALL (what UI will do)
```bash
POST /stats/admin/tournaments/1/matches/5/maps/12/result
{
  "team1Id": 10,
  "team2Id": 11,
  "team1Tickets": 456,
  "team2Tickets": 234,
  "winningTeamId": 10,
  "roundId": "round-abc123"
}

# Response: { "id": 42, "roundId": "round-abc123", ... }
# Teams auto-detected from round, may include teamMappingWarning
```

---

### Scenario B: Create without round, link later
```bash
# Create
POST /stats/admin/tournaments/1/matches/5/maps/12/result
{
  "team1Id": 10,
  "team2Id": 11,
  "team1Tickets": 456,
  "team2Tickets": 234,
  "winningTeamId": 10
}

# Response: { "id": 42, "roundId": null, ... }

# Then link later
PUT /stats/admin/tournaments/1/match-results/42/round
{
  "roundId": "round-abc123"
}
```

---

### Scenario C: Unlink Round
```bash
PUT /stats/admin/tournaments/1/match-results/42/round
{
  "roundId": null
}

# Result remains, round reference removed
```

---

## Response Details

### TournamentMatchResultAdminResponse
- `id`: Result ID
- `roundId`: Linked round ID (null if not linked)
- `week`: Week from the round (null if not linked)
- `team1Id`, `team2Id`: Team IDs in the result
- `team1Name`, `team2Name`: Team names (from tournament teams)
- `team1Tickets`, `team2Tickets`: Final scores
- `winningTeamId`, `winningTeamName`: Winner details
- `teamMappingWarning`: Warning if round auto-detection had issues
- `updatedAt`: Last modification timestamp

---

## Error Handling

All endpoints return:
- **400 Bad Request**: Invalid teams, missing data, round not found
- **401 Unauthorized**: User not authenticated
- **404 Not Found**: Tournament/match/map/result not found
- **500 Internal Server Error**: Unexpected errors

---

## Async Behavior

All endpoints trigger automatic ranking recalculation in background:
- After creating a result
- After linking/unlinking a round
- After updating scores