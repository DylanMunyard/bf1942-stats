using api.ImageStorage.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.ImageStorage;

/// <summary>
/// Manages tournament map images for organizers
/// </summary>
[ApiController]
[Route("stats/admin/images")]
[Authorize]
public class ImageStorageController(IImageIndexingService imageIndexingService, ILogger<ImageStorageController> logger) : ControllerBase
{
    /// <summary>
    /// Scans the tournament-images directory and indexes all images
    /// This triggers image discovery and thumbnail generation
    /// Limited concurrent processing to manage CPU usage
    /// </summary>
    /// <remarks>
    /// Admin only operation. Can take several seconds to complete depending on number of images
    /// </remarks>
    [HttpPost("scan")]
    public async Task<ActionResult<ScanResultResponse>> ScanAndIndexImages(CancellationToken cancellationToken)
    {
        try
        {
            logger.LogInformation("Starting image scan and indexing");
            var result = await imageIndexingService.ScanAndIndexImagesAsync(cancellationToken);
            return Ok(result);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error scanning images");
            return StatusCode(500, new { error = "Failed to scan images", message = ex.Message });
        }
    }

    /// <summary>
    /// Gets list of available folders containing tournament images
    /// </summary>
    [HttpGet("folders")]
    public async Task<ActionResult<FolderListResponse>> GetFolders(CancellationToken cancellationToken)
    {
        try
        {
            var folders = await imageIndexingService.GetFoldersAsync(cancellationToken);
            return Ok(folders);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting folders");
            return StatusCode(500, new { error = "Failed to get folders", message = ex.Message });
        }
    }

    /// <summary>
    /// Gets all images in a specific folder with thumbnail previews
    /// </summary>
    /// <param name="folderPath">The folder path, e.g., "golden-gun" or "vanilla"</param>
    /// <param name="page">Page number (1-based), default is 1</param>
    /// <param name="pageSize">Number of images per page, default is 10, maximum is 10</param>
    [HttpGet("folders/{folderPath}")]
    public async Task<ActionResult<FolderContentsResponse>> GetFolderContents(
        string folderPath,
        [FromQuery] int page = Constants.ApiConstants.Pagination.DefaultPage,
        [FromQuery] int pageSize = Constants.ApiConstants.Pagination.ImageStorageDefaultPageSize,
        CancellationToken cancellationToken = default)
    {
        try
        {
            // Basic validation to prevent directory traversal
            if (folderPath.Contains("..") || folderPath.Contains("/") || folderPath.Contains("\\"))
            {
                return BadRequest(new { error = "Invalid folder path" });
            }

            // Validate pagination parameters
            if (page < 1)
                return BadRequest(Constants.ApiConstants.ValidationMessages.PageNumberTooLow);

            if (pageSize < 1 || pageSize > Constants.ApiConstants.Pagination.ImageStorageMaxPageSize)
                return BadRequest(Constants.ApiConstants.ValidationMessages.PageSizeTooLarge(Constants.ApiConstants.Pagination.ImageStorageMaxPageSize));

            var contents = await imageIndexingService.GetFolderContentsAsync(folderPath, page, pageSize, cancellationToken);
            return Ok(contents);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting folder contents for {FolderPath}", folderPath);
            return StatusCode(500, new { error = "Failed to get folder contents", message = ex.Message });
        }
    }
}
