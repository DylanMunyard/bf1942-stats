namespace api.Constants;

/// <summary>
/// Global constants for API pagination, validation, and filtering.
/// </summary>
public static class ApiConstants
{
    /// <summary>
    /// Pagination default and maximum values.
    /// </summary>
    public static class Pagination
    {
        public const int DefaultPage = 1;
        public const int DefaultPageSize = 50;
        public const int MaxPageSize = 500;
        public const int SearchDefaultPageSize = 20;
        public const int SearchMaxPageSize = 100;
    }

    /// <summary>
    /// Sorting default values.
    /// </summary>
    public static class Sorting
    {
        public const string AscendingOrder = "asc";
        public const string DescendingOrder = "desc";
        public static readonly string[] ValidSortOrders = { AscendingOrder, DescendingOrder };
    }

    /// <summary>
    /// Server sorting field names.
    /// </summary>
    public static class ServerSortFields
    {
        public const string ServerName = "ServerName";
        public const string GameId = "GameId";
        public const string Country = "Country";
        public const string Region = "Region";
        public const string TotalPlayersAllTime = "TotalPlayersAllTime";
        public const string TotalActivePlayersLast24h = "TotalActivePlayersLast24h";
        public const string LastActivity = "LastActivity";

        public static readonly string[] ValidFields =
        {
            ServerName,
            GameId,
            Country,
            Region,
            TotalPlayersAllTime,
            TotalActivePlayersLast24h,
            LastActivity
        };
    }

    /// <summary>
    /// Player sorting field names.
    /// </summary>
    public static class PlayerSortFields
    {
        public const string PlayerName = "PlayerName";
        public const string TotalPlayTimeMinutes = "TotalPlayTimeMinutes";
        public const string LastSeen = "LastSeen";
        public const string IsActive = "IsActive";

        public static readonly string[] ValidFields =
        {
            PlayerName,
            TotalPlayTimeMinutes,
            LastSeen,
            IsActive
        };
    }

    /// <summary>
    /// Supported games.
    /// </summary>
    public static class Games
    {
        public const string BattleField1942 = "bf1942";
        public const string ForgottenHope2 = "fh2";
        public const string BattleFieldVietnam = "bfvietnam";

        public static readonly string[] AllowedGames =
        {
            BattleField1942,
            ForgottenHope2,
            BattleFieldVietnam
        };
    }

    /// <summary>
    /// Time period defaults.
    /// </summary>
    public static class TimePeriods
    {
        public const int DefaultDays = 7;
    }

    /// <summary>
    /// Similarity search limits.
    /// </summary>
    public static class SimilaritySearch
    {
        public const int DefaultLimit = 10;
        public const int MaxLimit = 50;
        public const int MinLimit = 1;
    }

    /// <summary>
    /// Validation error messages.
    /// </summary>
    public static class ValidationMessages
    {
        public const string ServerNameEmpty = "Server name cannot be empty";
        public const string PlayerNameEmpty = "Player name cannot be empty";
        public const string SearchQueryEmpty = "Search query cannot be empty";
        public const string QueryEmpty = "Query cannot be empty";
        public const string ServerGuidEmpty = "Server GUID cannot be empty";
        public const string BothPlayersRequired = "Both player1 and player2 must be provided";

        public const string PageNumberTooLow = "Page number must be at least 1";
        public const string PageSizeTooSmall = "Page size must be at least 1";
        public static string PageSizeTooLarge(int maxSize) => $"Page size must be between 1 and {maxSize}";

        public static string InvalidSortField(string validOptions) =>
            $"Invalid sortBy field. Valid options: {validOptions}";

        public const string InvalidSortOrder = "Sort order must be 'asc' or 'desc'";

        public static string InvalidGame(string validOptions) =>
            $"Invalid game. Allowed values: {validOptions}";

        public const string MinimumTotalPlayersNegative = "Minimum total players cannot be negative";
        public const string MaximumTotalPlayersNegative = "Maximum total players cannot be negative";
        public const string MinimumTotalPlayersGreaterThanMaximum =
            "Minimum total players cannot be greater than maximum total players";

        public const string MinimumActivePlayersNegative = "Minimum active players last 24h cannot be negative";
        public const string MaximumActivePlayersNegative = "Maximum active players last 24h cannot be negative";
        public const string MinimumActivePlayersGreaterThanMaximum =
            "Minimum active players last 24h cannot be greater than maximum active players last 24h";

        public const string MinimumPlayTimeNegative = "Minimum play time cannot be negative";
        public const string MaximumPlayTimeNegative = "Maximum play time cannot be negative";
        public const string MinimumPlayTimeGreaterThanMaximum =
            "Minimum play time cannot be greater than maximum play time";

        public static string LastActivityFromGreaterThanTo(string fieldName) =>
            $"{fieldName}From cannot be greater than {fieldName}To";

        public static string ItemNotFound(string itemType, string identifier) =>
            $"{itemType} '{identifier}' not found";

        public static string InvalidRange(string rangeName) =>
            $"Invalid range. Valid options: {rangeName}";

        public const string LimitOutOfRange = "Limit must be between 1 and 50";

        public static string InvalidMode(string validOptions) =>
            $"Invalid mode. Valid options: {validOptions}";

        public const string InternalServerError = "An internal server error occurred";
        public const string InternalServerErrorComparingPlayers =
            "An internal server error occurred while comparing players";
        public const string InternalServerErrorFindingSimilarPlayers =
            "An internal server error occurred while finding similar players";
        public const string InternalServerErrorComparingActivityHours =
            "An internal server error occurred while comparing player activity hours";
        public const string InternalServerErrorRetrievingServerStats =
            "An error occurred while retrieving server statistics";
    }
}
