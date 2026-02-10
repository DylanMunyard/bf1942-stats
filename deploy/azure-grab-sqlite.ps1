param(
    [Parameter(Mandatory=$true)]
    [string]$HetznerKeyPath
)

# Configuration
$RESOURCE_GROUP = "MC_bfstats-io_bfstats-aks_australiaeast"
$SNAPSHOT_NAME = "pre-migrate-back-to-home"
$TEMP_RG = "temp-recovery-rg"
$TEMP_VM_NAME = "temp-recovery-vm"
$TEMP_DISK_NAME = "temp-recovery-disk"
$LOCATION = "australiaeast"

# Check/Create temporary resource group
$rgExists = az group exists --name $TEMP_RG
if ($rgExists -eq "false") {
    Write-Host "Creating temporary resource group..." -ForegroundColor Green
    az group create --name $TEMP_RG --location $LOCATION
} else {
    Write-Host "Using existing resource group: $TEMP_RG" -ForegroundColor Yellow
}

# Check/Create managed disk from snapshot
$diskExists = az disk show --resource-group $TEMP_RG --name $TEMP_DISK_NAME 2>$null
if (!$diskExists) {
    Write-Host "Creating disk from snapshot..." -ForegroundColor Green
    az disk create `
      --resource-group $TEMP_RG `
      --name $TEMP_DISK_NAME `
      --source "/subscriptions/6acd4b16-d85b-47d3-acd7-738e4c22bdf1/resourceGroups/$RESOURCE_GROUP/providers/Microsoft.Compute/snapshots/$SNAPSHOT_NAME" `
      --location $LOCATION
} else {
    Write-Host "Using existing disk: $TEMP_DISK_NAME" -ForegroundColor Yellow
}

# Check/Create temporary VM
$vmExists = az vm show --resource-group $TEMP_RG --name $TEMP_VM_NAME 2>$null
if (!$vmExists) {
    Write-Host "Creating temporary VM..." -ForegroundColor Green
    az vm create `
      --resource-group $TEMP_RG `
      --name $TEMP_VM_NAME `
      --image Ubuntu2204 `
      --size Standard_D2s_v3 `
      --os-disk-size-gb 128 `
      --storage-sku Premium_LRS `
      --location $LOCATION `
      --admin-username azureuser `
      --generate-ssh-keys
} else {
    Write-Host "Using existing VM: $TEMP_VM_NAME" -ForegroundColor Yellow
}

# Check if disk is attached
$attachedDisks = az vm show --resource-group $TEMP_RG --name $TEMP_VM_NAME --query "storageProfile.dataDisks[?name=='$TEMP_DISK_NAME'].name" -o tsv
if (!$attachedDisks) {
    Write-Host "Attaching disk to VM..." -ForegroundColor Green
    az vm disk attach `
      --resource-group $TEMP_RG `
      --vm-name $TEMP_VM_NAME `
      --name $TEMP_DISK_NAME
} else {
    Write-Host "Disk already attached to VM" -ForegroundColor Yellow
}

# Hetzner config
$HETZNER_IP = "HETZNER_VM_IP"
$HETZNER_USER = "HETZNER_VM_SSH_USER"
$HETZNER_KEY = Resolve-Path $HetznerKeyPath
$HETZNER_DEST = "/root"

# Get Azure VM's public IP
Write-Host "Getting Azure VM public IP..." -ForegroundColor Green
$vmIp = az vm show -g $TEMP_RG -n $TEMP_VM_NAME -d --query publicIps -o tsv
Write-Host "Azure VM IP: $vmIp" -ForegroundColor Cyan

