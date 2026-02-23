namespace api.PlayerRelationships.Models;

/// <summary>
/// Represents a co-play session detected from overlapping PlayerObservations.
/// Used for ETL to Neo4j.
/// </summary>
public record CoPlaySession
{
    public required string Player1Name { get; init; }
    public required string Player2Name { get; init; }
    public required string ServerGuid { get; init; }
    public required DateTime SessionStart { get; init; }
    public required DateTime SessionEnd { get; init; }
    public int DurationMinutes { get; init; }
    public int Player1Score { get; init; }
    public int Player2Score { get; init; }
    public string MapName { get; init; } = "";
    public string GameType { get; init; } = "";
}

/// <summary>
/// Aggregated relationship data between two players.
/// Used to create/update PLAYED_WITH relationships in Neo4j.
/// </summary>
public record PlayerRelationship
{
    public required string Player1Name { get; init; }
    public required string Player2Name { get; init; }
    public int SessionCount { get; init; }
    public int TotalMinutes { get; init; }
    public DateTime FirstPlayedTogether { get; init; }
    public DateTime LastPlayedTogether { get; init; }
    public List<string> ServerGuids { get; init; } = [];
    public double AvgScoreDiff { get; init; }
    
    /// <summary>
    /// Whether this is a recent connection (e.g., within last 7 days).
    /// </summary>
    public bool IsRecent => (DateTime.UtcNow - LastPlayedTogether).TotalDays <= 7;
    
    /// <summary>
    /// Whether this is an active connection (e.g., within last 30 days).
    /// </summary>
    public bool IsActive => (DateTime.UtcNow - LastPlayedTogether).TotalDays <= 30;
}

/// <summary>
/// Player-Server relationship data.
/// </summary>
public record PlayerServerRelationship
{
    public required string PlayerName { get; init; }
    public required string ServerGuid { get; init; }
    public int SessionCount { get; init; }
    public int TotalMinutes { get; init; }
    public DateTime LastPlayed { get; init; }
}
