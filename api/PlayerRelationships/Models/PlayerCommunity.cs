namespace api.PlayerRelationships.Models;

/// <summary>
/// Represents a detected community of players who frequently play together.
/// </summary>
public record PlayerCommunity
{
    /// <summary>
    /// Unique identifier for the community.
    /// </summary>
    public required string Id { get; init; }
    
    /// <summary>
    /// Community name (can be auto-generated or set by members).
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// List of player names in the community.
    /// </summary>
    public required List<string> Members { get; init; }
    
    /// <summary>
    /// The most connected/central players in the community.
    /// </summary>
    public required List<string> CoreMembers { get; init; }
    
    /// <summary>
    /// Primary servers where this community plays.
    /// </summary>
    public required List<string> PrimaryServers { get; init; }
    
    /// <summary>
    /// When the community was first detected.
    /// </summary>
    public required DateTime FormationDate { get; init; }
    
    /// <summary>
    /// Last time the community was active together.
    /// </summary>
    public required DateTime LastActiveDate { get; init; }
    
    /// <summary>
    /// Average sessions per member pair within the community.
    /// </summary>
    public required double AvgSessionsPerPair { get; init; }
    
    /// <summary>
    /// Community cohesion score (0-1, higher means more tightly connected).
    /// </summary>
    public required double CohesionScore { get; init; }
    
    /// <summary>
    /// Total unique member count.
    /// </summary>
    public int MemberCount => Members.Count;
    
    /// <summary>
    /// Whether the community is currently active.
    /// </summary>
    public bool IsActive => (DateTime.UtcNow - LastActiveDate).TotalDays <= 30;
}

/// <summary>
/// Server activity for a community.
/// </summary>
public record ServerActivity
{
    /// <summary>
    /// Server GUID.
    /// </summary>
    public required string ServerGuid { get; init; }
    
    /// <summary>
    /// Server name.
    /// </summary>
    public required string ServerName { get; init; }
    
    /// <summary>
    /// Percentage of community activity on this server.
    /// </summary>
    public required double ActivityPercentage { get; init; }
}