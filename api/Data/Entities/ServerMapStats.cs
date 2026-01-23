using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed monthly server map statistics.
/// Aggregated from Rounds table - provides map insights per server.
/// Used by /stats/servers/{name}/insights/maps endpoint.
/// </summary>
public class ServerMapStats
{
    public required string ServerGuid { get; set; }
    public required string MapName { get; set; }
    public int Year { get; set; }
    public int Month { get; set; } // 1-12

    /// <summary>
    /// Total number of rounds played on this map.
    /// </summary>
    public int TotalRounds { get; set; }

    /// <summary>
    /// Total play time in minutes (sum of round durations).
    /// </summary>
    public int TotalPlayTimeMinutes { get; set; }

    /// <summary>
    /// Average concurrent players across all rounds.
    /// </summary>
    public double AvgConcurrentPlayers { get; set; }

    /// <summary>
    /// Peak concurrent players (max ParticipantCount across rounds).
    /// </summary>
    public int PeakConcurrentPlayers { get; set; }

    /// <summary>
    /// Number of rounds won by Team 1 (Tickets2 less than Tickets1 at round end).
    /// </summary>
    public int Team1Victories { get; set; }

    /// <summary>
    /// Number of rounds won by Team 2 (Tickets1 less than Tickets2 at round end).
    /// </summary>
    public int Team2Victories { get; set; }

    /// <summary>
    /// Most common Team 1 label on this map.
    /// </summary>
    public string? Team1Label { get; set; }

    /// <summary>
    /// Most common Team 2 label on this map.
    /// </summary>
    public string? Team2Label { get; set; }

    public Instant UpdatedAt { get; set; }
}
