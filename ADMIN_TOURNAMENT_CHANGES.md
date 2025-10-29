# Admin Tournament Controller Changes

## Summary

This document describes the changes made to implement user-based tournament management and separate admin endpoints from future public endpoints.

## Changes Made

### 1. Database Schema Updates

**Added `CreatedByUserEmail` field to Tournament entity:**
- Type: `string` (mandatory)
- Purpose: Track which user created the tournament using their email from JWT
- Indexed for query performance

**Migration:** `20251029111403_AddCreatedByUserEmailToTournament`

**Added `Game` field to Tournament entity:**
- Type: `string` (mandatory)
- Purpose: Track which game the tournament is for
- Allowed values: `bf1942`, `fh2`, `bfvietnam`
- Indexed for query performance

**Migration:** `20251029121232_AddGameToTournament`

### 2. Controller Restructuring

**Renamed:** `TournamentController.cs` → `AdminTournamentController.cs`

**Route changed:** `/stats/Tournament` → `/stats/admin/tournaments`

This separation allows for:
- Admin endpoints for tournament management (authenticated users managing their own tournaments)
- Future public endpoints for viewing tournaments (separate controller)

### 3. Authentication & Authorization

All endpoints now:
1. Extract user email from JWT using `User.FindFirstValue(ClaimTypes.Email)`
2. Store the email when creating tournaments (no user input required)
3. Filter queries by the logged-in user's email (users only see their own tournaments)

### 4. Updated Endpoints

#### `GET /stats/admin/tournaments`
- **Auth:** Required
- **Returns:** Only tournaments created by the current user
- Filters by `CreatedByUserEmail`

#### `GET /stats/admin/tournaments/{id}`
- **Auth:** Required
- **Returns:** Tournament details only if created by the current user
- Verifies ownership via `CreatedByUserEmail`

#### `POST /stats/admin/tournaments`
- **Auth:** Required
- **Changes:** Automatically extracts email from JWT and stores in `CreatedByUserEmail`
- User doesn't need to send their email
- **New required field:** `game` - must be one of: `bf1942`, `fh2`, `bfvietnam`
- Game type is validated and stored in lowercase

#### `PUT /stats/admin/tournaments/{id}`
- **Auth:** Required
- **Changes:** Verifies ownership before allowing updates
- **Optional field:** `game` - if provided, must be one of: `bf1942`, `fh2`, `bfvietnam`

#### `POST /stats/admin/tournaments/{id}/rounds`
- **Auth:** Required
- **Changes:** Verifies ownership before allowing round additions

#### `DELETE /stats/admin/tournaments/{id}`
- **Auth:** Required
- **Changes:** Verifies ownership before allowing deletion

#### `GET /stats/admin/tournaments/{id}/image`
- **Auth:** Required
- **Changes:** Verifies ownership before serving image

## Security Benefits

1. **User isolation:** Users can only manage their own tournaments
2. **Automatic attribution:** Tournament creator is automatically tracked from JWT
3. **No trust in client data:** Email is extracted server-side from verified JWT token
4. **Authorization on all operations:** All admin operations verify user ownership

## Frontend Impact

Frontend applications should:
1. Update API endpoint from `/stats/Tournament` to `/stats/admin/tournaments`
2. **Remove any email fields from tournament creation requests** (handled automatically by backend)
3. **Add `game` field to tournament creation requests** (required)
4. **Add `game` selector to tournament creation/edit UI** with options: BF1942, FH2, BF Vietnam
5. Display `game` field in tournament lists and details
6. Understand that GET requests now only return tournaments created by the authenticated user

## Future Work

A separate `TournamentController.cs` can be created for public tournament viewing:
- Route: `/stats/tournaments`
- No authentication required (or optional)
- Returns all public tournaments or tournament details for viewing only
- No modification operations

