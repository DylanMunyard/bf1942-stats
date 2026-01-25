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

    /// <summary>
    /// One-time full backfill of MapHourlyPatterns from all historical Rounds data.
    /// Use for initial population - daily refresh only updates last 60 days.
    /// </summary>
    Task BackfillMapHourlyPatternsAsync(CancellationToken ct = default);

    /// <summary>
    /// Refreshes ServerMapStats for a single (ServerGuid, MapName, Year, Month) from Rounds,
    /// excluding soft-deleted rounds. Used after round delete/undelete to recalc the affected cell.
    /// </summary>
    Task RefreshServerMapStatsForServerMapPeriodAsync(string serverGuid, string mapName, int year, int month, CancellationToken ct = default);
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

    /// <summary>
    /// Run backfill for specific player names only.
    /// Used after round deletion to recalculate aggregates for affected players.
    /// </summary>
    /// <param name="playerNames">Collection of player names to recalculate</param>
    /// <param name="ct">Cancellation token</param>
    Task RunForPlayersAsync(IEnumerable<string> playerNames, CancellationToken ct = default);
}