# Mount disk, checkpoint WAL, copy and compress (streamed via SSH for live output)
$gzReady = ssh -o StrictHostKeyChecking=accept-new "azureuser@${vmIp}" "test -f /home/azureuser/playertracker.db.gz && echo 'yes' || echo 'no'"
if ($gzReady.Trim() -eq "yes") {
    Write-Host "Compressed database already on VM, skipping mount/checkpoint/gzip" -ForegroundColor Yellow
    ssh "azureuser@${vmIp}" "ls -lh /home/azureuser/playertracker.db.gz"
} else {
    Write-Host "Mounting disk, checkpointing WAL, compressing..." -ForegroundColor Green
    $mountScript = @"
set -e
echo 'Mounting disk...'
sudo mkdir -p /mnt/recovery
if mountpoint -q /mnt/recovery; then
  echo 'Already mounted'
elif sudo mount /dev/sdc /mnt/recovery 2>/dev/null; then
  echo 'Mounted /dev/sdc'
elif sudo mount /dev/sdc1 /mnt/recovery 2>/dev/null; then
  echo 'Mounted /dev/sdc1'
else
  echo 'ERROR: Could not mount /dev/sdc or /dev/sdc1' >&2
  exit 1
fi
ls -lh /mnt/recovery/playertracker.db*
echo 'Installing sqlite3 and pigz...'
sudo apt-get update -qq && sudo apt-get install -y -qq sqlite3 pigz
echo 'Running WAL checkpoint...'
sudo sqlite3 /mnt/recovery/playertracker.db 'PRAGMA wal_checkpoint(TRUNCATE);'
echo 'WAL checkpoint complete'
ls -lh /mnt/recovery/playertracker.db*
echo 'Compressing database with pigz (fast mode)...'
sudo pigz -1 -c /mnt/recovery/playertracker.db > /home/azureuser/playertracker.db.gz
ls -lh /mnt/recovery/playertracker.db /home/azureuser/playertracker.db.gz
echo 'Compressed and ready for transfer'
"@
    $mountScript -replace "`r", "" | ssh "azureuser@${vmIp}" "bash -s"
}

# Get Hetzner's SSH public key (generate ed25519 key if none exists)
Write-Host "Fetching Hetzner SSH public key..." -ForegroundColor Green
$hetznerPubKey = ssh -i $HETZNER_KEY -o StrictHostKeyChecking=accept-new "${HETZNER_USER}@${HETZNER_IP}" "cat ~/.ssh/id_ed25519.pub 2>/dev/null || cat ~/.ssh/id_rsa.pub 2>/dev/null || (ssh-keygen -t ed25519 -f ~/.ssh/id_ed25519 -N '' >/dev/null 2>&1 && cat ~/.ssh/id_ed25519.pub)"

if (!$hetznerPubKey) {
    Write-Host "ERROR: Could not get SSH public key from Hetzner" -ForegroundColor Red
    exit 1
}
Write-Host "Got Hetzner public key" -ForegroundColor Green

# Add Hetzner's public key to Azure VM's authorized_keys
Write-Host "Adding Hetzner key to Azure VM authorized_keys..." -ForegroundColor Green
ssh "azureuser@${vmIp}" "echo '$hetznerPubKey' >> ~/.ssh/authorized_keys"

# Pull compressed database from Azure VM to Hetzner via SCP
Write-Host "Pulling compressed database from Azure VM ($vmIp) to Hetzner ($HETZNER_IP)..." -ForegroundColor Green
ssh -i $HETZNER_KEY "${HETZNER_USER}@${HETZNER_IP}" "scp -o StrictHostKeyChecking=accept-new azureuser@${vmIp}:/home/azureuser/playertracker.db.gz ${HETZNER_DEST}/playertracker.db.gz"

# Decompress on Hetzner and set ownership for container (runs as 1000:1000)
Write-Host "Decompressing and setting permissions on Hetzner..." -ForegroundColor Green
ssh -i $HETZNER_KEY "${HETZNER_USER}@${HETZNER_IP}" "gunzip -f ${HETZNER_DEST}/playertracker.db.gz && chown 1000:1000 ${HETZNER_DEST}/playertracker.db && ls -lh ${HETZNER_DEST}/playertracker.db"

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "Transfer complete!" -ForegroundColor Green
Write-Host "Database: ${HETZNER_USER}@${HETZNER_IP}:${HETZNER_DEST}/playertracker.db" -ForegroundColor Cyan
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Verify on Hetzner:" -ForegroundColor Yellow
Write-Host "  ssh -i $HETZNER_KEY ${HETZNER_USER}@${HETZNER_IP} 'ls -lh ${HETZNER_DEST}/playertracker.db'" -ForegroundColor Cyan
Write-Host ""
Write-Host "Cleanup when done:" -ForegroundColor Yellow
Write-Host "  az group delete -n $TEMP_RG --yes --no-wait" -ForegroundColor Cyan
