#!/bin/bash

# ClickHouse Backup Script for BF1942 Stats
# This script creates compressed backups of ClickHouse database tables

set -euo pipefail

# Configuration
CLICKHOUSE_URL="${CLICKHOUSE_URL:-http://clickhouse-service.clickhouse:8123}"
CLICKHOUSE_USER="${CLICKHOUSE_USER:-default}"
CLICKHOUSE_PASSWORD="${CLICKHOUSE_PASSWORD:-}"
CLICKHOUSE_DATABASE="${CLICKHOUSE_DATABASE:-default}"
BACKUP_DIR="${BACKUP_DIR:-/mnt/backup}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
TIMESTAMP=$(date +"%Y%m%d_%H%M%S")
BACKUP_PREFIX="clickhouse_${TIMESTAMP}"

# Tables to backup (based on your application)
TABLES=(
    "player_metrics"
    "player_rounds"
    "player_achievements"
    "gamification_achievements"
    "team_killer_metrics"
    "server_statistics"
)

# Logging function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# Error handling
error_exit() {
    log "ERROR: $1"
    exit 1
}

# ClickHouse query function
clickhouse_query() {
    local query="$1"
    local auth_header=""
    
    if [[ -n "$CLICKHOUSE_PASSWORD" ]]; then
        auth_header="--header X-ClickHouse-Key:$CLICKHOUSE_PASSWORD"
    fi
    
    curl -s --fail \
        --header "X-ClickHouse-User:$CLICKHOUSE_USER" \
        $auth_header \
        --data "$query" \
        "$CLICKHOUSE_URL" || return 1
}

# Test ClickHouse connectivity
test_clickhouse_connection() {
    log "Testing ClickHouse connectivity..."
    local result
    result=$(clickhouse_query "SELECT 1") || error_exit "Cannot connect to ClickHouse at $CLICKHOUSE_URL"
    
    if [[ "$result" != "1" ]]; then
        error_exit "ClickHouse connection test failed. Response: $result"
    fi
    
    log "ClickHouse connectivity test passed"
}

# Get table list from ClickHouse
get_existing_tables() {
    local query="SELECT name FROM system.tables WHERE database = '$CLICKHOUSE_DATABASE' AND engine NOT LIKE '%View%' FORMAT TSV"
    clickhouse_query "$query" | grep -E "^($(IFS='|'; echo "${TABLES[*]}"))\$" || echo ""
}

# Backup single table
backup_table() {
    local table="$1"
    local backup_file="$BACKUP_DIR/${BACKUP_PREFIX}_${table}.csv.gz"
    
    log "Backing up table: $table"
    
    # Check if table exists and get row count
    local row_count
    row_count=$(clickhouse_query "SELECT COUNT(*) FROM ${CLICKHOUSE_DATABASE}.${table}") || {
        log "WARNING: Cannot access table $table, skipping..."
        return 0
    }
    
    if [[ "$row_count" == "0" ]]; then
        log "Table $table is empty, creating empty backup file"
        touch "$backup_file"
        return 0
    fi
    
    log "Table $table contains $row_count rows"
    
    # Create the backup query
    local query="SELECT * FROM ${CLICKHOUSE_DATABASE}.${table} FORMAT CSV"
    
    # Execute backup with compression
    if clickhouse_query "$query" | gzip > "$backup_file"; then
        local file_size
        file_size=$(stat -c%s "$backup_file")
        log "Table $table backup completed: $(numfmt --to=iec $file_size)"
    else
        error_exit "Failed to backup table: $table"
    fi
    
    # Verify backup file was created and is not empty (unless table was empty)
    if [[ ! -f "$backup_file" ]] || ([[ "$row_count" != "0" ]] && [[ ! -s "$backup_file" ]]); then
        error_exit "Backup file verification failed for table: $table"
    fi
    
    # Set appropriate permissions
    chmod 640 "$backup_file"
}

