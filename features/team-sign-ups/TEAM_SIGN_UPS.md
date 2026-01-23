# Tournament Team Registration Feature Plan

## Implementation Status

| Phase | Status | Notes |
|-------|--------|-------|
| Phase 1: Database Foundation | ✅ Complete | Migration: `20260119112721_AddTeamRegistrationFields` |
| Phase 2: Self-Service Registration APIs | ✅ Complete | Controllers and DTOs created |
| Phase 3: Public API Extensions | ✅ Complete | Team list enhanced with leader info |
| Phase 4: Frontend Implementation | ⏳ Pending | Not yet started |

**Migration Applied:** ✅ Yes (2026-01-20)

**Next Steps:** Implement frontend (Phase 4)

---

## Backend Implementation Details

### Files Created

**Entities Modified:**
- `api/PlayerTracking/PlayerTrackerDbContext.cs` - Added new fields to `TournamentTeam` and `TournamentTeamPlayer`

**New Controllers:**
- `api/Gamification/TeamRegistrationController.cs` - Self-service registration endpoints
- `api/Gamification/TeamLeaderController.cs` - Team leader management endpoints

**New DTOs (in `api/Gamification/Models/`):**
- `RegistrationStatusResponse.cs` - User's registration status
- `CreateTeamRequest.cs` / `CreateTeamResponse.cs` - Team creation
- `JoinTeamRequest.cs` - Join team request
- `TeamDetailsResponse.cs` - Team details with players
- `UpdateTeamRequest.cs` - Update team info
- `AddPlayerRequest.cs` - Add player to team

**Migration:**
- `api/Migrations/20260119112721_AddTeamRegistrationFields.cs`

### API Endpoints Summary

**TeamRegistrationController** (`/stats/tournament/{tournamentId}/registration`):
```
GET  /my-status              → RegistrationStatusResponse
POST /teams                  → CreateTeamResponse
POST /teams/{teamId}/join    → 200 OK
DELETE /teams/{teamId}/leave → 200 OK
```

**TeamLeaderController** (`/stats/tournament/{tournamentId}/my-team`):
```
GET    /                      → TeamDetailsResponse
PUT    /                      → 200 OK
POST   /players               → 200 OK
DELETE /players/{playerName}  → 200 OK
```

**PublicTournamentController** (enhanced):
- Team list now includes `Tag`, `LeaderPlayerName`, and `IsLeader` per player

---

## Overview

Build a self-service team registration system for tournaments during the "Registration" phase. Users authenticate via Discord, link their BF1942 player name(s), then can create teams or join existing teams.

## User Registration Flow

1. **Not logged in** → Click "Register" → Discord OAuth → return to tournament
2. **Logged in, no player names** → Prompt to link player name(s) from Players table
3. **Logged in with player names** → Can create or join a team:
   - **Create Team**: Enter team name/tag, select their player name, acknowledge rules
   - **Join Team**: Browse teams, click join, select their player name, acknowledge rules
4. **Team leaders** can manually add other players by PlayerName to their roster

## Database Schema Changes

### 1. Modify TournamentTeam Entity

Add leadership and tag:

```csharp
public class TournamentTeam
{
    // Existing: Id, TournamentId, Name, CreatedAt

    // NEW fields
    public string? Tag { get; set; }           // Short clan tag e.g. "[GGE]"
    public int? LeaderUserId { get; set; }     // User who created/leads team

    // NEW navigation
    public User? Leader { get; set; }
}
```

### 2. Modify TournamentTeamPlayer Entity

Add user link and rules acknowledgment:

```csharp
public class TournamentTeamPlayer
{
    // Existing: Id, TournamentTeamId, PlayerName

    // NEW fields
    public int? UserId { get; set; }                    // Link to User if they joined themselves
    public bool IsTeamLeader { get; set; } = false;
    public bool RulesAcknowledged { get; set; } = false;
    public Instant? RulesAcknowledgedAt { get; set; }
    public Instant JoinedAt { get; set; }

    // NEW navigation
    public User? User { get; set; }
}
```

## API Endpoints

