#!/bin/bash

# Backup Transfer Script for BF1942 Stats
# This script transfers backups from Kubernetes storage to Proxmox host mount
# Designed to run as a separate job after backup creation

set -euo pipefail

# Configuration
BACKUP_SOURCE_DIR="${BACKUP_SOURCE_DIR:-/mnt/backup}"
BACKUP_DEST_DIR="${BACKUP_DEST_DIR:-/mnt/proxmox-backup}"
RETENTION_DAYS="${RETENTION_DAYS:-30}"
SYNC_MODE="${SYNC_MODE:-copy}" # copy or move

# Logging function
log() {
    echo "[$(date '+%Y-%m-%d %H:%M:%S')] $1"
}

# Error handling
error_exit() {
    log "ERROR: $1"
    exit 1
}

# Check if directories exist
check_directories() {
    if [[ ! -d "$BACKUP_SOURCE_DIR" ]]; then
        error_exit "Source backup directory not found: $BACKUP_SOURCE_DIR"
    fi
    
    # Create destination directory if it doesn't exist
    mkdir -p "$BACKUP_DEST_DIR" || error_exit "Failed to create destination directory: $BACKUP_DEST_DIR"
    
    # Test write permissions on destination
    local test_file="$BACKUP_DEST_DIR/.write_test_$$"
    if ! touch "$test_file" 2>/dev/null; then
        error_exit "No write permissions on destination directory: $BACKUP_DEST_DIR"
    fi
    rm -f "$test_file"
    
    log "Directory checks passed"
    log "Source: $BACKUP_SOURCE_DIR"
    log "Destination: $BACKUP_DEST_DIR"
}

# Get disk usage information
check_disk_space() {
    local source_usage dest_usage source_available dest_available
    
    source_usage=$(du -sh "$BACKUP_SOURCE_DIR" | cut -f1)
    source_available=$(df -h "$BACKUP_SOURCE_DIR" | awk 'NR==2 {print $4}')
    
    dest_usage=$(du -sh "$BACKUP_DEST_DIR" | cut -f1)
    dest_available=$(df -h "$BACKUP_DEST_DIR" | awk 'NR==2 {print $4}')
    
    log "Source directory usage: $source_usage (Available: $source_available)"
    log "Destination directory usage: $dest_usage (Available: $dest_available)"
    
    # Check if destination has enough space (rough estimate)
    local dest_available_kb dest_required_kb
    dest_available_kb=$(df "$BACKUP_DEST_DIR" | awk 'NR==2 {print $4}')
    dest_required_kb=$(du -s "$BACKUP_SOURCE_DIR" | cut -f1)
    
    if [[ $dest_available_kb -lt $dest_required_kb ]]; then
        log "WARNING: Destination may not have enough space"
        log "Available: ${dest_available_kb}KB, Required: ${dest_required_kb}KB"
    fi
}

# Transfer files
transfer_backups() {
    local transferred_count=0
    local failed_count=0
    local total_size=0
    
    log "Starting backup transfer process..."
    log "Transfer mode: $SYNC_MODE"
    
    # Find all backup files from today and yesterday (in case job runs around midnight)
    local current_date previous_date
    current_date=$(date +"%Y%m%d")
    previous_date=$(date -d "1 day ago" +"%Y%m%d")
    
    # Transfer SQLite backups
    log "Transferring SQLite backups..."
    for file in "$BACKUP_SOURCE_DIR"/playertracker_${current_date}_*.db.gz "$BACKUP_SOURCE_DIR"/playertracker_${previous_date}_*.db.gz; do
        if [[ -f "$file" ]]; then
            local basename filename dest_file file_size
            basename=$(basename "$file")
            dest_file="$BACKUP_DEST_DIR/$basename"
            
            if [[ ! -f "$dest_file" ]]; then
                log "Transferring: $basename"
                
                if [[ "$SYNC_MODE" == "move" ]]; then
                    if mv "$file" "$dest_file"; then
                        ((transferred_count++))
                        file_size=$(stat -c%s "$dest_file")
                        ((total_size += file_size))
                        log "Moved: $basename ($(numfmt --to=iec $file_size))"
                    else
                        log "ERROR: Failed to move $basename"
                        ((failed_count++))
                    fi
                else
                    if cp "$file" "$dest_file"; then
                        ((transferred_count++))
                        file_size=$(stat -c%s "$dest_file")
                        ((total_size += file_size))
                        log "Copied: $basename ($(numfmt --to=iec $file_size))"
                    else
                        log "ERROR: Failed to copy $basename"
                        ((failed_count++))
                    fi
                fi
                
                # Set appropriate permissions
                chmod 640 "$dest_file" 2>/dev/null || log "WARNING: Could not set permissions for $dest_file"
            else
                log "Skipping $basename (already exists at destination)"
            fi
        fi
    done
    
    # Transfer ClickHouse backups and manifests
    log "Transferring ClickHouse backups..."
    for file in "$BACKUP_SOURCE_DIR"/clickhouse_${current_date}_*.csv.gz "$BACKUP_SOURCE_DIR"/clickhouse_${current_date}_*.txt "$BACKUP_SOURCE_DIR"/clickhouse_${current_date}_*.marker \
                "$BACKUP_SOURCE_DIR"/clickhouse_${previous_date}_*.csv.gz "$BACKUP_SOURCE_DIR"/clickhouse_${previous_date}_*.txt "$BACKUP_SOURCE_DIR"/clickhouse_${previous_date}_*.marker; do
        if [[ -f "$file" ]]; then
            local basename dest_file file_size
            basename=$(basename "$file")
            dest_file="$BACKUP_DEST_DIR/$basename"
            
            if [[ ! -f "$dest_file" ]]; then
                log "Transferring: $basename"
                
                if [[ "$SYNC_MODE" == "move" ]]; then
                    if mv "$file" "$dest_file"; then
                        ((transferred_count++))
                        file_size=$(stat -c%s "$dest_file")
                        ((total_size += file_size))
                        log "Moved: $basename ($(numfmt --to=iec $file_size))"
                    else
                        log "ERROR: Failed to move $basename"
                        ((failed_count++))
                    fi
                else
                    if cp "$file" "$dest_file"; then
                        ((transferred_count++))
                        file_size=$(stat -c%s "$dest_file")
                        ((total_size += file_size))
                        log "Copied: $basename ($(numfmt --to=iec $file_size))"
                    else
                        log "ERROR: Failed to copy $basename"
                        ((failed_count++))
                    fi
                fi
                
                # Set appropriate permissions
                chmod 640 "$dest_file" 2>/dev/null || log "WARNING: Could not set permissions for $dest_file"
            else
                log "Skipping $basename (already exists at destination)"
            fi
        fi
    done
    
    log "Transfer completed: $transferred_count files transferred, $failed_count failed"
    log "Total transferred size: $(numfmt --to=iec $total_size)"
    
    if [[ $failed_count -gt 0 ]]; then
        error_exit "$failed_count file transfers failed"
    fi
}

