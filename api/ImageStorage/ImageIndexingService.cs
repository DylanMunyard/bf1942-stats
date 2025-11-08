using api.ImageStorage.Models;
using api.PlayerTracking;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Processing;

namespace api.ImageStorage;

/// <summary>
/// Service for indexing and managing tournament map images on the PVC filesystem
/// </summary>
public interface IImageIndexingService
{
    /// <summary>
    /// Scans the tournament-images directory and indexes all images
    /// Limited concurrent processing to avoid high CPU usage
    /// </summary>
    Task<ScanResultResponse> ScanAndIndexImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets all folders under tournament-images
    /// </summary>
    Task<FolderListResponse> GetFoldersAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Gets images in a specific folder with thumbnail previews
    /// </summary>
    Task<FolderContentsResponse> GetFolderContentsAsync(string folderPath, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default);
}

public class ImageIndexingService : IImageIndexingService
{
    private readonly PlayerTrackerDbContext _db;
    private readonly ILogger<ImageIndexingService> _logger;
    private readonly string _imagesBasePath;
    private const int ThumbnailWidth = 300;
    private const int MaxConcurrentImages = 3; // Limit CPU impact on main node
    private const int DelayBetweenBatchesMs = 100; // Small delay between batches

    public ImageIndexingService(PlayerTrackerDbContext db, ILogger<ImageIndexingService> logger)
    {
        _db = db;
        _logger = logger;
        _imagesBasePath = TournamentImagesConfig.ResolvePath();
    }

