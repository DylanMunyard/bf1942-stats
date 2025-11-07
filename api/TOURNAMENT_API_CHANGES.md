# Tournament API - Recent Changes Summary

**Release:** `adec4e2` - Add tournament status, game mode, week dates, files, and latest matches

---

## New Endpoints

### Tournament Files Management
- **POST** `/stats/admin/tournaments/{id}/files` - Add a file link to tournament
- **PUT** `/stats/admin/tournaments/{id}/files/{fileId}` - Update a tournament file link
- **DELETE** `/stats/admin/tournaments/{id}/files/{fileId}` - Delete a tournament file link

### Public Leaderboard
- **GET** `/stats/tournaments/{idOrName}/leaderboard?week={week}` - Get team rankings (cumulative or by week)

---

## Updated Endpoints & Response Fields

### Tournament Detail Endpoints
**PUT** `/stats/admin/tournaments/{id}` and **GET** endpoints now support:

#### New Request Fields:
- `status` (enum: draft, registration, open, closed) - Tournament status
- `gameMode` (string) - Game mode like Conquest, CTF, TDM, Coop (supports custom values)
- `weekDates` (array) - Replace all week dates for the tournament

#### New Response Fields in `TournamentDetailResponse`:
- `status` - Current tournament status
- `gameMode` - Tournament game mode
- `weekDates` - Array of week date ranges
- `files` - Array of tournament files
- `latestMatches` - 2 most recent completed matches

Example structure:
```json
{
  "id": 1,
  "name": "Grand Tournament 2024",
  "status": "open",
  "gameMode": "Conquest",
  "weekDates": [
    {
      "id": 1,
      "week": "Week 1",
      "startDate": "2024-01-15T00:00:00Z",
      "endDate": "2024-01-21T23:59:59Z"
    }
  ],
  "files": [
    {
      "id": 1,
      "name": "Tournament Bracket",
      "url": "https://example.com/bracket.pdf",
      "category": "bracket",
      "uploadedAt": "2024-01-10T10:00:00Z"
    }
  ],
  "latestMatches": [
    {
      "id": 1,
      "scheduledDate": "2024-01-20T19:00:00Z",
      "team1Name": "Team A",
      "team2Name": "Team B",
      "maps": [...]
    }
  ]
}
```

### Public Tournament Detail Endpoint
**GET** `/stats/tournaments/{idOrName}` now returns:
- `status` - Tournament status (draft filtered out from public API)
- `gameMode` - Tournament game mode
- `latestMatches` - 2 most recent completed matches

---

## New Data Structures

### TournamentFileResponse
```json
{
  "id": 1,
  "name": "Tournament Bracket",
  "url": "https://example.com/bracket.pdf",
  "category": "bracket",
  "uploadedAt": "2024-01-10T10:00:00Z"
}
```

### TournamentWeekDateResponse
```json
{
  "id": 1,
  "week": "Week 1",
  "startDate": "2024-01-15T00:00:00Z",
  "endDate": "2024-01-21T23:59:59Z"
}
```

### PublicTeamRankingResponse (Leaderboard)
```json
{
  "rank": 1,
  "teamId": 1,
  "teamName": "Team A",
  "matchesPlayed": 5,
  "victories": 4,
  "ties": 0,
  "losses": 1,
  "roundsWon": 12,
  "roundsTied": 1,
  "roundsLost": 7,
  "ticketsFor": 450,
  "ticketsAgainst": 380,
  "ticketDifferential": 70,
  "points": 12,
  "totalRounds": 20
}
```

---

## Breaking Changes

⚠️ **None** - All changes are backward compatible. New fields are optional and existing fields remain unchanged.

---

## Validation Rules

### Tournament Status
- Allowed values: `draft`, `registration`, `open`, `closed`
- Soft enforcement - any value is accepted but warning logged if non-standard

### Game Mode
- Recommended: `Conquest`, `CTF`, `TDM`, `Coop`
- Custom values allowed - soft validation only

### Week Dates
- Start date must be before end date
- Multiple weeks can overlap if needed
- Week identifier is a string (e.g., "Week 1", "Round 1", custom format)

### Tournament Files
- `name` and `url` are required
- `category` is optional (suggested: bracket, schedule, rules, etc.)
- Files are ordered by upload date (newest first)

---

## Usage Examples

### Create Tournament with Status & Game Mode
```bash
POST /stats/admin/tournaments
{
  "name": "Spring Tournament 2024",
  "organizer": "PlayerName",
  "game": "bf1942",
  "status": "registration",
  "gameMode": "Conquest"
}
```

### Update Tournament with Week Dates
```bash
PUT /stats/admin/tournaments/1
{
  "status": "open",
  "weekDates": [
    {
      "week": "Week 1",
      "startDate": "2024-01-15T00:00:00Z",
      "endDate": "2024-01-21T23:59:59Z"
    },
    {
      "week": "Week 2",
      "startDate": "2024-01-22T00:00:00Z",
      "endDate": "2024-01-28T23:59:59Z"
    }
  ]
}
```

### Add Tournament File
```bash
POST /stats/admin/tournaments/1/files
{
  "name": "Tournament Bracket - v2",
  "url": "https://example.com/bracket-v2.pdf",
  "category": "bracket"
}
```

### Get Tournament Leaderboard
```bash
# Cumulative standings
GET /stats/tournaments/1/leaderboard

# Week-specific standings
GET /stats/tournaments/1/leaderboard?week=Week%201
```

---

## Migration Guide for UI

### Update Tournament Display
- Add status badge showing current tournament state
- Display game mode prominently
- Show week date ranges if available
- Display tournament files (if any) with category grouping

### Add Leaderboard Page
- Create new leaderboard view component
- Support week filtering via dropdown
- Display table with: Rank, Team, Record (W-T-L), Points, Rounds, Ticket Diff
- Include toggle for "All Time" vs "Week-specific" view

### File Management UI
- Add file upload section in tournament editor
- Support file categorization
- Show upload timestamp
- Allow delete operation (404 handled gracefully)

---

## OpenAPI Spec Location
Full OpenAPI 3.0 specification available in: `tournament-api-spec.yaml`

Import into Postman, Swagger UI, or your API documentation tool:
```bash
# Swagger UI
npx -y swagger-ui-express ./tournament-api-spec.yaml

# ReDoc
npx -y redoc-cli serve tournament-api-spec.yaml
```
