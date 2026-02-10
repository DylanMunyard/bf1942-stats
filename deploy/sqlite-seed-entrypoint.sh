#!/bin/sh
set -e

DB_PATH="/mnt/data/playertracker.db"

if [ -z "$BLOB_SAS_URL" ]; then
  echo "ERROR: BLOB_SAS_URL environment variable is not set"
  exit 1
fi

# Safety check: don't overwrite existing DB unless OVERWRITE=true
if [ -f "$DB_PATH" ] && [ "$OVERWRITE" != "true" ]; then
  echo "ERROR: $DB_PATH already exists. Set OVERWRITE=true to replace it."
  exit 1
fi

echo "Downloading database files from blob storage..."

curl -fSL -o "$DB_PATH" "$BLOB_SAS_URL"
echo "Downloaded playertracker.db: $(ls -lh "$DB_PATH" | awk '{print $5}')"

if [ -n "$BLOB_SAS_URL_WAL" ]; then
  curl -fSL -o "${DB_PATH}-wal" "$BLOB_SAS_URL_WAL"
  echo "Downloaded playertracker.db-wal: $(ls -lh "${DB_PATH}-wal" | awk '{print $5}')"
fi

if [ -n "$BLOB_SAS_URL_SHM" ]; then
  curl -fSL -o "${DB_PATH}-shm" "$BLOB_SAS_URL_SHM"
  echo "Downloaded playertracker.db-shm: $(ls -lh "${DB_PATH}-shm" | awk '{print $5}')"
fi

# Verify SQLite header
HEADER=$(head -c 16 "$DB_PATH" | strings)
if echo "$HEADER" | grep -q "SQLite format 3"; then
  echo "SQLite header check: OK"
else
  echo "ERROR: File does not have a valid SQLite header"
  rm -f "$DB_PATH" "${DB_PATH}-wal" "${DB_PATH}-shm"
  exit 1
fi

# Run integrity check
echo "Running PRAGMA quick_check..."
RESULT=$(sqlite3 "$DB_PATH" "PRAGMA quick_check;" 2>&1)
if [ "$RESULT" = "ok" ]; then
  echo "Integrity check: OK"
else
  echo "ERROR: Integrity check failed: $RESULT"
  rm -f "$DB_PATH" "${DB_PATH}-wal" "${DB_PATH}-shm"
  exit 1
fi

echo ""
echo "========================================="
echo "Seed complete. Database is ready."
echo "========================================="
echo ""

echo "Container will now exit. Delete this service and start the API."