# Main backup process
main() {
    log "Starting ClickHouse backup process..."
    log "ClickHouse URL: $CLICKHOUSE_URL"
    log "Database: $CLICKHOUSE_DATABASE"
    log "Backup directory: $BACKUP_DIR"
    
    # Create backup directory if it doesn't exist
    mkdir -p "$BACKUP_DIR" || error_exit "Failed to create backup directory: $BACKUP_DIR"
    
    # Check available disk space (require at least 500MB free for ClickHouse backups)
    local available_space
    available_space=$(df "$BACKUP_DIR" | awk 'NR==2 {print $4}')
    if [[ $available_space -lt 512000 ]]; then
        error_exit "Insufficient disk space. Available: ${available_space}KB, Required: 500MB"
    fi
    
    # Test connection
    test_clickhouse_connection
    
    # Get list of existing tables
    local existing_tables
    existing_tables=$(get_existing_tables)
    
    if [[ -z "$existing_tables" ]]; then
        log "WARNING: No target tables found in ClickHouse database"
        # Create empty marker file to indicate backup ran but found no tables
        touch "$BACKUP_DIR/${BACKUP_PREFIX}_empty.marker"
    else
        log "Found tables to backup: $existing_tables"
        
        # Backup each existing table
        local success_count=0
        local total_size=0
        
        while IFS= read -r table; do
            if [[ -n "$table" ]]; then
                backup_table "$table"
                ((success_count++))
                
                # Add to total size
                local table_backup_file="$BACKUP_DIR/${BACKUP_PREFIX}_${table}.csv.gz"
                if [[ -f "$table_backup_file" ]]; then
                    local file_size
                    file_size=$(stat -c%s "$table_backup_file")
                    ((total_size += file_size))
                fi
            fi
        done <<< "$existing_tables"
        
        log "Successfully backed up $success_count tables"
        log "Total backup size: $(numfmt --to=iec $total_size)"
    fi
    
    # Clean up old backups
    log "Cleaning up backups older than $RETENTION_DAYS days..."
    find "$BACKUP_DIR" -name "clickhouse_*.csv.gz" -type f -mtime +$RETENTION_DAYS -exec rm {} \; || log "WARNING: Failed to clean up some old backups"
    find "$BACKUP_DIR" -name "clickhouse_*.marker" -type f -mtime +$RETENTION_DAYS -exec rm {} \; || log "WARNING: Failed to clean up some old marker files"
    
    # Count remaining backups
    local backup_count
    backup_count=$(find "$BACKUP_DIR" -name "clickhouse_*.csv.gz" -o -name "clickhouse_*.marker" | wc -l)
    log "Backup cleanup completed. Total backup files: $backup_count"
    
    # Create backup manifest
    local manifest_file="$BACKUP_DIR/${BACKUP_PREFIX}_manifest.txt"
    {
        echo "ClickHouse Backup Manifest"
        echo "Created: $(date)"
        echo "Database: $CLICKHOUSE_DATABASE"
        echo "Tables backed up:"
        if [[ -n "$existing_tables" ]]; then
            while IFS= read -r table; do
                if [[ -n "$table" ]]; then
                    local table_backup_file="$BACKUP_DIR/${BACKUP_PREFIX}_${table}.csv.gz"
                    if [[ -f "$table_backup_file" ]]; then
                        local file_size row_count_result
                        file_size=$(stat -c%s "$table_backup_file")
                        row_count_result=$(clickhouse_query "SELECT COUNT(*) FROM ${CLICKHOUSE_DATABASE}.${table}" 2>/dev/null || echo "unknown")
                        echo "  - $table: $row_count_result rows, $(numfmt --to=iec $file_size)"
                    fi
                fi
            done <<< "$existing_tables"
        else
            echo "  - No tables found"
        fi
    } > "$manifest_file"
    
    chmod 640 "$manifest_file"
    
    # Output backup information for Kubernetes logs
    cat << EOF
BACKUP_STATUS=SUCCESS
BACKUP_PREFIX=$BACKUP_PREFIX
BACKUP_DIR=$BACKUP_DIR
TABLES_BACKED_UP=$success_count
TOTAL_SIZE=${total_size:-0}
BACKUP_COUNT=$backup_count
EOF
    
    log "ClickHouse backup process completed successfully!"
}

# Run main function
main "$@"