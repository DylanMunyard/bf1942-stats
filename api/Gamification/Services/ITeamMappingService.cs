namespace api.Gamification.Services;

/// <summary>
/// Service for automatically detecting tournament team assignments from round player data.
/// Uses player roster matching: checks if tournament team players appear in round's PlayerSession records.
/// </summary>
public interface ITeamMappingService
{
    /// <summary>
    /// Attempt to auto-detect tournament team mapping for a round.
    /// </summary>
    /// <param name="roundId">The round ID to analyze</param>
    /// <param name="tournamentId">The tournament to match teams from</param>
    /// <returns>Tuple of (Team1Id, Team2Id, WarningMessage). WarningMessage is null if successful.</returns>
    Task<(int Team1Id, int Team2Id, string? WarningMessage)> DetectTeamMappingAsync(string roundId, int tournamentId);
}
