using api.PlayerTracking;

namespace api.Gamification.Services;

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

    /// <summary>
    /// Create or update a tournament match result with manually entered data (no round required).
    /// Allows tournament organizers to manually enter match results.
    /// </summary>
    /// <summary>
    /// Create a new tournament match result with manually entered data (no round required).
    /// Allows tournament organizers to manually enter match results. Each call creates a new result.
    /// To update an existing result, use the manual-update endpoint with the result ID.
    /// </summary>
    Task<(int ResultId, string? WarningMessage)> CreateOrUpdateManualMatchResultAsync(
        int tournamentId,
        int matchId,
        int mapId,
        int? team1Id,
        int? team2Id,
        int? team1Tickets,
        int? team2Tickets,
        int? winningTeamId = null,
        string? roundId = null);
}
