namespace api.Services;

/// <summary>
/// In-process locks for aggregate recalculation and background jobs.
/// Serializes work that touches PlayerStatsMonthly/PlayerServerStats/PlayerMapStats/PlayerBestScores,
/// ServerMapStats, and ServerPlayerRankings to avoid conflicts between scheduled jobs and
/// delete/undelete-triggered recalculations.
/// </summary>
public interface IAggregateConcurrencyService
{
    /// <summary>
    /// Runs work while holding the player-aggregates lock
    /// (PlayerStatsMonthly, PlayerServerStats, PlayerMapStats, PlayerBestScores).
    /// </summary>
    Task ExecuteWithPlayerAggregatesLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    /// <summary>
    /// Runs work while holding the player-aggregates lock and returns a value.
    /// </summary>
    Task<T> ExecuteWithPlayerAggregatesLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);

    /// <summary>
    /// Runs work while holding the ServerMapStats lock.
    /// </summary>
    Task ExecuteWithServerMapStatsLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    /// <summary>
    /// Runs work while holding the ServerMapStats lock and returns a value.
    /// </summary>
    Task<T> ExecuteWithServerMapStatsLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);

    /// <summary>
    /// Runs work while holding the ServerPlayerRankings lock.
    /// </summary>
    Task ExecuteWithServerPlayerRankingsLockAsync(Func<CancellationToken, Task> work, CancellationToken ct = default);

    /// <summary>
    /// Runs work while holding the ServerPlayerRankings lock and returns a value.
    /// </summary>
    Task<T> ExecuteWithServerPlayerRankingsLockAsync<T>(Func<CancellationToken, Task<T>> work, CancellationToken ct = default);
}
