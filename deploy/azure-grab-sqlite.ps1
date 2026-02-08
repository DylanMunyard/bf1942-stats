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

# Storage account config
$STORAGE_ACCOUNT = "bfstatsio"
$STORAGE_RG = "bfstats-io"
$CONTAINER_NAME = "sqlite"

# Assign managed identity to VM
Write-Host "Assigning managed identity to VM..." -ForegroundColor Green
az vm identity assign --resource-group $TEMP_RG --name $TEMP_VM_NAME

# Ensure blob container exists (no-op if it already exists)
Write-Host "Ensuring blob container '$CONTAINER_NAME' exists..." -ForegroundColor Green
az storage container create `
  --name $CONTAINER_NAME `
  --account-name $STORAGE_ACCOUNT `
  --resource-group $STORAGE_RG `
  --auth-mode login

# Grant Storage Blob Data Contributor role to VM's managed identity
Write-Host "Granting Storage Blob Data Contributor role to VM identity..." -ForegroundColor Green
$vmPrincipalId = az vm show --resource-group $TEMP_RG --name $TEMP_VM_NAME --query identity.principalId -o tsv
$storageAccountId = az storage account show --name $STORAGE_ACCOUNT --resource-group $STORAGE_RG --query id -o tsv

az role assignment create `
  --assignee-object-id $vmPrincipalId `
  --assignee-principal-type ServicePrincipal `
  --role "Storage Blob Data Contributor" `
  --scope $storageAccountId

Write-Host "Waiting 60s for role assignment propagation..." -ForegroundColor Yellow
Start-Sleep -Seconds 60

# Write VM script to temp file to avoid PowerShell here-string escaping issues
$vmScript = @'
#!/bin/bash
set -e
echo "Starting..."
sudo mkdir -p /mnt/recovery
if sudo mount /dev/sdc /mnt/recovery 2>/dev/null; then
  echo "Mounted /dev/sdc"
elif sudo mount /dev/sdc1 /mnt/recovery 2>/dev/null; then
  echo "Mounted /dev/sdc1"
else
  echo "ERROR: Could not mount /dev/sdc or /dev/sdc1" >&2
  exit 1
fi
ls -lh /mnt/recovery/playertracker.db*
sudo apt-get update -qq && sudo apt-get install -y -qq sqlite3
sudo sqlite3 /mnt/recovery/playertracker.db "PRAGMA wal_checkpoint(TRUNCATE);"
echo "WAL checkpoint complete"
ls -lh /mnt/recovery/playertracker.db*
curl -sL https://aka.ms/InstallAzureCLIDeb | sudo bash
az login --identity
for f in /mnt/recovery/playertracker.db /mnt/recovery/playertracker.db-wal /mnt/recovery/playertracker.db-shm; do
  if [ -f "$f" ]; then
    echo "Uploading $(basename "$f")..."
    az storage blob upload \
      --account-name __STORAGE_ACCOUNT__ \
      --container-name __CONTAINER_NAME__ \
      --name "$(basename "$f")" \
      --file "$f" \
      --overwrite \
      --auth-mode login
  fi
done
echo "Upload complete"
az storage blob list --account-name __STORAGE_ACCOUNT__ --container-name __CONTAINER_NAME__ --auth-mode login -o table
'@ -replace '__STORAGE_ACCOUNT__', $STORAGE_ACCOUNT -replace '__CONTAINER_NAME__', $CONTAINER_NAME

$tempScript = Join-Path $env:TEMP "vm-upload.sh"
[System.IO.File]::WriteAllText($tempScript, $vmScript)

Write-Host "Mounting disk, checkpointing WAL, and uploading to blob storage..." -ForegroundColor Green
az vm run-command invoke `
  --resource-group $TEMP_RG `
  --name $TEMP_VM_NAME `
  --command-id RunShellScript `
  --scripts "@$tempScript"

Remove-Item $tempScript

# Generate SAS URLs (24hr expiry) for each uploaded blob
Write-Host "Generating SAS URLs..." -ForegroundColor Green
$expiry = (Get-Date).AddHours(24).ToUniversalTime().ToString("yyyy-MM-ddTHH:mmZ")
$accountKey = az storage account keys list --account-name $STORAGE_ACCOUNT --resource-group $STORAGE_RG --query "[0].value" -o tsv
$blobs = az storage blob list --account-name $STORAGE_ACCOUNT --container-name $CONTAINER_NAME --account-key $accountKey --query "[].name" -o tsv

Write-Host ""
Write-Host "========================================" -ForegroundColor Green
Write-Host "SAS URLs (valid for 24 hours):" -ForegroundColor Green
foreach ($blob in $blobs) {
    $sasUrl = az storage blob generate-sas `
      --account-name $STORAGE_ACCOUNT `
      --container-name $CONTAINER_NAME `
      --name $blob `
      --permissions r `
      --expiry $expiry `
      --account-key $accountKey `
      --full-uri `
      -o tsv
    Write-Host ""
    Write-Host "${blob}:" -ForegroundColor Yellow
    Write-Host $sasUrl -ForegroundColor Cyan
}
Write-Host "========================================" -ForegroundColor Green
Write-Host ""
Write-Host "Set these env vars in the Railway sqlite-seed service:" -ForegroundColor Yellow
Write-Host "  BLOB_SAS_URL     = <playertracker.db URL>" -ForegroundColor Yellow
if ($blobs -contains "playertracker.db-wal") {
    Write-Host "  BLOB_SAS_URL_WAL = <playertracker.db-wal URL>" -ForegroundColor Yellow
}
if ($blobs -contains "playertracker.db-shm") {
    Write-Host "  BLOB_SAS_URL_SHM = <playertracker.db-shm URL>" -ForegroundColor Yellow
}
Write-Host ""
Write-Host "Cleanup when done:" -ForegroundColor Yellow
Write-Host "  az group delete -n $TEMP_RG --yes --no-wait" -ForegroundColor Cyan
