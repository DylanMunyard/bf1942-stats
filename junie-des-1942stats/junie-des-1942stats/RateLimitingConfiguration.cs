namespace junie_des_1942stats;

/// <summary>
/// Rate limiting configuration for API endpoints.
/// Helps protect against abuse and DOS attacks while allowing reasonable usage.
/// </summary>
public static class RateLimitingConfiguration
{
    /// <summary>
    /// Default policy name for general API rate limiting.
    /// </summary>
    public const string DefaultPolicyName = "default";

    /// <summary>
    /// Policy name for search endpoints (more lenient).
    /// </summary>
    public const string SearchPolicyName = "search";

    /// <summary>
    /// Policy name for comparison endpoints (strict).
    /// </summary>
    public const string ComparisonPolicyName = "comparison";

    /// <summary>
    /// Configuration for the default rate limiting policy.
    /// </summary>
    public static class DefaultPolicy
    {
        /// <summary>
        /// Maximum number of requests per window.
        /// </summary>
        public const int RequestLimit = 100;

        /// <summary>
        /// Time window in seconds.
        /// </summary>
        public const int WindowInSeconds = 60; // 100 requests per minute

        /// <summary>
        /// Whether to queue requests that exceed the limit.
        /// </summary>
        public const bool AutoReplenishment = true;
    }

    /// <summary>
    /// Configuration for search endpoint rate limiting.
    /// Search is typically CPU-intensive, so we limit it more.
    /// </summary>
    public static class SearchPolicy
    {
        /// <summary>
        /// Maximum number of search requests per window.
        /// </summary>
        public const int RequestLimit = 30;

        /// <summary>
        /// Time window in seconds.
        /// </summary>
        public const int WindowInSeconds = 60; // 30 requests per minute

        /// <summary>
        /// Whether to queue requests that exceed the limit.
        /// </summary>
        public const bool AutoReplenishment = true;
    }

    /// <summary>
    /// Configuration for player comparison endpoints.
    /// Comparisons are expensive operations, so we limit them strictly.
    /// </summary>
    public static class ComparisonPolicy
    {
        /// <summary>
        /// Maximum number of comparison requests per window.
        /// </summary>
        public const int RequestLimit = 20;

        /// <summary>
        /// Time window in seconds.
        /// </summary>
        public const int WindowInSeconds = 60; // 20 requests per minute

        /// <summary>
        /// Whether to queue requests that exceed the limit.
        /// </summary>
        public const bool AutoReplenishment = true;
    }

    /// <summary>
    /// Error message returned when rate limit is exceeded.
    /// </summary>
    public const string RateLimitExceededMessage = "Too many requests. Please try again later.";

    /// <summary>
    /// Retry-After header value in seconds for rate-limited responses.
    /// </summary>
    public const int RetryAfterSeconds = 60;
}
