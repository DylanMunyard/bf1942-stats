# BF1942 Stats Database Backup System

This directory contains a comprehensive backup solution for the BF1942 Stats application databases (SQLite and ClickHouse) designed to run on Kubernetes with transfer to Proxmox host storage.

## Overview

The backup system consists of three main components:

1. **SQLite Database Backup** - Backs up the main application database
2. **ClickHouse Database Backup** - Backs up analytics and metrics data  
3. **Backup Transfer** - Copies backups to Proxmox host mount for long-term storage

## Architecture

```
┌─────────────────┐    ┌──────────────────┐    ┌─────────────────┐
│   SQLite DB     │───▶│  Kubernetes      │───▶│  Proxmox Host   │
│ (Application)   │    │  Temp Storage    │    │  Mount Storage  │
└─────────────────┘    │                  │    └─────────────────┘
                       │                  │           
┌─────────────────┐    │                  │    
│  ClickHouse DB  │───▶│                  │    
│  (Analytics)    │    └──────────────────┘    
└─────────────────┘                            
```

## Schedule

The backup system runs **twice daily**:
- **2:00 AM & 2:00 PM** - SQLite backup
- **2:05 AM & 2:05 PM** - ClickHouse backup (5 minutes later)
- **2:15 AM & 2:15 PM** - Transfer to Proxmox (10 minutes after backups)

## Files

### Scripts
- `backup-sqlite.sh` - SQLite database backup script
- `backup-clickhouse.sh` - ClickHouse tables backup script  
- `backup-transfer.sh` - Transfer backups to Proxmox mount

### Kubernetes Resources
- `backup-cronjobs.yaml` - Complete Kubernetes deployment including:
  - PersistentVolumeClaims for storage
  - ConfigMap with backup scripts
  - CronJobs for scheduling

## Prerequisites

### Kubernetes Setup
1. **Namespace**: `bf42-stats` (should already exist)
2. **Node**: `bethany` (as specified in your deployment)
3. **Storage Classes**: 
   - `local-path` for temporary backup storage
   - Configure appropriate storage class for Proxmox mount

### Proxmox Storage Mount
You need to configure the `proxmox-backup-pvc` PVC in `backup-cronjobs.yaml` to use a storage class that mounts your Proxmox backup location. This could be:
- **NFS**: If Proxmox exports the backup directory via NFS
- **hostPath**: If the backup directory is directly mounted on the Kubernetes node
- **Custom CSI**: If using a specific storage driver

## Installation

1. **Configure Proxmox Storage**:
   Edit `backup-cronjobs.yaml` and update the `proxmox-backup-pvc` section:
   ```yaml
   spec:
     storageClassName: your-proxmox-storage-class  # Update this
     resources:
       requests:
         storage: 50Gi  # Adjust size as needed
   ```

2. **Deploy the backup system**:
   ```bash
   kubectl apply -f backup-cronjobs.yaml
   ```

3. **Verify deployment**:
   ```bash
   # Check PVCs
   kubectl get pvc -n bf42-stats
   
   # Check ConfigMap
   kubectl get configmap backup-scripts -n bf42-stats
   
   # Check CronJobs
   kubectl get cronjob -n bf42-stats
   ```

## Configuration

### Environment Variables

#### SQLite Backup (`backup-sqlite-job`)
- `DB_PATH`: Database location (default: `/mnt/data/playertracker.db`)
- `BACKUP_DIR`: Backup directory (default: `/mnt/backup`)
- `RETENTION_DAYS`: Days to keep backups (default: `30`)

#### ClickHouse Backup (`backup-clickhouse-job`)
- `CLICKHOUSE_URL`: ClickHouse server URL (default: `http://clickhouse-service.clickhouse:8123`)
- `CLICKHOUSE_USER`: Username (default: `default`)
- `CLICKHOUSE_DATABASE`: Database name (default: `default`)
- `BACKUP_DIR`: Backup directory (default: `/mnt/backup`)
- `RETENTION_DAYS`: Days to keep backups (default: `30`)

#### Backup Transfer (`backup-transfer-job`)
- `BACKUP_SOURCE_DIR`: Source directory (default: `/mnt/backup`)
- `BACKUP_DEST_DIR`: Destination directory (default: `/mnt/proxmox-backup`)
- `RETENTION_DAYS`: Days to keep backups (default: `30`)
- `SYNC_MODE`: Transfer mode - `copy` or `move` (default: `copy`)

## Backup Contents

### SQLite Backup
- **File Pattern**: `playertracker_YYYYMMDD_HHMMSS.db.gz`
- **Content**: Complete SQLite database with all application data
- **Compression**: gzip compressed for space efficiency
- **Method**: Uses SQLite `.backup` command for consistency

