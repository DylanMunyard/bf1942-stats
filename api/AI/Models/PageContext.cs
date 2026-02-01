namespace api.AI.Models;

/// <summary>
/// Represents the current page context for AI interactions.
/// </summary>
public record PageContext
{
    /// <summary>
    /// Type of page: player, server, round, home.
    /// </summary>
    public string? PageType { get; init; }

    /// <summary>
    /// Player name if on a player page.
    /// </summary>
    public string? PlayerName { get; init; }

    /// <summary>
    /// Server GUID if on a server page.
    /// </summary>
    public string? ServerGuid { get; init; }

    /// <summary>
    /// Server display name if on a server page (used for plugin calls and AI responses).
    /// </summary>
    public string? ServerName { get; init; }

    /// <summary>
    /// Game identifier: bf1942, fh2, bfvietnam.
    /// </summary>
    public string? Game { get; init; }
}
