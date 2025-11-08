using NodaTime;

namespace api.ImageStorage.Models;

/// <summary>
/// Represents indexed metadata for tournament map images stored on the filesystem.
/// Images are organized in folders on the PVC at /data/tournament-images/{folderPath}/{fileName}
/// </summary>
public class TournamentImageIndex
{
    public int Id { get; set; }

    /// <summary>
    /// The folder path under /data/tournament-images/, e.g., "golden-gun", "vanilla"
    /// </summary>
    public string FolderPath { get; set; } = "";

    /// <summary>
    /// The filename, e.g., "map1.png"
    /// </summary>
    public string FileName { get; set; } = "";

    /// <summary>
    /// Full relative path to the image file, e.g., "golden-gun/map1.png"
    /// </summary>
    public string RelativeImagePath { get; set; } = "";

    /// <summary>
    /// Cached thumbnail image (300px width) as PNG bytes
    /// </summary>
    public byte[]? ThumbnailData { get; set; }

    /// <summary>
    /// Original image width in pixels
    /// </summary>
    public int ImageWidth { get; set; }

    /// <summary>
    /// Original image height in pixels
    /// </summary>
    public int ImageHeight { get; set; }

    /// <summary>
    /// File size in bytes
    /// </summary>
    public long FileSize { get; set; }

    /// <summary>
    /// When this index entry was created
    /// </summary>
    public Instant IndexedAt { get; set; }

    /// <summary>
    /// Last modified time of the original image file
    /// Used to detect when image has changed and needs re-indexing
    /// </summary>
    public Instant FileLastModified { get; set; }
}
