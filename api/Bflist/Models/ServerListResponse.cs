namespace api.Bflist.Models;

/// <summary>
/// Response DTO for server list endpoint
/// </summary>
public class ServerListResponse
{
    /// <summary>
    /// Array of server summaries
    /// </summary>
    public ServerSummary[] Servers { get; set; } = [];

    /// <summary>
    /// ISO timestamp of when the data was last updated
    /// </summary>
    public string LastUpdated { get; set; } = "";
}
