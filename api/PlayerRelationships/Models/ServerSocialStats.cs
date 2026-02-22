namespace api.PlayerRelationships.Models;

public record ServerSocialStats
{
    public required string ServerGuid { get; init; }
    public int UniquePlayerCount { get; init; }
    public double AverageConnectionsPerPlayer { get; init; }
    public int CommunityCount { get; init; }
    public double RetentionRate { get; init; }
}
