namespace api.PlayerRelationships.Models;

public record PlayerNetworkGraph
{
    public required string CenterPlayer { get; init; }
    public List<NetworkNode> Nodes { get; init; } = [];
    public List<NetworkEdge> Edges { get; init; } = [];
    public int Depth { get; init; }
}

public record NetworkNode
{
    public required string Id { get; init; }
    public required string Label { get; init; }
}

public record NetworkEdge
{
    public required string Source { get; init; }
    public required string Target { get; init; }
    public int Weight { get; init; }
    public DateTime LastInteraction { get; init; }
}
