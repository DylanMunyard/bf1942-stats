namespace api.PlayerRelationships.Models;

/// <summary>
/// Represents a player's closeness to a server based on their average ping.
/// Lower ping indicates closer proximity.
/// </summary>
public record ServerPlayerCloseness(
    string PlayerName,
    double AvgPing,
    int SessionCount,
    DateTime LastPlayed);
