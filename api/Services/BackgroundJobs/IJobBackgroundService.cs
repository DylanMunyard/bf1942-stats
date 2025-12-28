namespace api.Services.BackgroundJobs;

/// <summary>
/// Interface for background job execution logic, allowing jobs to be triggered on-demand.
/// </summary>
public interface IJobBackgroundService
{
    /// <summary>
    /// Executes the job logic.
    /// </summary>
    Task RunAsync(CancellationToken ct = default);
}

/// <summary>
/// Interface for daily aggregate refresh job.
/// </summary>
public interface IDailyAggregateRefreshBackgroundService : IJobBackgroundService
{
    /// <summary>
    /// One-time full backfill of ServerMapStats from all historical Rounds data.
    /// Use for initial population - daily refresh only updates last 2 months.
    /// </summary>
    Task BackfillServerMapStatsAsync(CancellationToken ct = default);
}

/// <summary>
/// Marker interface for weekly cleanup job.
/// </summary>
public interface IWeeklyCleanupBackgroundService : IJobBackgroundService;

/// <summary>
/// Marker interface for aggregate backfill job.
/// Rebuilds aggregate tables from historical PlayerSessions data.
/// </summary>
public interface IAggregateBackfillBackgroundService : IJobBackgroundService
{
    /// <summary>
    /// Run backfill for a specific tier only.
    /// </summary>
    /// <param name="tier">Tier number (1=7 days, 2=30 days, 3=90 days, 4=all)</param>
    /// <param name="ct">Cancellation token</param>
    Task RunTierAsync(int tier, CancellationToken ct = default);
}

/// <summary>
/// Backfills ServerOnlineCounts from ClickHouse to SQLite.
/// Aggregates minute-level data to hourly granularity.
/// </summary>
public interface IServerOnlineCountsBackfillBackgroundService : IJobBackgroundService
{
    /// <summary>
    /// Run backfill for a specific number of days.
    /// </summary>
    /// <param name="days">Number of days to backfill (default 60)</param>
    /// <param name="ct">Cancellation token</param>
    Task RunAsync(int days, CancellationToken ct = default);
}
