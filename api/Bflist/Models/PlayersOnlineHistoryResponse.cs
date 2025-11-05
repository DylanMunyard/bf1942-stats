namespace api.Bflist.Models;

public class PlayersOnlineHistoryResponse
{
    /// <summary>
    /// Array of player count data points over time
    /// </summary>
    public PlayersOnlineDataPoint[] DataPoints { get; set; } = [];

    /// <summary>
    /// Trend analysis and insights for the requested period
    /// </summary>
    public PlayerTrendsInsights? Insights { get; set; }

    /// <summary>
    /// The period for which the data was requested (e.g., "7d", "3d", "1d", "1month", "3months", "thisyear", "alltime")
    /// </summary>
    public string Period { get; set; } = "";

    /// <summary>
    /// The game for which the data was requested
    /// </summary>
    public string Game { get; set; } = "";

    /// <summary>
    /// When the data was last updated
    /// </summary>
    public string LastUpdated { get; set; } = "";
}
