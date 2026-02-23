namespace api.PlayerRelationships.Models;

/// <summary>
/// Represents player migration data between servers.
/// </summary>
public record PlayerMigrationFlow
{
    /// <summary>
    /// Time period for this flow data.
    /// </summary>
    public required DateTime StartDate { get; init; }
    public required DateTime EndDate { get; init; }
    
    /// <summary>
    /// Migration links between servers.
    /// </summary>
    public required List<MigrationLink> Links { get; init; }
    
    /// <summary>
    /// Server nodes with metadata.
    /// </summary>
    public required List<ServerNode> Nodes { get; init; }
}

/// <summary>
/// A migration link showing player movement from one server to another.
/// </summary>
public record MigrationLink
{
    /// <summary>
    /// Source server GUID.
    /// </summary>
    public required string SourceGuid { get; init; }
    
    /// <summary>
    /// Target server GUID.
    /// </summary>
    public required string TargetGuid { get; init; }
    
    /// <summary>
    /// Number of unique players who migrated.
    /// </summary>
    public required int PlayerCount { get; init; }
    
    /// <summary>
    /// Total sessions that moved.
    /// </summary>
    public required int SessionCount { get; init; }
    
    /// <summary>
    /// Average days between last play on source and first play on target.
    /// </summary>
    public required double AvgMigrationDays { get; init; }
}

/// <summary>
/// Server node in the migration flow.
/// </summary>
public record ServerNode
{
    /// <summary>
    /// Server GUID.
    /// </summary>
    public required string Guid { get; init; }
    
    /// <summary>
    /// Server name.
    /// </summary>
    public required string Name { get; init; }
    
    /// <summary>
    /// Game type.
    /// </summary>
    public required string Game { get; init; }
    
    /// <summary>
    /// Total player inflow.
    /// </summary>
    public required int Inflow { get; init; }
    
    /// <summary>
    /// Total player outflow.
    /// </summary>
    public required int Outflow { get; init; }
    
    /// <summary>
    /// Net migration (inflow - outflow).
    /// </summary>
    public int NetMigration => Inflow - Outflow;
    
    /// <summary>
    /// Server lifecycle stage during this period.
    /// </summary>
    public required string LifecycleStage { get; init; } // "Growing", "Stable", "Declining", "Dead"
}