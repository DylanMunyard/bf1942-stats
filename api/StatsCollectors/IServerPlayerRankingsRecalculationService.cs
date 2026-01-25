namespace api.StatsCollectors;

/// <summary>
/// Recalculates ServerPlayerRankings for a specific server and (year, month) from PlayerSessions.
/// Excludes soft-deleted sessions. Used after round delete/undelete and by the scheduled ranking job.
/// </summary>
public interface IServerPlayerRankingsRecalculationService
{
    /// <summary>
    /// Deletes existing ServerPlayerRankings for the given server+period, then rebuilds from
    /// PlayerSessions (excluding IsDeleted). Returns the number of rankings inserted.
    /// </summary>
    Task<int> RecalculateForServerAndPeriodAsync(string serverGuid, int year, int month, CancellationToken ct = default);
}
