using System.ComponentModel.DataAnnotations;

namespace junie_des_1942stats;

/// <summary>
/// Base class for paginated requests.
/// </summary>
public abstract class PaginatedRequest
{
    [Range(1, int.MaxValue, ErrorMessage = "Page number must be at least 1")]
    public int Page { get; set; } = ApiConstants.Pagination.DefaultPage;

    [Range(1, int.MaxValue, ErrorMessage = "Page size must be at least 1")]
    public int PageSize { get; set; } = ApiConstants.Pagination.DefaultPageSize;

    [Required]
    public virtual string SortBy { get; set; } = string.Empty;

    [RegularExpression("^(asc|desc)$", ErrorMessage = "Sort order must be 'asc' or 'desc'")]
    public virtual string SortOrder { get; set; } = ApiConstants.Sorting.AscendingOrder;
}

/// <summary>
/// Request for getting all servers with filtering and pagination.
/// </summary>
public class GetAllServersRequest : PaginatedRequest
{
    public override string SortBy { get; set; } = ApiConstants.ServerSortFields.ServerName;

    [StringLength(255)]
    public string? ServerName { get; set; }

    [StringLength(100)]
    public string? GameId { get; set; }

    [StringLength(50)]
    [RegularExpression("^(bf1942|fh2|bfvietnam)$", ErrorMessage = "Invalid game value")]
    public string? Game { get; set; }

    [StringLength(100)]
    public string? Country { get; set; }

    [StringLength(100)]
    public string? Region { get; set; }

    public bool? HasActivePlayers { get; set; }

