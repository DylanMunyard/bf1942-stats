namespace api.PlayerRelationships.Models;

public record PlayerNetworkStats
{
    public required string PlayerName { get; init; }
    public int ConnectionCount { get; init; }
    public int TotalCoPlaySessions { get; init; }
    public int ServerCount { get; init; }
    public DateTime FirstSeen { get; init; }
    public DateTime LastSeen { get; init; }
}
