# Competitive Rankings Feature for Data Explorer

## Overview
Add competitive ranking information to the Data Explorer Breakdown on PlayerDetails.vue to show where players stand among their peers.

## Implementation Plan

### 1. Backend Changes

#### Add new endpoints to DataExplorerController:
- `GET /stats/data-explorer/players/{playerName}/competitive-rankings`
  - Returns player's current rankings across all maps
  - Includes rank position, total players, percentile
  - Optional time period filter (30d, 60d, all-time)
  
- `GET /stats/data-explorer/players/{playerName}/ranking-timeline`  
  - Returns historical ranking data for trend visualization
  - Monthly snapshots of rank positions
  - Can filter by specific map

#### New DTOs:
```csharp
public record PlayerCompetitiveRankingsResponse(
    string PlayerName,
    List<MapRankingDto> MapRankings,
    RankingSummaryDto Summary,
    DateRangeDto DateRange
);

public record MapRankingDto(
    string MapName,
    int Rank,
    int TotalPlayers,
    double Percentile,
    int TotalScore,
    double KdRatio,
    string Trend // "up", "down", "stable"
);

public record RankingSummaryDto(
    int TotalMapsPlayed,
    int Top10Rankings,
    int Top25Rankings,
    double AveragePercentile,
    string BestRankedMap,
    int BestRank
);

public record RankingTimelineResponse(
    string PlayerName,
    string? MapName,
    List<RankingSnapshotDto> Timeline
);

public record RankingSnapshotDto(
    int Year,
    int Month,
    int Rank,
    int TotalPlayers,
    double Percentile,
    int TotalScore
);
```

### 2. Frontend Changes

#### New Component: PlayerCompetitiveRankings.vue
Location: `@ui/src/components/data-explorer/PlayerCompetitiveRankings.vue`

Features:
- Tab view: "Current Rankings" | "Rank Timeline"
- Current Rankings tab:
  - List of maps with rank badges
  - Percentile indicators (top 1%, 5%, 10%, 25%)
  - Visual rank trend arrows
  - Click to see full map leaderboard
- Timeline tab:
  - Line chart showing rank progression over months
  - Map selector to filter specific map
  - Percentile band visualization

#### Integration into PlayerDetails.vue
Add new card in the Data Explorer section:
```vue
<div class="explorer-card">
  <div class="explorer-card-header">
    <h3 class="explorer-card-title">COMPETITIVE RANKINGS</h3>
    <p class="text-[10px] text-neutral-500 font-mono mt-1">YOUR POSITION AMONG ALL PLAYERS</p>
  </div>
  <div class="explorer-card-body">
    <PlayerCompetitiveRankings
      :player-name="playerName"
      :game="playerPanelGame"
    />
  </div>
</div>
```

### 3. Visual Design

#### Rank Badges:
- 🥇 #1 - Gold with glow effect
- 🥈 #2 - Silver  
- 🥉 #3 - Bronze
- Top 10 - Neon cyan
- Top 25 - Neon purple
- Top 50 - White
- Others - Neutral gray

#### Percentile Indicators:
- Top 1% - "ELITE" badge with neon gold
- Top 5% - "MASTER" badge with neon cyan
- Top 10% - "EXPERT" badge with neon purple
- Top 25% - "VETERAN" badge with white

#### Timeline Chart:
- Dark theme with neon accents
- Percentile bands in background (1%, 5%, 10%, 25% lines)
- Player's rank line with glow effect
- Hover tooltips with exact rank/percentile

### 4. Performance Considerations

- Cache ranking calculations for 1 hour
- Use existing PlayerMapStats aggregates
- Batch timeline queries by year
- Lazy load timeline data on tab switch

### 5. Example Query for Rankings

```sql
WITH PlayerScores AS (
    SELECT 
        PlayerName,
        MapName,
        SUM(TotalScore) as Score,
        SUM(TotalKills) as Kills,
        SUM(TotalDeaths) as Deaths
    FROM PlayerMapStats
    WHERE Year = @Year AND Month = @Month
    GROUP BY PlayerName, MapName
),
RankedPlayers AS (
    SELECT 
        PlayerName,
        MapName,
        Score,
        Kills,
        Deaths,
        RANK() OVER (PARTITION BY MapName ORDER BY Score DESC) as Rank,
        COUNT(*) OVER (PARTITION BY MapName) as TotalPlayers
    FROM PlayerScores
)
SELECT * FROM RankedPlayers
WHERE PlayerName = @PlayerName
```

