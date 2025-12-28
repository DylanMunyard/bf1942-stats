using api.ClickHouse.Models;

namespace api.PlayerStats;

/// <summary>
/// SQLite-based player comparison service that queries pre-computed tables.
/// </summary>
public interface ISqlitePlayerComparisonService
{
    /// <summary>
    /// Compares two players and returns a comprehensive comparison result.
    /// This is the main entry point for player comparison, matching the ClickHouse interface.
    /// </summary>
    Task<PlayerComparisonResult> ComparePlayersAsync(string player1, string player2, string? serverGuid = null);

    /// <summary>
    /// Gets bucket totals comparison between two players (AllTime, Last30Days, Last6Months, LastYear).
    /// </summary>
    Task<List<BucketTotalsComparison>> GetBucketTotalsAsync(string player1, string player2, string? serverGuid = null);

    /// <summary>
    /// Gets map performance comparison between two players.
    /// </summary>
    Task<List<MapPerformanceComparison>> GetMapPerformanceAsync(string player1, string player2, string? serverGuid = null);

    /// <summary>
    /// Gets head-to-head sessions where both players overlapped in the same round.
    /// Returns sessions and the set of server GUIDs involved.
    /// </summary>
    Task<(List<HeadToHeadSession> Sessions, HashSet<string> ServerGuids)> GetHeadToHeadDataAsync(
        string player1, string player2, string? serverGuid = null);

    /// <summary>
    /// Gets common servers where both players have played in the last 6 months.
    /// Returns server details and the set of server GUIDs.
    /// </summary>
    Task<(List<ServerDetails> Servers, HashSet<string> ServerGuids)> GetCommonServersDataAsync(
        string player1, string player2);
}