### ClickHouse Backup
- **File Pattern**: `clickhouse_YYYYMMDD_HHMMSS_[table].csv.gz`
- **Content**: Individual table exports in CSV format
- **Tables Backed Up**:
  - `player_metrics`
  - `player_rounds`  
  - `player_achievements`
  - `gamification_achievements`
  - `team_killer_metrics`
  - `server_statistics`
- **Manifest**: `clickhouse_YYYYMMDD_HHMMSS_manifest.txt` with backup details

## Monitoring

### Checking Backup Status

```bash
# Check recent backup jobs
kubectl get jobs -n bf42-stats --sort-by=.metadata.creationTimestamp

# View backup logs
kubectl logs -n bf42-stats job/backup-sqlite-job-[suffix]
kubectl logs -n bf42-stats job/backup-clickhouse-job-[suffix]
kubectl logs -n bf42-stats job/backup-transfer-job-[suffix]

# Check backup files
kubectl exec -n bf42-stats deployment/bf42-stats -- ls -la /mnt/backup/
```

### Backup Metrics
Each backup job outputs status information in the logs:
- `BACKUP_STATUS=SUCCESS/FAILURE`
- File sizes and compression ratios
- Number of files backed up
- Transfer statistics

## Troubleshooting

### Common Issues

1. **PVC Mount Issues**:
   ```bash
   kubectl describe pvc backup-storage-pvc proxmox-backup-pvc -n bf42-stats
   ```

2. **Permission Problems**:
   ```bash
   kubectl exec -n bf42-stats deployment/bf42-stats -- ls -la /mnt/
   ```

3. **ClickHouse Connectivity**:
   ```bash
   kubectl exec -n bf42-stats deployment/bf42-stats -- curl -s http://clickhouse-service.clickhouse:8123/
   ```

4. **Storage Space Issues**:
   ```bash
   kubectl exec -n bf42-stats deployment/bf42-stats -- df -h /mnt/backup /mnt/proxmox-backup
   ```

### Manual Backup Execution

To run backups manually:

```bash
# Create a one-time SQLite backup job
kubectl create job --from=cronjob/backup-sqlite-job backup-sqlite-manual -n bf42-stats

# Create a one-time ClickHouse backup job  
kubectl create job --from=cronjob/backup-clickhouse-job backup-clickhouse-manual -n bf42-stats

# Create a one-time transfer job
kubectl create job --from=cronjob/backup-transfer-job backup-transfer-manual -n bf42-stats
```

## Retention Policy

- **Default**: 30 days retention
- **Cleanup**: Automatic cleanup of old backups during each run
- **Location**: Both temporary Kubernetes storage and Proxmox destination
- **Transfer Reports**: Generated with each transfer for audit purposes

## Security

- **File Permissions**: Backup files created with 640 permissions (owner read/write, group read)
- **Network**: ClickHouse backups use existing service networking
- **Authentication**: Uses existing ClickHouse authentication (default user)
- **Isolation**: Backup jobs run in isolated containers

## Recovery

### SQLite Database Recovery
```bash
# Copy backup from Proxmox mount
cp /mnt/proxmox-backup/playertracker_YYYYMMDD_HHMMSS.db.gz /tmp/

# Decompress
gunzip /tmp/playertracker_YYYYMMDD_HHMMSS.db.gz

# Stop application, replace database, restart
# (Specific steps depend on your deployment process)
```

### ClickHouse Recovery
```bash
# For each table backup file:
zcat clickhouse_YYYYMMDD_HHMMSS_[table].csv.gz | \
  curl -X POST 'http://clickhouse-service.clickhouse:8123/' \
  --data-binary @- \
  -H "X-ClickHouse-User: default" \
  --data-urlencode "query=INSERT INTO default.[table] FORMAT CSV"
```

## Maintenance

### Updating Backup Scripts
1. Modify the scripts in this directory
2. Update the ConfigMap: `kubectl apply -f backup-cronjobs.yaml`
3. Restart running jobs if needed

### Changing Schedule
1. Edit the `schedule` fields in `backup-cronjobs.yaml`
2. Apply changes: `kubectl apply -f backup-cronjobs.yaml`

### Adjusting Retention
1. Update `RETENTION_DAYS` environment variables
2. Apply changes: `kubectl apply -f backup-cronjobs.yaml`

## Support

For issues with the backup system:
1. Check Kubernetes job logs
2. Verify PVC mounts and permissions  
3. Test database connectivity
4. Review backup file integrity
5. Monitor available disk space