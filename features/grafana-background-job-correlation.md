# Background Job Correlation Dashboard

## Problem Statement

When you see alerts like:
- **High Memory Usage**: Memory > 2GB
- **High CPU Usage**: CPU > 0.8 cores
- **High log volume** from Loki

You need to answer: **"Which background job was running and what was it doing?"**

## Solution

Custom Prometheus metrics that correlate background job execution with system resources.

## Metrics Added

### 1. Job Execution Tracking

```promql
# Which jobs are currently active? (use rate() to see activity)
rate(bg_job_executions_total[1m])

# Job execution duration (helps identify slow jobs)
bg_job_duration_seconds
```

**Labels**: `job={stats_collection|clickhouse_sync|ranking_calc|gamification}`

### 2. Volume Metrics (What was the job processing?)

```promql
# Active player count (correlate with memory spikes)
bg_active_players

# Servers processed (high count = more API calls, more memory)
rate(bg_servers_processed_total[1m])

# ClickHouse records synced (high volume = potential memory spike)
rate(bg_clickhouse_records_synced_total[1m])
```

**ClickHouse operation labels**: `operation={player_metrics|server_counts|rounds}`

### 3. Runtime Metrics (Already Available)

```promql
# Memory working set
container_memory_working_set_bytes{container="bf42-stats"}

# GC collections
dotnet_gc_collections_count

# GC pause duration
dotnet_gc_pause_duration_seconds
```

## Answering "What Caused the Spike?"

### Dashboard Panel Setup

Create a Grafana dashboard with these panels stacked vertically:

#### Panel 1: Memory Usage
```promql
container_memory_working_set_bytes{namespace="default",pod=~"bf42-stats.*"}
```

#### Panel 2: Active Background Jobs
```promql
rate(bg_job_executions_total[1m])
```
This shows **which job was running** at the time of the spike. Use bars or lines with different colors per job label.

#### Panel 3: Job Volume
```promql
# Player count
bg_active_players

# ClickHouse records being synced
rate(bg_clickhouse_records_synced_total{operation="player_metrics"}[1m])

# Servers being processed
rate(bg_servers_processed_total[1m])
```

This shows **what the job was processing**. High player counts or record volumes correlate with memory usage.

#### Panel 4: GC Activity
```promql
rate(dotnet_gc_collections_count[1m])
```

This shows if GC is struggling to keep up (indicates memory pressure).

### Example Analysis Workflow

1. **See memory spike at 14:23**
2. **Check Panel 2**: `rate(bg_job_executions_total{job="clickhouse_sync"}[1m])` shows activity
3. **Check Panel 3**: `rate(bg_clickhouse_records_synced_total{operation="player_metrics"}[1m])` shows 50K records/min
4. **Conclusion**: "ClickHouse sync was processing 50K player metrics when memory spiked"

5. **Check Panel 3 again**: `bg_active_players` shows 200 players online
6. **Conclusion**: "High player count (200) caused large metrics batch, which spiked memory during ClickHouse sync"

## PromQL Queries for Alerts

### Alert: Correlate Memory Spike with Job Activity

```yaml
# Alert when memory is high AND a specific job is running
- alert: HighMemoryDuringClickHouseSync
  expr: |
    container_memory_working_set_bytes{pod=~"bf42-stats.*"} > 2e9
    and
    rate(bg_job_executions_total{job="clickhouse_sync"}[1m]) > 0
  annotations:
    description: "Memory is {{humanize $value}} while ClickHouse sync is active"
```

### Alert: High Volume Processing

```yaml
- alert: HighVolumeClickHouseSync
  expr: rate(bg_clickhouse_records_synced_total[5m]) > 10000
  annotations:
    description: "ClickHouse syncing {{humanize $value}} records/sec - may cause memory spike"
```

## Query Examples

### "Which job runs most frequently?"
```promql
topk(4, sum by (job) (rate(bg_job_executions_total[5m])))
```

### "Which job takes longest?"
```promql
histogram_quantile(0.95,
  sum by (job, le) (rate(bg_job_duration_seconds_bucket[5m]))
)
```

### "What's the ClickHouse sync volume over time?"
```promql
sum by (operation) (rate(bg_clickhouse_records_synced_total[1m]))
```

### "Memory usage vs active players"
```promql
# Panel 1: Memory
container_memory_working_set_bytes{pod=~"bf42-stats.*"}

# Panel 2 (overlay): Active players (scaled)
bg_active_players * 10000000  # Scale to match memory axis
```

## Implementation Notes

### Instrumented Services

1. **StatsCollectionBackgroundService** (every 30s)
   - Emits: `bg_job_executions_total{job="stats_collection"}`
   - Emits: `bg_job_duration_seconds{job="stats_collection"}`
   - Emits: `bg_servers_processed_total`
   - Emits: `bg_active_players` (gauge)

2. **ClickHouseSyncBackgroundService** (every 1m)
   - Emits: `bg_job_executions_total{job="clickhouse_sync"}`
   - Emits: `bg_job_duration_seconds{job="clickhouse_sync"}`
   - Emits: `bg_clickhouse_records_synced_total{operation="player_metrics|server_counts|rounds"}`
   - Emits: `bg_clickhouse_sync_duration_seconds{operation=...}`

3. **RankingCalculationService** (every 1h)
   - Emits: `bg_job_executions_total{job="ranking_calc"}`
   - Emits: `bg_job_duration_seconds{job="ranking_calc"}`
   - Emits: `bg_rankings_inserted_total`

4. **GamificationBackgroundService** (every 5m, if enabled)
   - Emits: `bg_job_executions_total{job="gamification"}`
   - Emits: `bg_job_duration_seconds{job="gamification"}`

### Metrics Endpoint

All metrics are exported at `http://bf42-stats:8080/metrics` and scraped by Prometheus every 30s (configured in ServiceMonitor).

### No Manual Memory Tracking

We **don't** manually track GC or memory allocations - that's already done by `metrics.AddRuntimeInstrumentation()` in Program.cs, which provides:
- `dotnet_gc_*` metrics
- `process_*` metrics
- `dotnet_threadpool_*` metrics

We only add **job context** so you can correlate these existing metrics with "which job was running".

## Next Steps

1. Import ASP.NET Core dashboard (ID 19924) for baseline runtime metrics
2. Create custom "Background Job Correlation" dashboard with panels above
3. Set up alerts that combine memory/CPU with job execution
4. Verify metrics are being scraped: `curl http://bf42-stats:8080/metrics | grep bg_`
