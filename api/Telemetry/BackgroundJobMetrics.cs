using System.Diagnostics.Metrics;

namespace api.Telemetry;

/// <summary>
/// Centralized metrics for background job monitoring and correlation analysis.
/// Enables answering "Which job was running when memory/CPU spiked?"
/// </summary>
public static class BackgroundJobMetrics
{
    private static readonly Meter Meter = new("junie-des-1942stats.BackgroundJobs", "1.0.0");

    // === Job Execution Tracking ===
    // Use rate() in PromQL to see which jobs are active at any time

    /// <summary>
    /// Counter incremented each time a job completes. Use rate() to see job frequency.
    /// Labels: job={stats_collection|clickhouse_sync|ranking_calc|gamification}
    /// </summary>
    public static readonly Counter<long> JobExecutions = Meter.CreateCounter<long>(
        "bg_job_executions_total",
        description: "Total job executions (use with 'job' label to filter)");

    /// <summary>
    /// Histogram of job execution durations. Correlate with memory spikes.
    /// Labels: job={stats_collection|clickhouse_sync|ranking_calc|gamification}
    /// </summary>
    public static readonly Histogram<double> JobDuration = Meter.CreateHistogram<double>(
        "bg_job_duration_seconds",
        unit: "s",
        description: "Job execution duration in seconds");

    // === Volume Metrics (What was the job doing?) ===

    /// <summary>
    /// Gauge for current active player count. Correlate with memory/CPU spikes.
    /// </summary>
    public static readonly ObservableGauge<int> ActivePlayers = Meter.CreateObservableGauge<int>(
        "bg_active_players",
        observeValue: () => _currentActivePlayers,
        description: "Current number of active players across all servers");

    private static int _currentActivePlayers = 0;
    public static void SetActivePlayers(int count) => _currentActivePlayers = count;

    /// <summary>
    /// Counter for game servers processed. High volume = potential memory spike.
    /// Labels: game={bf1942|fh2|bfvietnam|all}
    /// </summary>
    public static readonly Counter<long> ServersProcessed = Meter.CreateCounter<long>(
        "bg_servers_processed_total",
        description: "Total game servers processed by stats collection");

    /// <summary>
    /// Counter for ClickHouse records synced. High volume = potential spike.
    /// Labels: operation={player_metrics|server_counts|rounds}
    /// </summary>
    public static readonly Counter<long> ClickHouseRecordsSynced = Meter.CreateCounter<long>(
        "bg_clickhouse_records_synced_total",
        description: "Total records synced to ClickHouse");

    /// <summary>
    /// Duration of ClickHouse sync operations. Slow syncs may indicate issues.
    /// Labels: operation={player_metrics|server_counts|rounds}
    /// </summary>
    public static readonly Histogram<double> ClickHouseSyncDuration = Meter.CreateHistogram<double>(
        "bg_clickhouse_sync_duration_seconds",
        unit: "s",
        description: "Duration of ClickHouse sync operations");

    /// <summary>
    /// Counter for player rankings inserted. High volume = potential spike.
    /// </summary>
    public static readonly Counter<long> RankingsInserted = Meter.CreateCounter<long>(
        "bg_rankings_inserted_total",
        description: "Total player rankings inserted");
}
