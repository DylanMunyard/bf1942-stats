# Player Server Map Detail Integration

## Overview

Enhanced the player details view to show detailed server+map statistics using the modern `ServerMapDetailPanel` component instead of the old table-based view.

## Changes Made

### 1. Enhanced `ServerMapDetailPanel.vue`

**Added player filtering capability:**
- Added optional `playerName` prop to filter/highlight a specific player
- When `playerName` is provided:
  - Shows player badge in breadcrumb/context area
  - Passes player name to `MapRankingsPanel` for highlighting
  - Will show player-specific context in the panel

**File:** `ui/src/components/data-explorer/ServerMapDetailPanel.vue`

```vue
const props = defineProps<{
  serverGuid: string;
  mapName: string;
  playerName?: string; // Optional: filter/highlight specific player
}>();
```

### 2. Enhanced `PlayerServerMapStats.vue`

**Made map rows clickable:**
- Added `cursor-pointer` to table rows
- Added `@click` handler to emit `open-map-detail` event
- Added visual hint text: "Click a map row to see detailed stats"
- Added `@click.stop` to sessions link to prevent row click from triggering

**New event emitted:**
```typescript
emit('open-map-detail', mapName: string)
```

**File:** `ui/src/components/PlayerServerMapStats.vue`

### 3. Updated `PlayerDetails.vue`

**Added new handler:**
```typescript
const openPlayerServerMapDetail = (mapName: string) => {
  if (selectedServerGuid.value && selectedServerGuid.value !== '__all__') {
    selectedServerMapDetail.value = {
      serverGuid: selectedServerGuid.value,
      mapName: mapName
    };
  }
};
```

**Connected to PlayerServerMapStats:**
```vue
<PlayerServerMapStats
  @open-map-detail="openPlayerServerMapDetail"
  ...
/>
```

**Passed playerName to ServerMapDetailPanel:**
```vue
<ServerMapDetailPanel
  :server-guid="selectedServerMapDetail.serverGuid"
  :map-name="selectedServerMapDetail.mapName"
  :player-name="playerName"
  @close="closeServerMapDetail"
/>
```

## User Flow

### Before
1. Go to Player Details → Click a Server
2. See table of maps with stats
3. Click rank number → See rankings modal
4. Click map name → Go to sessions page (filtered)
5. **No way to see detailed map+server stats with player context**

### After
1. Go to Player Details → Click a Server
2. See table of maps with stats **+ hint text**
3. **Click anywhere on a map row → Opens detailed panel with:**
   - Player badge showing context (e.g., "PlayerName on BF1942 • ServerName")
   - Map activity stats
   - Activity heatmap
   - Team win statistics
   - **Rankings with player highlighted**
   - Recent sessions for that map
4. Click rank number → See full rankings
5. Click map name link → Go to sessions page (existing behavior preserved)

## Benefits

1. **Reuses modern component**: Leverages the well-designed `ServerMapDetailPanel` instead of maintaining old code
2. **Consistent UX**: Same look and feel as DataExplorer's map detail views
3. **Player context**: Rankings automatically highlight the player in question
4. **Mobile-friendly**: Inherits all the mobile optimizations we just added
5. **More information**: Shows activity patterns, win stats, recent sessions, etc.

## Future Enhancements

Possible improvements:
- Add player-specific stats to the activity summary (e.g., "You played 15 rounds, avg score 2,450")
- Filter recent sessions to only show sessions where the player participated
- Add quick comparison: "You rank #5 out of 127 players on this map"
- Show player's historical performance trend on this map

## Testing Checklist

- [ ] Click a server from PlayerDetails Servers list
- [ ] Click a map row in the map stats table
- [ ] Verify ServerMapDetailPanel opens
- [ ] Verify player name badge appears in breadcrumb
- [ ] Verify player is highlighted in rankings
- [ ] Verify rank button still opens full rankings
- [ ] Verify map name link still goes to sessions page
- [ ] Test on mobile (panel should be full-width overlay)
- [ ] Test on desktop (panel should be modal with padding)