    public DateTime? LastActivityFrom { get; set; }
    public DateTime? LastActivityTo { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinTotalPlayers { get; set; }

    [Range(0, int.MaxValue)]
    public int? MaxTotalPlayers { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinActivePlayersLast24h { get; set; }

    [Range(0, int.MaxValue)]
    public int? MaxActivePlayersLast24h { get; set; }
}

/// <summary>
/// Request for searching servers.
/// </summary>
public class SearchServersRequest
{
    [Required(ErrorMessage = "Search query cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    [StringLength(50)]
    [RegularExpression("^(bf1942|fh2|bfvietnam)$", ErrorMessage = "Invalid game value")]
    public string? Game { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Page number must be at least 1")]
    public int Page { get; set; } = ApiConstants.Pagination.DefaultPage;

    [Range(1, int.MaxValue, ErrorMessage = "Page size must be at least 1")]
    public int PageSize { get; set; } = ApiConstants.Pagination.SearchDefaultPageSize;
}

/// <summary>
/// Request for getting server rankings.
/// </summary>
public class GetServerRankingsRequest
{
    [Required(ErrorMessage = "Server name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string ServerName { get; set; } = string.Empty;

    [Range(1900, 2100)]
    public int? Year { get; set; }

    [Range(1, int.MaxValue, ErrorMessage = "Page number must be at least 1")]
    public int Page { get; set; } = ApiConstants.Pagination.DefaultPage;

    [Range(1, int.MaxValue, ErrorMessage = "Page size must be at least 1")]
    public int PageSize { get; set; } = 100;

    [StringLength(255)]
    public string? PlayerName { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinScore { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinKills { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinDeaths { get; set; }

    [Range(0, double.MaxValue)]
    public double? MinKdRatio { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinPlayTimeMinutes { get; set; }

    [StringLength(50)]
    public string? OrderBy { get; set; }

    [RegularExpression("^(asc|desc)$", ErrorMessage = "Sort order must be 'asc' or 'desc'")]
    public string? OrderDirection { get; set; }
}

/// <summary>
/// Request for getting all players with filtering and pagination.
/// </summary>
public class GetAllPlayersRequest : PaginatedRequest
{
    public override string SortBy { get; set; } = ApiConstants.PlayerSortFields.IsActive;
    public override string SortOrder { get; set; } = ApiConstants.Sorting.DescendingOrder;

    [StringLength(255)]
    public string? PlayerName { get; set; }

    [Range(0, int.MaxValue)]
    public int? MinPlayTime { get; set; }

    [Range(0, int.MaxValue)]
    public int? MaxPlayTime { get; set; }

    public DateTime? LastSeenFrom { get; set; }
    public DateTime? LastSeenTo { get; set; }

    public bool? IsActive { get; set; }

    [StringLength(255)]
    public string? ServerName { get; set; }

    [StringLength(100)]
    public string? GameId { get; set; }

    [StringLength(50)]
    [RegularExpression("^(bf1942|fh2|bfvietnam)$", ErrorMessage = "Invalid game value")]
    public string? Game { get; set; }

    [StringLength(255)]
    public string? MapName { get; set; }
}

/// <summary>
/// Request for searching players.
/// </summary>
public class SearchPlayersRequest
{
    [Required(ErrorMessage = "Search query cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string Query { get; set; } = string.Empty;

    [Range(1, int.MaxValue, ErrorMessage = "Page number must be at least 1")]
    public int Page { get; set; } = ApiConstants.Pagination.DefaultPage;

    [Range(1, int.MaxValue, ErrorMessage = "Page size must be at least 1")]
    public int PageSize { get; set; } = ApiConstants.Pagination.SearchDefaultPageSize;
}

/// <summary>
/// Request for comparing two players.
/// </summary>
public class ComparePlayersRequest
{
    [Required(ErrorMessage = "Player 1 name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string Player1 { get; set; } = string.Empty;

    [Required(ErrorMessage = "Player 2 name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string Player2 { get; set; } = string.Empty;

    [StringLength(36)]
    public string? ServerGuid { get; set; }
}

/// <summary>
/// Request for getting similar players.
/// </summary>
public class GetSimilarPlayersRequest
{
    [Required(ErrorMessage = "Player name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string PlayerName { get; set; } = string.Empty;

    [Range(1, 50, ErrorMessage = "Limit must be between 1 and 50")]
    public int Limit { get; set; } = ApiConstants.SimilaritySearch.DefaultLimit;

    [StringLength(50)]
    public string Mode { get; set; } = "default";
}

/// <summary>
/// Request for getting server statistics.
/// </summary>
public class GetServerStatsRequest
{
    [Required(ErrorMessage = "Server name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string ServerName { get; set; } = string.Empty;

    [Range(1, 365)]
    public int? Days { get; set; }
}

/// <summary>
/// Request for getting server leaderboards.
/// </summary>
public class GetServerLeaderboardsRequest
{
    [Required(ErrorMessage = "Server name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string ServerName { get; set; } = string.Empty;

    [Range(1, 365)]
    public int Days { get; set; } = ApiConstants.TimePeriods.DefaultDays;

    [Range(0, int.MaxValue)]
    public int? MinPlayersForWeighting { get; set; }
}

/// <summary>
/// Request for getting server insights.
/// </summary>
public class GetServerInsightsRequest
{
    [Required(ErrorMessage = "Server name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string ServerName { get; set; } = string.Empty;

    [Range(1, 365)]
    public int? Days { get; set; }
}

/// <summary>
/// Request for getting player statistics.
/// </summary>
public class GetPlayerStatsRequest
{
    [Required(ErrorMessage = "Player name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string PlayerName { get; set; } = string.Empty;
}

/// <summary>
/// Request for getting player's server map statistics.
/// </summary>
public class GetPlayerServerMapStatsRequest
{
    [Required(ErrorMessage = "Player name cannot be empty")]
    [StringLength(255, MinimumLength = 1)]
    public string PlayerName { get; set; } = string.Empty;

    [Required(ErrorMessage = "Server GUID cannot be empty")]
    [StringLength(36)]
    public string ServerGuid { get; set; } = string.Empty;

    [StringLength(50)]
    public string Range { get; set; } = "ThisYear";
}
