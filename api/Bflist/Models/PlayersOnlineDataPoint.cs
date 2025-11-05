namespace api.Bflist.Models;

public class PlayersOnlineDataPoint
{
    /// <summary>
    /// Timestamp of the data point
    /// </summary>
    public DateTime Timestamp { get; set; }

    /// <summary>
    /// Total number of players online at this timestamp
    /// </summary>
    public int TotalPlayers { get; set; }
}
