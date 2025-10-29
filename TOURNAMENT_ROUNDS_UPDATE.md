# Tournament Rounds Update - Summary

## Overview
Updated the tournament system to make rounds optional at creation time and added support for adding rounds incrementally.

## Changes Made

### 1. Database Schema Changes
- Added `AnticipatedRoundCount` field to the `Tournament` entity (nullable int)
- Created and applied migration: `20251029102655_AddAnticipatedRoundCountToTournament`

### 2. API Changes

#### Modified Endpoints

**POST /stats/tournament** (Create Tournament)
- `RoundIds` is now optional (was previously required)
- Added `AnticipatedRoundCount` field (optional) to allow organizers to indicate expected number of rounds
- Tournaments can now be created without any rounds
- If rounds are provided, validation still ensures they exist and aren't already in another tournament

**PUT /stats/tournament/{id}** (Update Tournament)
- Added `AnticipatedRoundCount` field support for updates

**GET /stats/tournament** (List Tournaments)
- Response now includes `AnticipatedRoundCount` field

**GET /stats/tournament/{id}** (Get Tournament Details)
- Response now includes `AnticipatedRoundCount` field

#### New Endpoint

**POST /stats/tournament/{id}/rounds** (Add Round to Tournament)
- Adds a single round to an existing tournament
- Request body: `{ "RoundId": "round-id-here" }`
- Returns the full tournament details with all rounds
- Validates that:
  - The tournament exists
  - The round exists
  - The round is not already in another tournament
- RESTful design - uses tournament ID in URL path and round ID in body

### 3. DTO Changes

**CreateTournamentRequest**
```csharp
public class CreateTournamentRequest
{
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public int? AnticipatedRoundCount { get; set; }      // NEW - Optional
    public List<string>? RoundIds { get; set; }          // NOW Optional
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
}
```

**UpdateTournamentRequest**
```csharp
public class UpdateTournamentRequest
{
    public string? Name { get; set; }
    public string? Organizer { get; set; }
    public int? AnticipatedRoundCount { get; set; }      // NEW
    public List<string>? RoundIds { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
}
```

**AddRoundRequest** (New)
```csharp
public class AddRoundRequest
{
    public string RoundId { get; set; } = "";
}
```

**TournamentListResponse**
```csharp
public class TournamentListResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }      // NEW
    public int RoundCount { get; set; }
    public bool HasHeroImage { get; set; }
}
```

**TournamentDetailResponse**
```csharp
public class TournamentDetailResponse
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string Organizer { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int? AnticipatedRoundCount { get; set; }      // NEW
    public List<TournamentRoundResponse> Rounds { get; set; } = [];
    public TournamentWinnerResponse? OverallWinner { get; set; }
    public string? HeroImageBase64 { get; set; }
    public string? HeroImageContentType { get; set; }
}
```

## Usage Examples

### Create a Tournament Without Rounds
```json
POST /stats/tournament
{
  "Name": "Summer Championship 2025",
  "Organizer": "PlayerName",
  "AnticipatedRoundCount": 5,
  "HeroImageBase64": "...",
  "HeroImageContentType": "image/jpeg"
}
```

### Add a Round to an Existing Tournament
```json
POST /stats/tournament/123/rounds
{
  "RoundId": "abc123def456"
}
```

### Create a Tournament With Initial Rounds (Still Supported)
```json
POST /stats/tournament
{
  "Name": "Winter Championship 2025",
  "Organizer": "PlayerName",
  "AnticipatedRoundCount": 3,
  "RoundIds": ["round1", "round2", "round3"],
  "HeroImageBase64": "...",
  "HeroImageContentType": "image/jpeg"
}
```

## Benefits
1. **Flexibility**: Organizers can create tournaments before rounds are played
2. **Better Planning**: `AnticipatedRoundCount` helps track progress
3. **RESTful Design**: New endpoint follows REST conventions with proper resource nesting
4. **Backward Compatible**: Existing functionality to create tournaments with rounds still works
5. **Progressive Enhancement**: Rounds can be added one at a time as they are played

## Migration Status
✅ Migration created and applied to database
✅ Build successful with no warnings or errors
✅ All validation logic preserved
✅ No linter errors

