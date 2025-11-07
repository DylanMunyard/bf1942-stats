# Tournament UI Backend Features - Navigation Redesign

**Status**: In Progress
**Branch**: `claude/ui-backend-features-011CUt3aDriLJgKNNA9gByzh`
**Created**: 2025-11-07

## Overview

Backend API changes to support the tournament navigation redesign in the UI. This adds support for:
- Tournament status management (Draft/Registration/Open/Closed)
- Game mode configuration
- Editable week date ranges (display metadata)
- Tournament file management (URL links)
- Latest matches display for tournament hub
- Draft tournaments filtered from public endpoints

## Design Decisions

### 1. Tournament Status
- **Type**: String column (NOT NULL, default 'draft')
- **Values**: 'draft' | 'registration' | 'open' | 'closed'
- **Default**: 'draft' for new tournaments
- **Existing**: Backfill to 'draft' (can be manually updated by admins)
- **Transitions**: No validation - admins can freely change status
- **Public Visibility**: Draft tournaments return 404 on public endpoints

### 2. Game Mode
- **Type**: String column (nullable)
- **Validation**: Soft validation with predefined constants (Conquest, CTF, TDM, Coop)
- **Extensibility**: Stored as string for future flexibility
- **Default**: null (optional field)

### 3. Week Dates
- **Storage**: Separate `TournamentWeekDate` table
- **Purpose**: Display metadata only (not used in match logic)
- **Relationship**: Week label links to `TournamentMatch.Week` (optional)
- **Update Strategy**: Replace all (delete + insert) on PUT
- **Structure**:
  - Week: string nullable (e.g., "Week 1", null for unweekly)
  - StartDate: Instant
  - EndDate: Instant

### 4. Tournament Files
- **Type**: URL links only (no upload support)
- **Storage**: `TournamentFile` table
- **Fields**: Name, URL, Category (optional), UploadedAt
- **Purpose**: Link to external resources (map packs, mods, etc.)
- **Endpoints**: Standard CRUD (POST, PUT, DELETE)

### 5. Team Leaders
- **Status**: Out of scope for v1
- **Reason**: Requires user account integration
- **Future**: Will be implemented when auth/user linking is ready

### 6. Latest Matches
- **Definition**: 2 most recent completed matches
- **Completed Match**: All maps have at least 1 match result
- **Sorting**: Ordered by ScheduledDate DESC
- **Usage**: Displayed on tournament hub for quick activity view

## Database Schema Changes

### New Tables

#### TournamentWeekDate
```csharp
public class TournamentWeekDate
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string? Week { get; set; }          // "Week 1", "Week 2", null
    public Instant StartDate { get; set; }
    public Instant EndDate { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
```

#### TournamentFile
```csharp
public class TournamentFile
{
    public int Id { get; set; }
    public int TournamentId { get; set; }
    public string Name { get; set; } = "";     // "Map Pack v1.0"
    public string Url { get; set; } = "";      // Full URL
    public string? Category { get; set; }      // Optional: 'map', 'mod', etc.
    public Instant UploadedAt { get; set; }

    public Tournament Tournament { get; set; } = null!;
}
```

### Modified Tables

#### Tournament
- Add `Status` (string, NOT NULL, default 'draft')
- Add `GameMode` (string, nullable)
- Add navigation: `List<TournamentWeekDate> WeekDates`
- Add navigation: `List<TournamentFile> Files`

## API Changes

### Public Endpoints

#### GET /public/tournaments/:id
**Changes**:
- Add `status` field
- Add `gameMode` field
- Add `latestMatches` field (2 most recent completed)
- **Behavior**: Return 404 if status === 'draft'

### Admin Endpoints

#### GET /admin/tournaments/:id
**Changes**:
- Add `status` field
- Add `gameMode` field
- Add `weekDates` array
- Add `files` array
- Add `latestMatches` field

#### PUT /admin/tournaments/:id
**Changes**:
- Accept `status` in request body
- Accept `gameMode` in request body
- Accept `weekDates` array (replaces all)

