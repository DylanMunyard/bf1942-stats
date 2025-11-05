namespace api.Bflist.Models;

/// <summary>
/// Minimal server summary containing only fields needed for the servers table
/// </summary>
public class ServerSummary
{
    /// <summary>
    /// Unique server identifier
    /// </summary>
    public string Guid { get; set; } = "";

    /// <summary>
    /// Server name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Server IP address
    /// </summary>
    public string Ip { get; set; } = "";

    /// <summary>
    /// Server port
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Current number of players
    /// </summary>
    public int NumPlayers { get; set; }

    /// <summary>
    /// Maximum number of players
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>
    /// Current map name
    /// </summary>
    public string MapName { get; set; } = "";

    /// <summary>
    /// Game type/mode
    /// </summary>
    public string GameType { get; set; } = "";

    /// <summary>
    /// Direct join link
    /// </summary>
    public string JoinLink { get; set; } = "";

    /// <summary>
    /// Remaining round time in seconds
    /// </summary>
    public int RoundTimeRemain { get; set; }

    /// <summary>
    /// Team 1 tickets
    /// </summary>
    public int Tickets1 { get; set; }

    /// <summary>
    /// Team 2 tickets
    /// </summary>
    public int Tickets2 { get; set; }

    /// <summary>
    /// Current players on the server
    /// </summary>
    public PlayerInfo[] Players { get; set; } = [];

    /// <summary>
    /// Team information
    /// </summary>
    public TeamInfo[] Teams { get; set; } = [];

    /// <summary>
    /// Server country from geo location data
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Server region from geo location data
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Server city from geo location data
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Server location coordinates (latitude,longitude) from geo location data
    /// </summary>
    public string? Loc { get; set; }

    /// <summary>
    /// Server timezone from geo location data
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Server organization/ASN from geo location data
    /// </summary>
    public string? Org { get; set; }

    /// <summary>
    /// Server postal code from geo location data
    /// </summary>
    public string? Postal { get; set; }

    /// <summary>
    /// Date when geo location was last looked up
    /// </summary>
    public DateTime? GeoLookupDate { get; set; }

    /// <summary>
    /// Whether the server is currently online
    /// </summary>
    public bool IsOnline { get; set; } = true;

    /// <summary>
    /// When the server was last seen online
    /// </summary>
    public DateTime LastSeenTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Server Discord invite URL
    /// </summary>
    public string? DiscordUrl { get; set; }

    /// <summary>
    /// Server forum URL
    /// </summary>
    public string? ForumUrl { get; set; }

    /// <summary>
    /// Game identifier (bf1942, fh2, bfvietnam)
    /// </summary>
    public string GameId { get; set; } = "";
}
