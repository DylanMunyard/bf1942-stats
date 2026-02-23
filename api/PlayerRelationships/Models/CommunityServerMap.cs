namespace api.PlayerRelationships.Models;

/// <summary>
/// Represents the server-player network for a community.
/// Used for bipartite graph visualization showing which players play on which servers.
/// </summary>
public record CommunityServerMap
{
    /// <summary>
    /// List of player nodes for the graph.
    /// </summary>
    public required List<ServerMapNode> Players { get; init; }

    /// <summary>
    /// List of server nodes for the graph.
    /// </summary>
    public required List<ServerMapNode> Servers { get; init; }

    /// <summary>
    /// Edges between players and servers, weighted by session count.
    /// </summary>
    public required List<ServerMapEdge> Edges { get; init; }
}

/// <summary>
/// A node in the server-player graph (either a player or server).
/// </summary>
public record ServerMapNode
{
    /// <summary>
    /// Unique identifier (player name or server GUID).
    /// </summary>
    public required string Id { get; init; }

    /// <summary>
    /// Display label (player name or server name).
    /// </summary>
    public required string Label { get; init; }

    /// <summary>
    /// Node type: "player" or "server".
    /// </summary>
    public required string Type { get; init; }

    /// <summary>
    /// For players: core member status. For servers: unused.
    /// </summary>
    public bool IsCore { get; init; }
}

/// <summary>
/// An edge in the server-player graph showing a player playing on a server.
/// </summary>
public record ServerMapEdge
{
    /// <summary>
    /// Source node ID (player name).
    /// </summary>
    public required string Source { get; init; }

    /// <summary>
    /// Target node ID (server GUID).
    /// </summary>
    public required string Target { get; init; }

    /// <summary>
    /// Number of sessions played on this server (used for edge thickness).
    /// </summary>
    public required int Weight { get; init; }

    /// <summary>
    /// Last time this player played on this server.
    /// </summary>
    public required DateTime LastPlayed { get; init; }
}