#### POST /admin/tournaments/:id/files
**New Endpoint**:
```
Request: { name, url, category? }
Response: { id, name, url, category?, uploadedAt }
```

#### PUT /admin/tournaments/:id/files/:fileId
**New Endpoint**:
```
Request: { name?, url?, category? }
Response: { id, name, url, category?, uploadedAt }
```

#### DELETE /admin/tournaments/:id/files/:fileId
**New Endpoint**:
```
Response: { success, message? }
```

## Implementation Plan

### Phase 1: Database Models & Migration
- [ ] Add `TournamentWeekDate` entity to DbContext
- [ ] Add `TournamentFile` entity to DbContext
- [ ] Update `Tournament` entity with new columns and navigation properties
- [ ] Create migration with:
  - Add Status column (default 'draft')
  - Add GameMode column (nullable)
  - Create TournamentWeekDate table
  - Create TournamentFile table
- [ ] Run migration and verify

### Phase 2: DTOs & Response Models
- [ ] Update `PublicTournamentDetailResponse` with status, gameMode, latestMatches
- [ ] Update `TournamentDetailResponse` with status, gameMode, weekDates, files, latestMatches
- [ ] Create `TournamentWeekDateResponse` record
- [ ] Create `TournamentFileResponse` record
- [ ] Update `UpdateTournamentRequest` with status, gameMode, weekDates
- [ ] Create `CreateTournamentFileRequest` record
- [ ] Create `UpdateTournamentFileRequest` record

### Phase 3: Core Logic - Latest Matches
- [ ] Implement latest matches query logic (helper method)
- [ ] Test with completed/incomplete matches

### Phase 4: Public Tournament Controller
- [ ] Update `GetTournament` to include status, gameMode, latestMatches
- [ ] Add draft status filtering (404 if draft)
- [ ] Test public endpoint with draft/non-draft tournaments

### Phase 5: Admin Tournament Controller - Read
- [ ] Update `GetTournament` to include status, gameMode, weekDates, files, latestMatches
- [ ] Load weekDates from database
- [ ] Load files from database
- [ ] Include latest matches
- [ ] Test admin endpoint

### Phase 6: Admin Tournament Controller - Update
- [ ] Update `UpdateTournament` to accept status, gameMode
- [ ] Add week dates update logic (replace all strategy)
- [ ] Validate game mode (soft validation)
- [ ] Test updates

### Phase 7: File Management Endpoints
- [ ] Implement `POST /admin/tournaments/:id/files` (create file link)
- [ ] Implement `PUT /admin/tournaments/:id/files/:fileId` (update file link)
- [ ] Implement `DELETE /admin/tournaments/:id/files/:fileId` (delete file link)
- [ ] Add authorization checks (user owns tournament)
- [ ] Test all file operations

### Phase 8: Testing & Validation
- [ ] Test draft filtering on public endpoint
- [ ] Test status transitions
- [ ] Test week dates CRUD
- [ ] Test file links CRUD
- [ ] Test latest matches with various scenarios
- [ ] Verify backward compatibility (existing tournaments work)

### Phase 9: Commit & Push
- [ ] Review all changes
- [ ] Create descriptive commit message
- [ ] Push to branch

## Notes

- Week dates are purely for display - they don't affect match logic
- Files are URL links only - no upload/blob storage in v1
- Draft status filtering prevents tournaments from appearing publicly until ready
- Latest matches shows activity on tournament hub page
- All changes are backward compatible with existing tournaments

## Testing Scenarios

1. **Draft Tournament**: Verify 404 on public endpoint, visible on admin
2. **Status Changes**: Change status freely between all values
3. **Week Dates**: Add/update/delete week date ranges
4. **Files**: Add/update/delete file links
5. **Latest Matches**: Tournament with 0, 1, 2, 3+ completed matches
6. **Incomplete Matches**: Matches with no results should not appear in latestMatches
7. **Existing Tournaments**: Verify they load correctly with new fields (null/default values)