## Benefits

1. **Engagement**: Players can see their competitive standing
2. **Goals**: Clear targets (reach top 10, top 5%, etc.)  
3. **Progress**: Timeline shows improvement over time
4. **Discovery**: Click through to see who they're competing against
5. **Social**: Share achievements when reaching milestones

## Future Enhancements

1. Add server-specific rankings (not just map-wide)
2. Ranking predictions based on current performance
3. Achievement unlocks for reaching certain ranks
4. Leaderboard notifications when rank changes significantly
5. Seasonal rankings (quarterly competitions)

## Implementation Status

### ✅ Completed

1. **Backend API**:
   - Added `PlayerCompetitiveRankingsDto.cs` with all necessary DTOs
   - Extended `IDataExplorerService` with two new methods:
     - `GetPlayerCompetitiveRankingsAsync` - Current rankings across all maps
     - `GetPlayerRankingTimelineAsync` - Historical ranking data
   - Added controller endpoints in `DataExplorerController`:
     - `GET /stats/data-explorer/players/{playerName}/competitive-rankings`
     - `GET /stats/data-explorer/players/{playerName}/ranking-timeline`
   - Implemented methods in `DataExplorerServiceOptimized.cs` using efficient SQL queries

2. **Frontend Component**:
   - Created `PlayerCompetitiveRankings.vue` with:
     - Hero section showing player's overall ranking tier (Elite/Master/Expert/Veteran)
     - Current Rankings tab showing all map rankings with trends
     - Timeline tab with interactive chart showing rank progression over 12 months
     - Click-through navigation to full map leaderboards
     - Responsive design matching the explorer theme

3. **Integration**:
   - Added component to PlayerDetails.vue in the left column below Data Explorer Breakdown
   - Component loads automatically when viewing a player profile

### Key Features Delivered

- **Percentile-based rankings**: Shows where player stands (top 1%, 5%, 10%, etc.)
- **Trend indicators**: Shows if rank is improving, declining, or stable
- **Visual hierarchy**: Gold/Silver/Bronze medals, color-coded percentiles
- **Historical view**: 12-month timeline chart with rank and percentile tracking
- **Map filtering**: Timeline can show overall average or specific map progression
- **Performance**: Uses existing PlayerMapStats aggregates, no new tables needed

### Usage

Players can now:
1. See their competitive rankings directly on their profile
2. Track rank changes over time
3. Click on any map to see the full leaderboard
4. Understand their percentile position among all players
5. View historical progression to see improvement

### Compilation Fixed

All compilation errors have been resolved:
- Fixed `GetYearMonthFromInstant` method not found by using `DateTime.UtcNow` pattern
- Fixed SQL query result mapping by creating proper query result classes
- Fixed type conversion issues between `long` and `int` for rank values
- Added missing query result classes: `MapRankResult`, `MonthlyRankingResult`
- Updated existing `PlayerRankingQueryResult` to include all necessary fields

### Runtime Fix - SQL Query Errors

1. **Fixed SQLite Error "no such column: sms.Game":**
   - Removed incorrect JOIN with ServerMapStats table on Game column
   - ServerMapStats doesn't have a Game column - game info is only in Servers table
   - Simplified queries to work with existing data structure
   - Rankings now show across all games (since PlayerMapStats aggregates all games together)
   - This avoids complex JOINs and works with existing data without requiring backfill

2. **Fixed "The required column 'ServerGuid' was not present" error:**
   - Created a new `CompetitiveRankingResult` class specifically for competitive rankings
   - This avoids mismatches with the existing `PlayerRankingQueryResult` that expects ServerGuid
   - The competitive rankings query now uses SELECT * which matches the CTE output exactly
   - Cleaner separation of concerns between different ranking queries