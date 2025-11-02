using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Services.Tournament;

/// <summary>
/// Service for managing tournament match results.
/// Handles creation, retrieval, updating, and deletion of match result records.
/// </summary>
public interface ITournamentMatchResultService
{
    /// <summary>
    /// Create or update a tournament match result when a round is linked to a match map.
    /// Attempts auto-detection of team mapping and stores the result.
    /// </summary>
    Task<(int ResultId, string? WarningMessage)> CreateOrUpdateMatchResultAsync(
        int tournamentId,
        int matchId,
        int mapId,
        string roundId);

    /// <summary>
    /// Retrieve a match result by ID.
    /// </summary>
    Task<TournamentMatchResult?> GetMatchResultAsync(int resultId);

    /// <summary>
    /// Override the team mapping for a match result (admin operation).
    /// </summary>
    Task OverrideTeamMappingAsync(int resultId, int team1Id, int team2Id);

    /// <summary>
    /// Delete a match result.
    /// </summary>
    Task DeleteMatchResultAsync(int resultId);

    /// <summary>
    /// Get all match results for a tournament with optional filtering.
    /// </summary>
    Task<List<TournamentMatchResult>> GetMatchResultsAsync(
        int tournamentId,
        string? week = null,
        int page = 1,
        int pageSize = 50);
}
