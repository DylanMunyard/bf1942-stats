# Player Map Details Feature

## Overview
When users click on a map from their Data Explorer Breakdown on the player details page, they now see personalized map statistics instead of generic map data. This provides a more contextual and useful view of their performance on that specific map.

## Problem Solved
Previously, clicking a map in the player's Data Explorer Breakdown would show generic map statistics (win rates, server rotations, etc.) which wasn't relevant to the player's context. Users expected to see their own performance details on that map.

## Implementation

### Frontend Changes

1. **New Component: PlayerMapDetailPanel.vue**
   - Shows player-specific performance on the selected map
   - Displays aggregated stats: total score, kills, deaths, K/D ratio
   - Features ranking tabs to see position among other players
   - Includes server breakdown showing performance on each server
   - Supports filtering rankings by server

2. **Updated PlayerDetails.vue**
   - Now uses `PlayerMapDetailPanel` instead of `MapDetailPanel` when clicking maps
   - Passes player name and game context to the new component

### Backend Changes

1. **New API Endpoint**
   - `GET /stats/data-explorer/players/{playerName}/map-stats/{mapName}`
   - Returns player's aggregated stats for the map
   - Includes breakdown by server

2. **New DTOs**
   - `PlayerMapDetailResponse` - Main response wrapper
   - `PlayerMapAggregatedStats` - Overall stats for the map
   - `PlayerMapServerBreakdown` - Stats broken down by server

3. **Service Implementation**
   - `GetPlayerMapStatsAsync` in DataExplorerService
   - Queries PlayerMapStats table for aggregated data
   - Groups by server for breakdown view
   - Respects time range filtering (default 60 days)

## Features

### Player Performance Summary
- Total score, kills, deaths, K/D ratio
- Visual stat cards with neon theme styling
- Time played on the map

### Competitive Rankings
- Multiple sorting options: by score, kills, K/D ratio, kill rate
- **Prominent Player Position Card**: Always visible sticky card showing:
  - Large rank display (#X of Y players)
  - Percentile badge (TOP X%)
  - Current metric value
  - Jump to position button when not visible
- **Context Players Section**: When browsing other pages, shows 5 players around your rank
- **Smart Navigation**: 
  - Automatically calculates which page the player is on
  - Jump button takes you directly to your position
  - Smooth scroll and highlight animation
- Highlights the player's row with "(YOU)" label
- Server filter for more specific rankings

### Server Breakdown
- Lists all servers where player has played this map
- Shows performance metrics per server
- Click-through navigation to server details
- Sorted by score

## User Experience

1. **Contextual Navigation**: Users stay in their player context while exploring map data
2. **Competitive Insight**: See how they rank against other players
3. **Server Comparison**: Understand which servers they perform best on
4. **Quick Access**: All relevant data in one modal without page navigation

## Technical Benefits

1. **Reuses Existing Data**: No new database tables or columns required
2. **Efficient Queries**: Uses indexed PlayerMapStats aggregates
3. **Consistent UI**: Follows the existing Data Explorer design patterns
4. **Responsive Design**: Works on mobile and desktop

## Future Enhancements

1. Add historical performance chart for the map
2. Show best rounds/sessions on this map
3. Add achievement progress related to the map
4. Include weapon statistics for the map
5. Show team preference/win rate on the map