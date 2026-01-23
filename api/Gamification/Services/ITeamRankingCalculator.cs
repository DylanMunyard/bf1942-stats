using api.PlayerTracking;

namespace api.Gamification.Services;

/// <summary>
/// Service for calculating team rankings based on tournament match results.
/// Implements hierarchical ranking algorithm:
/// 1. Primary: Rounds Won
/// 2. Tier 1: Rounds Tied vs Rounds Lost (tied > lost)
/// 3. Tier 2: Ticket Differential
/// 4. Tier 3: Administrative Penalties (future)
/// </summary>
public interface ITeamRankingCalculator
{
    /// <summary>
    /// Calculate rankings for a tournament, optionally filtered by week.
    /// </summary>
    /// <param name="tournamentId">The tournament ID</param>
    /// <param name="week">Optional week filter. If null, includes all weeks in cumulative calculation.</param>
    /// <param name="gameMode">Optional game mode (e.g., "CTF"). If null, uses default Conquest scoring.</param>
    /// <returns>List of team rankings sorted by calculated rank position</returns>
    Task<List<TournamentTeamRanking>> CalculateRankingsAsync(int tournamentId, string? week = null, string? gameMode = null);

    /// <summary>
    /// Recalculate all rankings for a tournament (cumulative and by week).
    /// This is the master recalculation method called after match results change.
    /// </summary>
    /// <param name="tournamentId">The tournament ID</param>
    /// <param name="gameMode">Optional game mode (e.g., "CTF"). If null, uses default Conquest scoring.</param>
    /// <returns>Count of ranking records updated</returns>
    Task<int> RecalculateAllRankingsAsync(int tournamentId, string? gameMode = null);
}
