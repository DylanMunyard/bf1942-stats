# Tournament API Specification

## Overview

The Tournament API is split into two main areas:
1. **Admin Endpoints** (`/stats/admin/tournaments`) - For authenticated users to manage their own tournaments
2. **Public Endpoints** (`/stats/tournament`) - For viewing tournaments (coming soon)

---

## Admin Endpoints (Authenticated)

These endpoints require authentication and operate on tournaments created by the current user only.

### GET /stats/admin/tournaments
List tournaments created by the current user

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
[
  {
    "id": 1,
    "name": "Summer Championship 2025",
    "organizer": "PlayerName",
    "game": "bf1942",
    "createdAt": "2025-10-29T10:00:00Z",
    "anticipatedRoundCount": 5,
    "roundCount": 3,
    "hasHeroImage": true
  }
]
```

**Notes:**
- Only returns tournaments created by the authenticated user
- `game`: Game type - one of: `bf1942`, `fh2`, `bfvietnam`
- `anticipatedRoundCount`: Optional field indicating how many rounds the organizer expects
- `roundCount`: Actual number of rounds currently added to the tournament

---

### GET /stats/admin/tournaments/{id}
Get tournament details (owned by current user only)

**Authentication:** Required (JWT Bearer token)

**Response:**
```json
{
  "id": 1,
  "name": "Summer Championship 2025",
  "organizer": "PlayerName",
  "game": "bf1942",
  "createdAt": "2025-10-29T10:00:00Z",
  "anticipatedRoundCount": 5,
  "rounds": [
    {
      "roundId": "bf1942-server-guid-2025-10-29-10-00-00",
      "serverGuid": "server-guid-123",
      "serverName": "BF1942 Official Server",
      "mapName": "Wake Island",
      "startTime": "2025-10-29T10:00:00Z",
      "endTime": "2025-10-29T10:45:00Z",
      "winningTeam": "Axis",
      "winningPlayers": ["Player1", "Player2", "Player3"],
      "tickets1": 150,
      "tickets2": 0,
      "team1Label": "Axis",
      "team2Label": "Allies"
    }
  ],
  "overallWinner": {
    "team": "Axis",
    "players": ["Player1", "Player2", "Player3"]
  },
  "heroImageBase64": "iVBORw0KGgoAAAANSUhEUgAAAAEAAAABCAYAAAAfFcSJ...",
  "heroImageContentType": "image/png"
}
```

**Notes:**
- Returns 404 if tournament not found or not owned by current user
- `game`: Game type - one of: `bf1942`, `fh2`, `bfvietnam`
- User email is automatically tracked from JWT (not exposed in response)

---

### GET /stats/admin/tournaments/{id}/image
Get tournament hero image as binary (owned by current user only)

**Authentication:** Required (JWT Bearer token)

**Response:** Binary image data with appropriate `Content-Type` header

**Notes:**
- Returns 404 if tournament not found or not owned by current user

---

### POST /stats/admin/tournaments
Create tournament

**Authentication:** Required (JWT Bearer token)

**Request:**
```json
{
  "name": "Summer Championship 2025",
  "organizer": "PlayerName",
  "game": "bf1942",
  "anticipatedRoundCount": 5,
  "roundIds": ["round-id-1", "round-id-2"],
  "heroImageBase64": "base64-encoded-string",
  "heroImageContentType": "image/png"
}
```

**Notes:**
- `name`: required
- `organizer`: required (must be existing player)
- `game`: required - one of: `bf1942`, `fh2`, `bfvietnam`
- `anticipatedRoundCount`: optional (indicates how many rounds are expected)
- `roundIds`: optional (rounds cannot be in other tournaments)
- `heroImageBase64`: optional (max 4MB)
- `heroImageContentType`: optional (jpeg, jpg, png, gif, webp)
- **User email is automatically extracted from JWT** - do not send it in request

**Response:** Same as GET tournament detail

**Example - Create tournament without rounds:**
```json
{
  "name": "Winter Championship 2026",
  "organizer": "PlayerName",
  "game": "fh2",
  "anticipatedRoundCount": 3
}
```

---

### PUT /stats/admin/tournaments/{id}
Update tournament (owned by current user only)

**Authentication:** Required (JWT Bearer token)

**Request:**
```json
{
  "name": "Updated Tournament Name",
  "organizer": "NewOrganizerName",
  "game": "bfvietnam",
  "anticipatedRoundCount": 7,
  "roundIds": ["round1", "round2", "round3"],
  "heroImageBase64": "base64-encoded-string",
  "heroImageContentType": "image/png"
}
```

**Notes:** 
- All fields optional. Include only fields to update.
- `game`: if provided, must be one of: `bf1942`, `fh2`, `bfvietnam`
- Returns 404 if tournament not found or not owned by current user

**Response:** Same as GET tournament detail

---

### DELETE /stats/admin/tournaments/{id}
Delete tournament (owned by current user only)

**Authentication:** Required (JWT Bearer token)

**Response:** 204 No Content

**Notes:**
- Returns 404 if tournament not found or not owned by current user

---

### POST /stats/admin/tournaments/{id}/rounds
Add a round to an existing tournament (owned by current user only)

**Authentication:** Required (JWT Bearer token)

**Request:**
```json
{
  "roundId": "bf1942-server-guid-2025-10-29-10-00-00"
}
```

**Notes:**
- `roundId`: required (must be an existing round)
- Round cannot already be in another tournament
- Returns 404 if tournament not found or not owned by current user
- Returns full tournament details after adding the round

**Response:** Same as GET tournament detail

**Example Use Case:**
1. Create tournament without rounds: `POST /stats/admin/tournaments`
2. As each round is played, add it: `POST /stats/admin/tournaments/1/rounds`
3. Repeat step 2 for each subsequent round

---

## Public Endpoints (Coming Soon)

Public viewing endpoints for tournaments will be added at `/stats/tournament`:
- `GET /stats/tournament` - List all public tournaments
- `GET /stats/tournament/{id}` - View any tournament details
- `GET /stats/tournament/{id}/image` - View any tournament hero image

These endpoints will not require authentication and will allow anyone to view tournaments.

---

### GET /stats/rounds
List game rounds for selection (no auth required)

**Query Parameters:**
- `page` (default: 1)
- `pageSize` (default: 100)
- `sortBy` (default: "StartTime")
- `sortOrder` (default: "desc")
- `serverName` (partial match)
- `serverGuid` 
- `mapName`
- `gameType` (bf1942, fh2, bfvietnam)
- `startTimeFrom` (ISO 8601)
- `startTimeTo` (ISO 8601)
- `minParticipants`
- `maxParticipants`
- `isActive` (bool)
- `includePlayers` (default: true)

**Response:**
```json
{
  "items": [
    {
      "roundId": "bf1942-server-guid-2025-10-29-10-00-00",
      "serverName": "BF1942 Official Server",
      "serverGuid": "server-guid-123",
      "mapName": "Wake Island",
      "gameType": "bf1942",
      "startTime": "2025-10-29T10:00:00Z",
      "endTime": "2025-10-29T10:45:00Z",
      "durationMinutes": 45,
      "participantCount": 32,
      "isActive": false,
      "team1Label": "Axis",
      "team2Label": "Allies",
      "players": []
    }
  ],
  "page": 1,
  "pageSize": 100,
  "totalItems": 250,
  "totalPages": 3
}
```

---

## Authentication
Use JWT Bearer token: `Authorization: Bearer <token>`

