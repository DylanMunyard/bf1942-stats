using Microsoft.AspNetCore.Mvc;

namespace api.ImageStorage;

/// <summary>
/// Public endpoints for serving assets (images, files, etc.)
/// No authentication required - safe for public distribution
/// Supports multiple asset types through different routes
/// </summary>
[ApiController]
[Route("stats/assets")]
public class AssetsController(IAssetServingService assetServingService) : ControllerBase
{
    /// <summary>
    /// Get a tournament asset file by relative path
    /// Path should be relative to the tournaments folder, e.g., "golden-gun/map1.png"
    /// </summary>
    /// <param name="path">Relative path to the asset file within tournaments</param>
    [HttpGet("tournaments/{*path}")]
    public async Task<IActionResult> GetTournamentAsset(string path)
    {
        var basePath = TournamentImagesConfig.ResolveTournamentsPath();
        var result = await assetServingService.GetAssetAsync(basePath, path);
        return HandleAssetResult(result);
    }

    /// <summary>
    /// Get a generic asset file from the assets root
    /// Path should be relative to the assets directory, e.g., "tournaments/golden-gun/map1.png"
    /// </summary>
    /// <param name="path">Relative path to the asset file</param>
    [HttpGet("{*path}")]
    public async Task<IActionResult> GetAsset(string path)
    {
        var basePath = TournamentImagesConfig.ResolveBasePath();
        var result = await assetServingService.GetAssetAsync(basePath, path);
        return HandleAssetResult(result);
    }

    private IActionResult HandleAssetResult(AssetResult result)
    {
        if (!result.IsSuccess)
        {
            return result.StatusCode switch
            {
                400 => BadRequest(new { error = result.ErrorMessage }),
                403 => Forbid(),
                404 => NotFound(new { error = result.ErrorMessage }),
                500 => StatusCode(500, new { error = result.ErrorMessage }),
                _ => StatusCode(result.StatusCode, new { error = result.ErrorMessage })
            };
        }

        return File(result.FileStream!, result.ContentType!, result.FileName, enableRangeProcessing: true);
    }
}
