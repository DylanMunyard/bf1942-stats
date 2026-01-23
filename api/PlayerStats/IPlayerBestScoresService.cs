using api.PlayerTracking;

namespace api.PlayerStats;

/// <summary>
/// Service for updating player best scores when sessions complete.
/// </summary>
public interface IPlayerBestScoresService
{
    /// <summary>
    /// Updates best scores for players whose sessions have just completed.
    /// Checks if any completed session qualifies for top 3 in any period.
    /// </summary>
    Task UpdateBestScoresForCompletedSessionsAsync(IEnumerable<PlayerSession> completedSessions, CancellationToken ct = default);
}
