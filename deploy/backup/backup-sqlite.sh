#!/bin/bash

# SQLite Backup Script for BF1942 Stats
# This script creates compressed backups of the SQLite database

set -euo pipefail

# Configuration
DB_PATH="${DB_PATH:-/mnt/data/playertracker.db}"
BACKUP_DIR="${BACKUP_DIR:-/mnt/backup}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_NAME="playertracker_${TIMESTAMP}.db"
COMPRESSED_BACKUP="${BACKUP_NAME}.gz"

# Logging function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# Error handling
error_exit() {
    log "ERROR: $1"
    exit 1
}

# Check if source database exists
if [[ ! -f "$DB_PATH" ]]; then
    error_exit "Source database not found: $DB_PATH"
fi

# Create backup directory if it doesn't exist
mkdir -p "$BACKUP_DIR" || error_exit "Failed to create backup directory: $BACKUP_DIR"

# Check available disk space (require at least 100MB free)
AVAILABLE_SPACE=$(df "$BACKUP_DIR" | awk 'NR==2 {print $4}')
if [[ $AVAILABLE_SPACE -lt 102400 ]]; then
    error_exit "Insufficient disk space. Available: ${AVAILABLE_SPACE}KB, Required: 100MB"
fi

log "Starting SQLite backup process..."
log "Source: $DB_PATH"
log "Destination: $BACKUP_DIR/$COMPRESSED_BACKUP"

# Perform SQLite backup using .backup command for consistency
log "Creating SQLite backup..."
sqlite3 "$DB_PATH" ".backup $BACKUP_DIR/$BACKUP_NAME" || error_exit "SQLite backup failed"

# Verify backup file was created
if [[ ! -f "$BACKUP_DIR/$BACKUP_NAME" ]]; then
    error_exit "Backup file was not created: $BACKUP_DIR/$BACKUP_NAME"
fi

# Get original file size for logging
ORIGINAL_SIZE=$(stat -c%s "$DB_PATH")
BACKUP_SIZE=$(stat -c%s "$BACKUP_DIR/$BACKUP_NAME")

log "Original database size: $(numfmt --to=iec $ORIGINAL_SIZE)"
log "Backup file size: $(numfmt --to=iec $BACKUP_SIZE)"

# Compress the backup
log "Compressing backup..."
gzip "$BACKUP_DIR/$BACKUP_NAME" || error_exit "Compression failed"

# Verify compressed file was created
if [[ ! -f "$BACKUP_DIR/$COMPRESSED_BACKUP" ]]; then
    error_exit "Compressed backup file was not created: $BACKUP_DIR/$COMPRESSED_BACKUP"
fi

COMPRESSED_SIZE=$(stat -c%s "$BACKUP_DIR/$COMPRESSED_BACKUP")
COMPRESSION_RATIO=$(echo "scale=1; $COMPRESSED_SIZE * 100 / $BACKUP_SIZE" | bc)

log "Compressed backup size: $(numfmt --to=iec $COMPRESSED_SIZE) (${COMPRESSION_RATIO}% of original)"

# Clean up old backups
log "Cleaning up backups older than $RETENTION_DAYS days..."
find "$BACKUP_DIR" -name "playertracker_*.db.gz" -type f -mtime +$RETENTION_DAYS -exec rm {} \; || log "WARNING: Failed to clean up some old backups"

# Count remaining backups
BACKUP_COUNT=$(find "$BACKUP_DIR" -name "playertracker_*.db.gz" -type f | wc -l)
log "Backup completed successfully. Total backups: $BACKUP_COUNT"

# Set appropriate file permissions
chmod 640 "$BACKUP_DIR/$COMPRESSED_BACKUP"

# Output backup information for Kubernetes logs
cat << EOF
BACKUP_STATUS=SUCCESS
BACKUP_FILE=$BACKUP_DIR/$COMPRESSED_BACKUP
ORIGINAL_SIZE=$ORIGINAL_SIZE
COMPRESSED_SIZE=$COMPRESSED_SIZE
COMPRESSION_RATIO=${COMPRESSION_RATIO}%
BACKUP_COUNT=$BACKUP_COUNT
EOF

log "SQLite backup process completed successfully!"