# Clean up old backups from destination
cleanup_old_backups() {
    log "Cleaning up old backups from destination (older than $RETENTION_DAYS days)..."
    
    local cleaned_count=0
    
    # Clean up SQLite backups
    while IFS= read -r -d '' file; do
        rm "$file" && ((cleaned_count++)) || log "WARNING: Failed to remove $file"
    done < <(find "$BACKUP_DEST_DIR" -name "playertracker_*.db.gz" -type f -mtime +$RETENTION_DAYS -print0 2>/dev/null || true)
    
    # Clean up ClickHouse backups
    while IFS= read -r -d '' file; do
        rm "$file" && ((cleaned_count++)) || log "WARNING: Failed to remove $file"
    done < <(find "$BACKUP_DEST_DIR" -name "clickhouse_*.csv.gz" -o -name "clickhouse_*.txt" -o -name "clickhouse_*.marker" -type f -mtime +$RETENTION_DAYS -print0 2>/dev/null || true)
    
    log "Cleaned up $cleaned_count old backup files"
}

# Create transfer report
create_transfer_report() {
    local report_file="$BACKUP_DEST_DIR/transfer_report_$(date +"%Y%m%d_%H%M%S").txt"
    local total_backups
    total_backups=$(find "$BACKUP_DEST_DIR" -name "playertracker_*.db.gz" -o -name "clickhouse_*.csv.gz" -o -name "clickhouse_*.marker" | wc -l)
    
    {
        echo "Backup Transfer Report"
        echo "Generated: $(date)"
        echo "Source: $BACKUP_SOURCE_DIR"
        echo "Destination: $BACKUP_DEST_DIR"
        echo "Transfer mode: $SYNC_MODE"
        echo "Retention period: $RETENTION_DAYS days"
        echo ""
        echo "Current backups in destination:"
        find "$BACKUP_DEST_DIR" -name "playertracker_*.db.gz" -o -name "clickhouse_*.csv.gz" -o -name "clickhouse_*.marker" -o -name "clickhouse_*.txt" | sort | while read -r file; do
            if [[ -f "$file" ]]; then
                local size mtime basename
                basename=$(basename "$file")
                size=$(stat -c%s "$file")
                mtime=$(stat -c%Y "$file")
                echo "  $basename - $(numfmt --to=iec $size) - $(date -d @$mtime)"
            fi
        done
    } > "$report_file"
    
    chmod 640 "$report_file"
    log "Transfer report created: $report_file"
    log "Total backups in destination: $total_backups"
}

# Main function
main() {
    log "Starting backup transfer process..."
    
    # Validate configuration
    check_directories
    check_disk_space
    
    # Perform transfer
    transfer_backups
    
    # Clean up old files
    cleanup_old_backups
    
    # Create report
    create_transfer_report
    
    # Output status for Kubernetes logs
    cat << EOF
TRANSFER_STATUS=SUCCESS
SOURCE_DIR=$BACKUP_SOURCE_DIR
DEST_DIR=$BACKUP_DEST_DIR
SYNC_MODE=$SYNC_MODE
RETENTION_DAYS=$RETENTION_DAYS
EOF
    
    log "Backup transfer process completed successfully!"
}

# Run main function
main "$@"