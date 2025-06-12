#!/bin/bash

###############################################################################
# Batch SQLite to ClickHouse Migration Script
#
# This script migrates player metrics data from SQLite
# (playertracker.db) to ClickHouse table (player_metrics) using the
# JSONEachRow format.
#
# - Processes records in batches to avoid memory or timeout issues.
# - Allows resuming from a specific offset for interrupted migrations.
# - Progress and batch status are printed to the console.
# - Exits immediately if any batch fails.
#
# Requirements:
#   - sqlite3 with JSON output support
#   - jq (for JSON processing)
#   - curl
#   - ClickHouse server running and accessible at localhost:8123 (k3s kubectl port-forward pod/clickhouse-8544c87db4-jsbn9 8123:8123 -n clickhouse)
#
# Variables to adjust:
#   BATCH_SIZE   - Number of records per batch
#   OFFSET       - Starting record (for resuming)
#   TOTAL        - Total number of records to migrate
#
# Usage:
#   Edit BATCH_SIZE, OFFSET, and TOTAL as needed, then run:
#     ./this_script.sh
###############################################################################

BATCH_SIZE=150000
OFFSET=3505917
TOTAL=4500000

echo "Migrating $TOTAL records using JSONEachRow format..."

while [ $OFFSET -lt $TOTAL ]; do
    BATCH_NUM=$((OFFSET / BATCH_SIZE + 1))
    echo "Processing batch $BATCH_NUM - records $OFFSET to $((OFFSET + BATCH_SIZE))"
    
    sqlite3 playertracker.db -json \
      "SELECT 
         datetime(po.Timestamp) as timestamp,
         ps.ServerGuid as server_guid,
         COALESCE(gs.Name, '') as server_name,
         ps.PlayerName as player_name,
         po.Score as score,
         po.Kills as kills,
         po.Deaths as deaths,
         po.Ping as ping,
         po.TeamLabel as team_name,
         COALESCE(ps.MapName, '') as map_name,
         COALESCE(ps.GameType, '') as game_type
       FROM PlayerObservations po
       JOIN PlayerSessions ps ON po.SessionId = ps.SessionId
       JOIN Servers gs ON ps.ServerGuid = gs.Guid
       ORDER BY po.ObservationId
       LIMIT $BATCH_SIZE OFFSET $OFFSET" | \
    jq -c '.[]' | \
    curl -X POST "http://localhost:8123/?query=INSERT%20INTO%20player_metrics%20FORMAT%20JSONEachRow" \
      -H 'X-ClickHouse-User: default' \
      -H 'Content-Type: application/json' \
      --data-binary @- \
      -w "HTTP Status: %{http_code}, Time: %{time_total}s\n"

    if [ $? -ne 0 ]; then
        echo "Error: Batch $BATCH_NUM failed!"
        exit 1
    fi
    
    OFFSET=$((OFFSET + BATCH_SIZE))
    PERCENT=$((OFFSET * 100 / TOTAL))
    echo "Progress: $PERCENT% ($OFFSET / $TOTAL)"
    sleep 1
done
