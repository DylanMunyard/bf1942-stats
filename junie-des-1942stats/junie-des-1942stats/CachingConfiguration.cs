namespace junie_des_1942stats;

/// <summary>
/// Caching configuration constants for API responses.
/// Specifies cache durations and vary-by keys for different endpoint categories.
/// </summary>
public static class CachingConfiguration
{
    /// <summary>
    /// Cache duration for server statistics (in seconds).
    /// Server stats change infrequently, so longer cache is acceptable.
    /// </summary>
    public const int ServerStatsCacheDurationSeconds = 300; // 5 minutes

    /// <summary>
    /// Cache duration for server leaderboards (in seconds).
    /// </summary>
    public const int ServerLeaderboardsCacheDurationSeconds = 300; // 5 minutes

    /// <summary>
    /// Cache duration for server insights (in seconds).
    /// </summary>
    public const int ServerInsightsCacheDurationSeconds = 300; // 5 minutes

    /// <summary>
    /// Cache duration for server list/search results (in seconds).
    /// </summary>
    public const int ServerListCacheDurationSeconds = 300; // 5 minutes

    /// <summary>
    /// Cache duration for player statistics (in seconds).
    /// Player stats are more frequently updated.
    /// </summary>
    public const int PlayerStatsCacheDurationSeconds = 180; // 3 minutes

    /// <summary>
    /// Cache duration for player list/search results (in seconds).
    /// </summary>
    public const int PlayerListCacheDurationSeconds = 180; // 3 minutes

    /// <summary>
    /// Cache duration for player comparison results (in seconds).
    /// Comparisons depend on both players' stats which update frequently.
    /// </summary>
    public const int PlayerComparisonCacheDurationSeconds = 120; // 2 minutes

    /// <summary>
    /// Cache duration for similarity search results (in seconds).
    /// </summary>
    public const int SimilaritySearchCacheDurationSeconds = 120; // 2 minutes

    /// <summary>
    /// Query keys to vary cache by for server statistics.
    /// </summary>
    public static readonly string[] ServerStatsVaryByKeys = { "serverName", "days" };

    /// <summary>
    /// Query keys to vary cache by for server leaderboards.
    /// </summary>
    public static readonly string[] ServerLeaderboardsVaryByKeys = { "serverName", "days", "minPlayersForWeighting" };

    /// <summary>
    /// Query keys to vary cache by for server insights.
    /// </summary>
    public static readonly string[] ServerInsightsVaryByKeys = { "serverName", "days" };

    /// <summary>
    /// Query keys to vary cache by for server list.
    /// </summary>
    public static readonly string[] ServerListVaryByKeys =
    {
        "page",
        "pageSize",
        "sortBy",
        "sortOrder",
        "serverName",
        "gameId",
        "game",
        "country",
        "region",
        "hasActivePlayers",
        "minTotalPlayers",
        "maxTotalPlayers"
    };

    /// <summary>
    /// Query keys to vary cache by for player statistics.
    /// </summary>
    public static readonly string[] PlayerStatsVaryByKeys = { "playerName" };

    /// <summary>
    /// Query keys to vary cache by for player list.
    /// </summary>
    public static readonly string[] PlayerListVaryByKeys =
    {
        "page",
        "pageSize",
        "sortBy",
        "sortOrder",
        "playerName",
        "isActive",
        "minPlayTime",
        "maxPlayTime"
    };

    /// <summary>
    /// Query keys to vary cache by for player comparison.
    /// </summary>
    public static readonly string[] PlayerComparisonVaryByKeys = { "player1", "player2", "serverGuid" };

    /// <summary>
    /// Query keys to vary cache by for similarity search.
    /// </summary>
    public static readonly string[] SimilaritySearchVaryByKeys = { "playerName", "limit", "mode" };
}
