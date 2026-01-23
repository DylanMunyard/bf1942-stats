# SQLite Corruption Recovery Playbook

Quick reference for recovering from SQLite database corruption.

---

## Prerequisites

- `kubectl` access to the cluster
- Database on a PVC with available space (~2x database size)

---

## Step 1: Isolate

```bash
# Scale down everything that touches the database
kubectl scale deployment bf42-stats -n bf42-stats --replicas=0
kubectl scale deployment sqlite-browser -n bf42-stats --replicas=0

# Verify
kubectl get pods -n bf42-stats
```

---

## Step 2: Deploy Recovery Pod

```bash
kubectl apply -f deploy/disaster-recovery/sqlite-recovery.yaml
kubectl exec -it -n bf42-stats sqlite-recovery -- /bin/sh
cd /mnt/data
```

---

## Step 3: Diagnose

```bash
# Check files
ls -la playertracker.db*

# Check header (should show "SQLite format 3")
head -c 16 playertracker.db | od -c

# Try integrity check (will likely fail if corrupt)
sqlite3 playertracker.db "PRAGMA integrity_check;" 2>&1 | head -20
```

---

## Step 4: Recover Data

```bash
# Check available disk space (need ~2x DB size)
df -h /mnt/data

# Run recovery (outputs SQL, doesn't modify original)
sqlite3 playertracker.db ".recover" > /mnt/data/recovery.sql

# Monitor progress
ls -lh /mnt/data/recovery.sql
```

**Time estimate:** ~5-10 min per 10GB

---

## Step 5: Rebuild Database

```bash
# Verify recovery SQL looks good
head -50 /mnt/data/recovery.sql
tail -30 /mnt/data/recovery.sql

# Build new database
sqlite3 /mnt/data/playertracker_new.db < /mnt/data/recovery.sql
```

**Time estimate:** ~20-30 min per 10GB

---

## Step 6: Verify

```bash
# Check integrity (optional but recommended)
sqlite3 /mnt/data/playertracker_new.db "PRAGMA integrity_check;"

# Check row counts
sqlite3 /mnt/data/playertracker_new.db "
SELECT 'Players', COUNT(*) FROM Players
UNION ALL SELECT 'Rounds', COUNT(*) FROM Rounds
UNION ALL SELECT 'PlayerSessions', COUNT(*) FROM PlayerSessions;
"

# Check recent data exists
sqlite3 /mnt/data/playertracker_new.db "SELECT MAX(LastSeen) FROM Players;"
```

---

## Step 7: Swap

```bash
cd /mnt/data

# Backup corrupt database
mv playertracker.db playertracker.db.corrupt

# Swap in recovered database
mv playertracker_new.db playertracker.db

# Verify
ls -lh playertracker.db*

# Clean up (optional, after confirming recovery works)
rm /mnt/data/recovery.sql
```

---

## Step 8: Restore Service

```bash
# Delete recovery pod
kubectl delete pod sqlite-recovery -n bf42-stats

# Start API
kubectl scale deployment bf42-stats -n bf42-stats --replicas=1

# Watch startup
kubectl get pods -n bf42-stats -w
kubectl logs -f deployment/bf42-stats -n bf42-stats
```

---

## Rollback (if recovery failed)

```bash
cd /mnt/data
mv playertracker.db playertracker_new.db
mv playertracker.db.corrupt playertracker.db
```

---

## Key Commands Reference

| Command | Purpose |
|---------|---------|
| `sqlite3 db ".recover" > out.sql` | Extract data from corrupt DB |
| `sqlite3 new.db < out.sql` | Build DB from SQL |
| `PRAGMA integrity_check;` | Verify DB structure |
| `PRAGMA wal_checkpoint(TRUNCATE);` | Force WAL merge |
| `head -c 16 db \| od -c` | Check SQLite header |

---

## Why .recover Works When Nothing Else Does

Normal SQLite operations use B-tree traversal. If any page in the path is corrupt, operations fail.

`.recover` bypasses the B-tree entirely:
- Scans file page-by-page sequentially
- Extracts records directly from leaf pages
- Reconstructs schema from what it finds
- Skips corrupted pages
