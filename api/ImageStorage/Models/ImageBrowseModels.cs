using System.Text.Json.Serialization;

namespace api.ImageStorage.Models;

/// <summary>
/// Response for listing images in a folder
/// </summary>
public record ImageResponse(
    [property: JsonPropertyName("id")] int Id,
    [property: JsonPropertyName("fileName")] string FileName,
    [property: JsonPropertyName("relativePath")] string RelativePath,
    [property: JsonPropertyName("thumbnail")] string? ThumbnailBase64, // Base64-encoded PNG
    [property: JsonPropertyName("width")] int Width,
    [property: JsonPropertyName("height")] int Height,
    [property: JsonPropertyName("fileSize")] long FileSize
);

/// <summary>
/// Response for listing available folders and images with pagination
/// </summary>
public record FolderContentsResponse(
    [property: JsonPropertyName("folder")] string FolderPath,
    [property: JsonPropertyName("images")] List<ImageResponse> Images,
    [property: JsonPropertyName("page")] int Page,
    [property: JsonPropertyName("pageSize")] int PageSize,
    [property: JsonPropertyName("totalItems")] int TotalItems,
    [property: JsonPropertyName("totalPages")] int TotalPages
);

/// <summary>
/// Response for listing all folders
/// </summary>
public record FolderListResponse(
    [property: JsonPropertyName("folders")] List<string> Folders
);

/// <summary>
/// Response for image scanning operation
/// </summary>
public record ScanResultResponse(
    [property: JsonPropertyName("newImagesIndexed")] int NewImagesIndexed,
    [property: JsonPropertyName("updatedImagesIndexed")] int UpdatedImagesIndexed,
    [property: JsonPropertyName("foldersFound")] int FoldersFound,
    [property: JsonPropertyName("message")] string Message
);
