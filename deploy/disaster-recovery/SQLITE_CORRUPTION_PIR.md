# SQLite Corruption Incident - Post-Incident Review

**Date:** 2026-01-23  
**Duration:** ~2 hours  
**Severity:** Critical (database corruption, full site outage)  
**Outcome:** Full recovery with no data loss

---

## Summary

SQLite database corruption caused by WAL mode conflict between the main API and sqlite-web browser due to subPath volume mounting.

## Root Cause

The `sqlite-browser` deployment mounted only the database file via `subPath`:

```yaml
volumeMounts:
  - name: stats-data
    mountPath: /data/playertracker.db
    subPath: playertracker.db  # <-- ROOT CAUSE
```

This prevented sqlite-web from seeing the WAL and SHM files (`playertracker.db-wal`, `playertracker.db-shm`). When an edit was made via sqlite-web while the API was running:

1. API was writing to WAL file
2. sqlite-web couldn't see WAL, saw stale data in main DB
3. sqlite-web wrote directly to main DB, corrupting B-tree pages
4. B-tree internal nodes were overwritten, making large portions of data unreachable

## Detection

Error in API logs:
```
Microsoft.Data.Sqlite.SqliteException (0x80004005): SQLite Error 11: 'database disk image is malformed'
```

## Resolution

Used SQLite's `.recover` command to extract data page-by-page (bypassing corrupted B-tree), then rebuilt a clean database.

### Recovery Commands

```bash
# 1. Extract data from corrupt DB
sqlite3 playertracker.db ".recover" > recovery.sql

# 2. Build new database
sqlite3 playertracker_new.db < recovery.sql

# 3. Verify and swap
sqlite3 playertracker_new.db "PRAGMA integrity_check;"
mv playertracker.db playertracker.db.corrupt
mv playertracker_new.db playertracker.db
```

## Timeline

| Time | Event |
|------|-------|
| T+0 | Edit made via sqlite-web |
| T+? | API begins returning corruption errors |
| T+10m | API scaled down, site offline |
| T+20m | Recovery pod deployed |
| T+25m | Diagnostics confirm B-tree corruption |
| T+35m | `.recover` started |
| T+55m | Recovery SQL complete (22.6GB) |
| T+90m | New database rebuilt (16GB) |
| T+100m | Database swapped, API restored |

## What Worked

- SQLite `.recover` command successfully extracted all data
- Recovery pod with proper volume mounts enabled diagnostics
- Keeping corrupt database as backup allowed safe experimentation

## What Didn't Work

- Azure blob backups were also corrupt (used `cp` instead of SQLite backup API)
- Initial resource limits on recovery pod (20m CPU) too restrictive

## Action Items

| Priority | Action | Status |
|----------|--------|--------|
| P0 | Fix sqlite-browser to mount full directory, not subPath | TODO |
| P0 | Add warning: scale down API before using sqlite-browser | TODO |
| P1 | Fix backup script to use `sqlite3 .backup` instead of `cp` | TODO |
| P2 | Consider switching to PostgreSQL for multi-connection safety | TODO |

## Prevention

### Option A: Fix sqlite-browser mount
```yaml
volumeMounts:
  - name: stats-data
    mountPath: /mnt/data  # Full directory, not subPath
```

### Option B: Operational procedure
Always scale down API before using sqlite-browser:
```bash
kubectl scale deployment bf42-stats -n bf42-stats --replicas=0
# ... use sqlite-browser ...
kubectl scale deployment bf42-stats -n bf42-stats --replicas=1
```

### Option C: Disable sqlite-browser in production
Keep it scaled to 0, only enable when needed with API down.

---

## Recovery Pod Spec

See: `sqlite-recovery.yaml` in this directory.