### New Controller: TeamRegistrationController
**Base route:** `/stats/tournament/{tournamentId}/registration`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/my-status` | Required | Get user's registration status (team membership, linked player names) |
| POST | `/teams` | Required | Create a new team (user becomes leader) |
| POST | `/teams/{teamId}/join` | Required | Join an existing team (immediately added) |
| DELETE | `/teams/{teamId}/leave` | Required | Leave a team |

### New Controller: TeamLeaderController
**Base route:** `/stats/tournament/{tournamentId}/my-team`

| Method | Endpoint | Auth | Description |
|--------|----------|------|-------------|
| GET | `/` | Team Leader | Get my team details with members |
| PUT | `/` | Team Leader | Update team name/tag |
| POST | `/players` | Team Leader | Add player by PlayerName |
| DELETE | `/players/{playerName}` | Team Leader | Remove player from team |

### Extend PublicTournamentController

Existing team list endpoint enhanced to include leader info.

## Security & Business Rules

1. **Registration Phase Guard**: All self-service actions require `Tournament.Status == "registration"`
2. **Player Name Required**: User must have at least one linked player name to register
3. **One Team Per User**: User can only be on one team per tournament
4. **Player Name Uniqueness**: Same PlayerName cannot be on multiple teams in same tournament
5. **Team Leader Protection**: Team leader cannot leave their own team (must delete team)
6. **Immediate Joining**: Users join teams directly, no approval required
7. **Strict Player Name Validation**: PlayerNames MUST exist in the Players table

## Implementation Order

### Phase 1: Database Foundation
1. Add Tag, LeaderUserId to TournamentTeam entity
2. Add UserId, IsTeamLeader, RulesAcknowledged, RulesAcknowledgedAt, JoinedAt to TournamentTeamPlayer
3. Add DbContext configurations for new relationships
4. Create EF migration

**Files:**
- `api/PlayerTracking/PlayerTrackerDbContext.cs`
- New migration in `api/Migrations/`

### Phase 2: Self-Service Registration APIs
1. Create `TeamRegistrationController.cs` (my-status, create team, join team, leave team)
2. Create `TeamLeaderController.cs` (manage roster, update team)
3. Add DTOs for requests/responses

**Files:**
- `api/Gamification/TeamRegistrationController.cs` (new)
- `api/Gamification/TeamLeaderController.cs` (new)
- `api/Gamification/Models/` (new DTOs)

### Phase 3: Public API Extensions
1. Enhance team list response with leader info

**Files:**
- `api/Controllers/PublicTournamentController.cs`

### Phase 4: Frontend Implementation
1. Create `teamRegistrationService.ts` - API client for registration endpoints
2. Create `CreateTeamModal.vue` - Form for team name, tag, player name selection, rules acknowledgment
3. Create `JoinTeamModal.vue` - Team selection, player name selection, rules acknowledgment
4. Create `TeamManagementPanel.vue` - Team leader view to manage roster
5. Update `PublicTournamentTeams.vue` - Add registration flow with status checks

**Required API Calls:**
```typescript
// teamRegistrationService.ts should implement:
getMyStatus(tournamentId: number): Promise<RegistrationStatusResponse>
createTeam(tournamentId: number, request: CreateTeamRequest): Promise<CreateTeamResponse>
joinTeam(tournamentId: number, teamId: number, request: JoinTeamRequest): Promise<void>
leaveTeam(tournamentId: number, teamId: number): Promise<void>
getMyTeam(tournamentId: number): Promise<TeamDetailsResponse>
updateTeam(tournamentId: number, request: UpdateTeamRequest): Promise<void>
addPlayer(tournamentId: number, playerName: string): Promise<void>
removePlayer(tournamentId: number, playerName: string): Promise<void>
```

**UI Flow:**
1. Check `tournament.status === "registration"` to show registration UI
2. Call `/my-status` to get user's linked player names and current team membership
3. If no linked player names → show prompt to link player names first
4. If no team → show "Create Team" and "Join Team" buttons
5. If on team as leader → show TeamManagementPanel
6. If on team as member → show team info with "Leave Team" button

**Files:**
- `src/components/tournament/` (new components)
- `src/views/PublicTournamentTeams.vue`
- `src/services/teamRegistrationService.ts` (new)

## Verification Plan

1. **Unit tests** for registration business logic
2. **Integration tests** for API endpoints
3. **Manual testing flow:**
   - Create tournament, set status to "registration"
   - New user clicks Register → Discord OAuth → prompted to link player name
   - User creates a team (name, tag, selects player name, acknowledges rules)
   - Another user joins the team (selects their player name, acknowledges rules)
   - Team leader adds another player by PlayerName
   - Verify team list shows leader and members correctly
   - Change tournament status to "open" → verify registration is blocked

## Design Decisions

- **No join request system**: Users join teams directly, immediately added
- **Player name linking required**: Users must link their BF1942 player name before registering
- **PlayerName-based identification**: Team members identified by BF1942 PlayerName (validated against Players table)
- **UserId linking**: When a user joins/creates a team, their UserId is linked to their TournamentTeamPlayer record
