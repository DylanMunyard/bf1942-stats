# Team Victory Processor Idempotency Implementation

## Overview
This document describes the changes made to make the `TeamVictoryProcessor` idempotent and fix the date processing logic in `GamificationService`.

## Problems Solved

### 1. Idempotency Issue
**Problem**: The `TeamVictoryProcessor` would create duplicate achievements when reprocessing the same rounds, as it didn't check for existing achievements.

**Solution**: 
- Created a new ClickHouse table schema using `ReplacingMergeTree(version)` engine for automatic deduplication
- Created `PlayerAchievementsMigrationService` to migrate existing data to the new idempotent schema
- The ReplacingMergeTree automatically handles duplicates at the database level

### 2. Date Processing Issue
**Problem**: The `GetLastTeamVictoryProcessedTimestampAsync()` method used `MIN()` of both achievement types, causing unnecessary reprocessing when one type had no records (defaulting to `'1900-01-01'`).

**Solution**:
- Changed logic to use `MAX()` of both achievement types combined
- Added 120-minute buffer by subtracting from the max timestamp
- This ensures we don't reprocess the same achievements while providing a safe buffer for late-arriving data

## Files Modified

### 1. `/junie-des-1942stats/ClickHouse/PlayerAchievementsMigrationService.cs` (NEW)
- Migration service similar to `PlayerMetricsMigrationService`
- Creates new `player_achievements_v2` table with `ReplacingMergeTree(version)` engine
- Provides migration, switch, rollback, and cleanup methods
- Uses `processed_at` as the version column for deduplication

### 2. `/junie-des-1942stats/Data/Migrations/PlayerAchievementsMigrationController.cs` (NEW)
- API controller similar to `PlayerMetricsMigrationController`
- Provides REST endpoints for migration operations
- Includes proper logging and error handling
- Routes: `/stats/admin/PlayerAchievementsMigration/{migrate|switch|rollback|cleanup}`

### 3. `/junie-des-1942stats/Program.cs`
**Changes:**
- Added HTTP client registration for `PlayerAchievementsMigrationService`
- Added singleton service registration with proper ClickHouse URL configuration
- Configured 5-minute timeout for migration operations

### 4. `/junie-des-1942stats/Gamification/Services/ClickHouseGamificationService.cs`
**Changes to `GetLastTeamVictoryProcessedTimestampAsync()`:**
- Changed from `MIN(COALESCE(max_processed, '1900-01-01'))` to `MAX(processed_at)`
- Combined both achievement types in a single query using `IN` clause
- Added 120-minute buffer: `return lastProcessed.AddMinutes(-120)`
- Updated documentation to reflect the new logic

### 3. `/junie-des-1942stats/Gamification/Services/TeamVictoryProcessor.cs`
**Changes:**
- Added `GetExistingAchievementKeysAsync()` method (currently returns empty set as ReplacingMergeTree handles deduplication)
- Added `GenerateAchievementKey()` method for future use if needed
- Simplified logic to rely on ClickHouse-level deduplication
- Maintained the same achievement creation logic but now idempotent at the database level

## Database Schema Changes

### New Table Structure
```sql
CREATE TABLE player_achievements_v2
(
    player_name String,
    achievement_type String,
    achievement_id String,
    achievement_name String,
    tier String,
    value UInt32,
    achieved_at DateTime,
    processed_at DateTime,
    server_guid String,
    map_name String,
    round_id String,
    metadata String,
    version DateTime  -- Version column for ReplacingMergeTree deduplication
)
ENGINE = ReplacingMergeTree(version)
PARTITION BY toYYYYMM(achieved_at)
ORDER BY (player_name, achievement_type, achievement_id, round_id, achieved_at)
```

### Key Differences from Original
- Added `version DateTime` column
- Changed engine from `MergeTree()` to `ReplacingMergeTree(version)`
- Updated ORDER BY clause to include deduplication key components

## Migration Process

### Option 1: API Endpoints (Recommended)
1. **Run Migration**: `POST /stats/admin/PlayerAchievementsMigration/migrate`
2. **Switch Tables**: `POST /stats/admin/PlayerAchievementsMigration/switch`
3. **Rollback** (if needed): `POST /stats/admin/PlayerAchievementsMigration/rollback`
4. **Cleanup**: `POST /stats/admin/PlayerAchievementsMigration/cleanup`

### Option 2: Service Methods (Programmatic)
1. **Run Migration**: Use `PlayerAchievementsMigrationService.MigrateToReplacingMergeTreeAsync()`
2. **Verify**: The service includes verification to ensure data integrity
3. **Switch Tables**: Use `SwitchToNewTableAsync()` to atomically switch to the new table
4. **Rollback** (if needed): Use `RollbackTableSwitchAsync()` if issues arise
5. **Cleanup**: Use `CleanupOldTableAsync()` to remove backup table

### API Endpoints

#### POST /stats/admin/PlayerAchievementsMigration/migrate
Migrates the `player_achievements` table to use ReplacingMergeTree for idempotency.

**Response:**
```json
{
  "success": true,
  "totalMigrated": 150000,
  "durationMs": 30000,
  "verificationPassed": true,
  "errorMessage": null,
  "executedAtUtc": "2024-01-15T10:30:00Z"
}
```

#### POST /stats/admin/PlayerAchievementsMigration/switch
Switches tables atomically after migration is complete.

#### POST /stats/admin/PlayerAchievementsMigration/rollback
Rolls back the table switch if issues arise.

#### POST /stats/admin/PlayerAchievementsMigration/cleanup
Cleans up backup tables after successful migration.

## Benefits

1. **Idempotency**: Can safely reprocess the same rounds without creating duplicates
2. **Performance**: ReplacingMergeTree handles deduplication efficiently at the storage level
3. **Data Integrity**: Automatic deduplication prevents inconsistent achievement counts
4. **Reduced Processing**: Fixed date logic prevents unnecessary reprocessing of old rounds
5. **Buffer Safety**: 120-minute buffer ensures late-arriving data is still processed

## Usage

After migration, the `TeamVictoryProcessor` can be run multiple times on the same data without creating duplicates. The processor will:

1. Process rounds since the last achievement timestamp minus 120 minutes
2. Create achievements for eligible players
3. Let ClickHouse automatically deduplicate any duplicates based on the composite key
4. Update the processed timestamp for future incremental processing

## Testing Recommendations

1. Test migration on a copy of production data
2. Verify achievement counts before and after migration
3. Test reprocessing the same rounds to ensure no duplicates
4. Validate that the 120-minute buffer works correctly for edge cases
5. Test rollback procedure in case of issues
