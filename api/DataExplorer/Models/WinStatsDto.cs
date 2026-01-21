namespace api.DataExplorer.Models;

/// <summary>
/// Win statistics for Axis vs Allied teams.
/// </summary>
public record WinStatsDto(
    string Team1Label,
    string Team2Label,
    int Team1Victories,
    int Team2Victories,
    double Team1WinPercentage,
    double Team2WinPercentage,
    int TotalRounds
);