    public async Task<ScanResultResponse> ScanAndIndexImagesAsync(CancellationToken cancellationToken = default)
    {
        try
        {
            if (!Directory.Exists(_imagesBasePath))
            {
                _logger.LogWarning("Tournament images directory not found: {Path}", _imagesBasePath);
                return new ScanResultResponse(0, 0, 0, "Tournament images directory not found");
            }

            var baseDir = new DirectoryInfo(_imagesBasePath);
            var folders = baseDir.GetDirectories();
            int newCount = 0;
            int updatedCount = 0;

            foreach (var folder in folders)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var imageFiles = folder.GetFiles("*.png")
                    .Concat(folder.GetFiles("*.jpg"))
                    .Concat(folder.GetFiles("*.jpeg"))
                    .Concat(folder.GetFiles("*.gif"))
                    .Concat(folder.GetFiles("*.webp"))
                    .ToList();

                // Process images with concurrency limit and delays
                var imageQueue = new Queue<FileInfo>(imageFiles);
                var activeTasks = new List<Task>();

                while (imageQueue.Count > 0 || activeTasks.Count > 0)
                {
                    // Start new tasks up to concurrency limit
                    while (activeTasks.Count < MaxConcurrentImages && imageQueue.Count > 0)
                    {
                        var file = imageQueue.Dequeue();
                        var folderName = folder.Name;
                        activeTasks.Add(IndexSingleImageAsync(file, folderName, cancellationToken));
                    }

                    if (activeTasks.Count > 0)
                    {
                        // Wait for at least one task to complete
                        var completed = await Task.WhenAny(activeTasks);
                        activeTasks.Remove(completed);

                        // Small delay before starting next batch
                        if (imageQueue.Count > 0 && activeTasks.Count == 0)
                        {
                            await Task.Delay(DelayBetweenBatchesMs, cancellationToken);
                        }
                    }
                }

                // Count new and updated
                var folderNewCount = await _db.TournamentImageIndices
                    .Where(x => x.FolderPath == folder.Name && x.IndexedAt > SystemClock.Instance.GetCurrentInstant().Minus(Duration.FromMinutes(5)))
                    .CountAsync(cancellationToken);
                newCount += folderNewCount;
            }

            _logger.LogInformation("Image indexing complete. Folders: {FolderCount}, Processed: {ImageCount} images", folders.Length, newCount + updatedCount);

            return new ScanResultResponse(
                newCount,
                updatedCount,
                folders.Length,
                $"Indexed {newCount} new images and updated {updatedCount} existing images"
            );
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Image indexing was cancelled");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error scanning and indexing images");
            throw;
        }
    }

    public async Task<FolderListResponse> GetFoldersAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_imagesBasePath))
        {
            return new FolderListResponse([]);
        }

        var baseDir = new DirectoryInfo(_imagesBasePath);
        var folders = baseDir.GetDirectories()
            .Select(d => d.Name)
            .OrderBy(n => n)
            .ToList();

        return new FolderListResponse(folders);
    }

    public async Task<FolderContentsResponse> GetFolderContentsAsync(string folderPath, int page = 1, int pageSize = 10, CancellationToken cancellationToken = default)
    {
        var query = _db.TournamentImageIndices
            .Where(x => x.FolderPath == folderPath)
            .OrderBy(x => x.FileName);

        var totalItems = await query.CountAsync(cancellationToken);
        var totalPages = (int)Math.Ceiling(totalItems / (double)pageSize);

        var indices = await query
            .Skip((page - 1) * pageSize)
            .Take(pageSize)
            .ToListAsync(cancellationToken);

        var images = indices.Select(x => new ImageResponse(
            Id: x.Id,
            FileName: x.FileName,
            RelativePath: x.RelativeImagePath,
            ThumbnailBase64: x.ThumbnailData != null ? Convert.ToBase64String(x.ThumbnailData) : null,
            Width: x.ImageWidth,
            Height: x.ImageHeight,
            FileSize: x.FileSize
        )).ToList();

        return new FolderContentsResponse(folderPath, images, page, pageSize, totalItems, totalPages);
    }

    private async Task IndexSingleImageAsync(FileInfo file, string folderName, CancellationToken cancellationToken)
    {
        try
        {
            var relativePath = $"{folderName}/{file.Name}";
            var fileLastModified = SystemClock.Instance.GetCurrentInstant().Minus(
                Duration.FromMilliseconds(DateTime.UtcNow.Ticks - file.LastWriteTimeUtc.Ticks)
            );

            // Check if image already exists and hasn't changed
            var existing = await _db.TournamentImageIndices
                .FirstOrDefaultAsync(x => x.FolderPath == folderName && x.FileName == file.Name, cancellationToken);

            if (existing != null && existing.FileLastModified >= fileLastModified)
            {
                return; // Image hasn't changed, skip
            }

            byte[] thumbnailData;
            int imageWidth, imageHeight;

            // Generate thumbnail with ImageSharp
            try
            {
                using (var image = await Image.LoadAsync(file.FullName, cancellationToken))
                {
                    imageWidth = image.Width;
                    imageHeight = image.Height;

                    // Calculate thumbnail height maintaining aspect ratio
                    var thumbnailHeight = (int)(ThumbnailWidth * (double)image.Height / image.Width);

                    // Resize for thumbnail
                    image.Mutate(x => x.Resize(new ResizeOptions
                    {
                        Size = new Size(ThumbnailWidth, thumbnailHeight),
                        Mode = ResizeMode.Max,
                        Sampler = KnownResamplers.Lanczos3
                    }));

                    // Save thumbnail as PNG
                    using (var ms = new MemoryStream())
                    {
                        await image.SaveAsPngAsync(ms, cancellationToken);
                        thumbnailData = ms.ToArray();
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to process image: {ImagePath}", file.FullName);
                return;
            }

            // Update or create index entry
            if (existing != null)
            {
                existing.ThumbnailData = thumbnailData;
                existing.ImageWidth = imageWidth;
                existing.ImageHeight = imageHeight;
                existing.FileSize = file.Length;
                existing.FileLastModified = fileLastModified;
                existing.IndexedAt = SystemClock.Instance.GetCurrentInstant();
                _db.TournamentImageIndices.Update(existing);
            }
            else
            {
                var newIndex = new TournamentImageIndex
                {
                    FolderPath = folderName,
                    FileName = file.Name,
                    RelativeImagePath = relativePath,
                    ThumbnailData = thumbnailData,
                    ImageWidth = imageWidth,
                    ImageHeight = imageHeight,
                    FileSize = file.Length,
                    FileLastModified = fileLastModified,
                    IndexedAt = SystemClock.Instance.GetCurrentInstant()
                };
                _db.TournamentImageIndices.Add(newIndex);
            }

            await _db.SaveChangesAsync(cancellationToken);
            _logger.LogDebug("Indexed image: {Path}", relativePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error indexing single image in folder {FolderName}", folderName);
        }
    }
}
