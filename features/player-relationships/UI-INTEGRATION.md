# Neo4j Player Relationships - UI Integration

## Admin Cron Tab Integration

The Neo4j sync job has been added to the Admin Portal's Cron tab, allowing admins to trigger relationship syncs on-demand.

### Location

**Admin Portal → Cron Tab**

URL: `/admin` (requires Admin role)

### Features

**1. Neo4j Sync Job**
- Appears in the Cron tab when Neo4j is enabled
- Configurable sync window: 1, 7, 30, or 90 days
- Shows migration status (schema up-to-date or pending)
- Displays sync results (relationships processed, duration)

**2. Auto-Detection**
- UI automatically checks if Neo4j is enabled on mount
- If disabled: Shows disabled state with helpful message
- If enabled: Shows sync controls and migration status

**3. Migration Status Badge**
- ✓ up-to-date: All migrations applied
- ⚠ N pending: Schema needs updates (migrations pending)

### UI Components

**Sync Controls:**
```
Neo4j Sync (Player Relationships)
Sync player co-play data to graph database. Last 7 days. Schema: ✓ up-to-date

[Dropdown: 1 day, 7 days, 30 days, 90 days] [Sync Button]
```

**When Disabled:**
```
Neo4j Sync
Neo4j is not enabled in API configuration.
```

**Success Message:**
```
✓ Synced 1,234 relationships in 3,421ms
```

**Error Message:**
```
✗ Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json.
```

### Files Modified

**Frontend:**
- `ui/src/components/admin-data/AdminCronTab.vue` - Added Neo4j sync UI
- `ui/src/services/adminJobsService.ts` - Added API service functions

**Backend (already done):**
- `api/AdminData/AdminDataController.cs` - Neo4j sync endpoints

### API Calls

**Check Migration Status (on component mount):**
```typescript
GET /stats/admin/data/neo4j/migrations
Authorization: Bearer {token}

Response:
{
  "appliedCount": 1,
  "pendingCount": 0,
  "isUpToDate": true,
  "appliedMigrations": ["001_InitialSchema.cypher"],
  "pendingMigrations": []
}
```

**Trigger Sync:**
```typescript
POST /stats/admin/data/neo4j/sync
Authorization: Bearer {token}
Content-Type: application/json

Body:
{
  "days": 7
}

Response:
{
  "success": true,
  "relationshipsProcessed": 1234,
  "durationMs": 3421,
  "fromDate": "2026-02-10T13:08:04Z",
  "toDate": "2026-02-17T13:08:04Z",
  "errorMessage": null
}
```

### User Flow

**1. Admin opens Cron tab**
- UI checks Neo4j status
- If enabled: Shows sync controls
- If disabled: Shows disabled state

**2. Admin selects sync window**
- Dropdown: 1, 7, 30, or 90 days
- Default: 7 days (last week)

**3. Admin clicks "Sync"**
- Button shows "syncing..." state
- Other jobs disabled during sync
- Waits for completion (blocking)

**4. Results displayed**
- Success: Shows count and duration
- Error: Shows error message
- Migration status refreshed

### Styling

**Visual Indicators:**
- Green left border for Neo4j item (`#00d4aa`)
- Disabled state: 50% opacity, strikethrough name
- Success message: Green background
- Error message: Red background

### Testing Checklist

**With Neo4j Enabled:**
- [ ] Neo4j sync item appears in Cron tab
- [ ] Migration status badge shows correct state
- [ ] Dropdown has all day options (1, 7, 30, 90)
- [ ] Clicking "Sync" triggers the job
- [ ] Button shows "syncing..." during execution
- [ ] Success message displays relationships count
- [ ] Migration status refreshes after sync

**With Neo4j Disabled:**
- [ ] Disabled state appears
- [ ] Shows helpful message
- [ ] No sync button visible

**Error Handling:**
- [ ] Shows error if API returns failure
- [ ] Shows error if Neo4j not configured
- [ ] Shows error if authentication fails
- [ ] Refreshes token automatically if needed

### Screenshots

**Neo4j Enabled:**
```
┌─────────────────────────────────────────────────────┐
│ Neo4j Sync (Player Relationships)                   │
│ Sync player co-play data to graph database.         │
│ Last 7 days. Schema: ✓ up-to-date                  │
│                                                      │
│ [7 days ▼]  [Sync]                                  │
└─────────────────────────────────────────────────────┘
```

**Neo4j Disabled:**
```
┌─────────────────────────────────────────────────────┐
│ Neo4j Sync                                          │
│ Neo4j is not enabled in API configuration.         │
└─────────────────────────────────────────────────────┘
```

**During Sync:**
```
┌─────────────────────────────────────────────────────┐
│ Neo4j Sync (Player Relationships)                   │
│ Sync player co-play data to graph database.         │
│ Last 7 days. Schema: ✓ up-to-date                  │
│                                                      │
│ [7 days ▼]  [syncing...]  (disabled)                │
└─────────────────────────────────────────────────────┘
```

**After Success:**
```
┌─────────────────────────────────────────────────────┐
│ ✓ Synced 1,234 relationships in 3,421ms            │
│                                                      │
│ Neo4j Sync (Player Relationships)                   │
│ ...                                                  │
└─────────────────────────────────────────────────────┘
```

### Future Enhancements

**Planned:**
1. **Progress indicator** for long-running syncs (WebSocket updates)
2. **Last sync timestamp** display
3. **Sync history** (last 10 syncs with results)
4. **Automatic sync schedule** toggle (enable/disable background job)
5. **Query browser** - Run predefined relationship queries from UI

**Potential Features:**
- Visual graph of player relationships
- "Find co-players" search interface
- Clan/community detection results
- Player network visualization (D3.js)

### Development

**To test locally:**

1. Enable Neo4j in API config:
   ```json
   {
     "Neo4j": {
       "Uri": "bolt://localhost:7687",
       "Username": "neo4j",
       "Password": "bf1942stats"
     }
   }
   ```

2. Start Neo4j:
   ```bash
   ./scripts/start-neo4j.sh
   ```

3. Start API:
   ```bash
   cd api && dotnet run
   ```

4. Start UI:
   ```bash
   cd ui && npm run dev
   ```

5. Login as admin and navigate to `/admin`

6. Cron tab should show Neo4j sync with migration status

### Troubleshooting

**"Neo4j is not enabled in API configuration"**
→ Check `appsettings.Development.json` has `Neo4j.Uri` set

**Migration status shows "⚠ pending"**
→ Restart API to run migrations automatically

**Sync fails with timeout**
→ Reduce days parameter (try 7 instead of 90)

**UI shows disabled state but Neo4j is running**
→ Check API logs for migration errors
→ Verify API can connect to Neo4j

**Authentication errors**
→ Refresh page to get new token
→ Check admin role in JWT token

### Related Documentation

- **API Usage:** `features/player-relationships/API-USAGE.md`
- **Testing:** `features/player-relationships/TESTING.md`
- **Migrations:** `features/player-relationships/MIGRATIONS.md`
- **Quick Start:** `features/player-relationships/QUICK-START.md`
