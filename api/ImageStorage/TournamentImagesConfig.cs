namespace api.ImageStorage;

/// <summary>
/// Centralized configuration for tournament images path resolution
/// Single source of truth for both static file serving and image indexing
/// </summary>
public static class TournamentImagesConfig
{
    /// <summary>
    /// Resolves the base assets storage path from environment variable
    /// Must be configured via ASSETS_STORAGE_PATH environment variable in production
    /// </summary>
    public static string ResolveBasePath()
    {
        var assetPath = Environment.GetEnvironmentVariable("ASSETS_STORAGE_PATH");

        if (string.IsNullOrWhiteSpace(assetPath))
        {
            // Fallback for local development
            assetPath = Path.Combine(Directory.GetCurrentDirectory(), "assets");
        }

        // Convert to absolute path if relative
        return Path.GetFullPath(assetPath);
    }

    /// <summary>
    /// Resolves the path to tournament-specific images (under tournaments/ subfolder)
    /// </summary>
    public static string ResolveTournamentsPath()
    {
        return Path.Combine(ResolveBasePath(), "tournaments");
    }

    /// <summary>
    /// Legacy method for backward compatibility - returns tournaments path
    /// </summary>
    public static string ResolvePath() => ResolveTournamentsPath();
}
