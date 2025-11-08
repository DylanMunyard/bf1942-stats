# Local Development: Tournament Map Image Upload

## Setup

When running locally with `docker-compose.dev.yml`, FileBrowser is now included for managing tournament map images.

### Start the development environment:

```bash
docker-compose -f docker-compose.dev.yml up -d
```

This will start:
- **FileBrowser** at `http://localhost:8081`
  - Default credentials: `admin` / `admin` (first login)
  - Provides a web UI for uploading and organizing tournament images

### Directory Structure

FileBrowser mounts to `/data/tournament-images` (persistent Docker volume). The API expects images to be organized as:

```
tournament-images/
├── golden-gun/
│   ├── map1.png
│   ├── map2.png
│   └── ...
├── vanilla/
│   ├── map1.png
│   ├── ctf_dustbowl.png
│   └── ...
└── other-tournament/
    └── ...
```

## Local Development Workflow

### 1. Upload Images via FileBrowser

1. Open `http://localhost:8081`
2. Create folders for each tournament (e.g., "golden-gun", "vanilla")
3. Upload map images to the appropriate folders
4. Images should be: `*.png`, `*.jpg`, `*.jpeg`, `*.gif`, or `*.webp`

### 2. Index Images via API

Once images are uploaded, trigger the indexing service (requires auth token):

**POST** `http://localhost:9222/stats/admin/images/scan`

This will:
- Scan the tournament-images directory
- Generate thumbnails for each image (300px width)
- Cache metadata in the SQLite database
- Return statistics on newly indexed images

### 3. Browse Available Images

Browse images via API (requires auth token):

**GET** `http://localhost:9222/stats/admin/images/folders`

Returns list of available tournament folders:
```json
{
  "folders": ["bf42-liga", "vanilla", "other-tournament"]
}
```

**GET** `http://localhost:9222/stats/admin/images/folders/{folderPath}`

Returns images in a folder with thumbnails:
```json
{
  "folder": "golden-gun",
  "images": [
    {
      "id": 1,
      "fileName": "map1.png",
      "relativePath": "golden-gun/map1.png",
      "thumbnail": "data:image/png;base64,...",
      "width": 1024,
      "height": 768,
      "fileSize": 245632
    }
  ]
}
```

### 4. Select Image for Tournament Map

When editing a tournament match map, you can now set the image:

**PUT** `http://localhost:9222/stats/admin/tournaments/{id}/matches/{matchId}/maps/{mapId}`

```json
{
  "mapId": 5,
  "mapName": "Graveyard",
  "teamId": null,
  "imagePath": "golden-gun/map1.png"
}
```

### 5. View Images Publicly

Once set, the image is served publicly at:

```
http://localhost:9222/images/tournament-maps/bf42-liga/tl_cambrai.jpg
```

## Important Notes

- **Image indexing is throttled**: Limited to 3 concurrent images with delays to avoid CPU spikes (by design)
- **Thumbnails are cached**: Subsequent requests for folder contents return cached thumbnails from the database
- **Loose coupling**: Images are not strictly linked to tournament IDs - you organize them however you want
- **FileBrowser auth**: Default credentials should be changed in production. For local dev, use any credentials on first login.

## Running API Locally (dotnet run)

FileBrowser stores images in `./tournament-images` at the project root (bind mount), so the API can access it directly:

```bash
# Terminal 1: Start all dev dependencies (including FileBrowser)
# Run from project root
docker-compose -f docker-compose.dev.yml up

# Terminal 2: Run API locally (from api folder, as you normally do in IDE)
ASSETS_STORAGE_PATH=../tournament-images dotnet run
```

That's it! The `tournament-images` directory is:
- Located at the project root: `/path/to/bf1942-stats/tournament-images/`
- Shared between FileBrowser (in Docker) and your API (running locally)
- Visible in your file explorer
- Persistent across restarts

## Workflow

1. Start docker-compose from project root: `docker-compose -f docker-compose.dev.yml up`
2. In IDE terminal (api folder): `ASSETS_STORAGE_PATH=../tournament-images dotnet run`
3. Upload via FileBrowser: `http://localhost:8081`
   - Create folders: `golden-gun/`, `vanilla/`, etc.
   - Upload map images
4. Index images: `POST http://localhost:5000/api/admin/images/scan`
5. Browse and select in tournament maps

You can also see the files directly in the `tournament-images/` folder at the project root.

## Cleanup

To stop and clean up the dev environment:

```bash
docker-compose -f docker-compose.dev.yml down
```

To also remove the tournament images volume:

```bash
docker-compose -f docker-compose.dev.yml down -v
```

## Troubleshooting

**Permission denied when creating folders in FileBrowser?**

If you see: `mkdir /data/tournament-images/tournaments: permission denied`

This means the Docker volume doesn't have write permissions. Fix it:

```bash
# Stop and remove the old container
docker-compose -f docker-compose.dev.yml down filebrowser

# Remove the volume to reset it
docker volume rm bf1942-stats_tournament-images

# Restart FileBrowser - it will reinitialize with correct permissions
docker-compose -f docker-compose.dev.yml up -d filebrowser
```

The updated docker-compose now:
- Runs FileBrowser as root to ensure write permissions
- Automatically chmod 777 the directory on startup
- Allows full read/write access for development

**Images not showing up after upload?**
- Ensure images are in the correct folder structure
- Call the `/api/admin/images/scan` endpoint to re-index
- Check FileBrowser that the files were actually uploaded

**Getting 404 when accessing image?**
- Verify the image path format: `folder/filename.ext`
- Make sure the file exists in FileBrowser
- Ensure the image has been indexed via the scan endpoint

**API not serving static files?**
- The API requires the `/data/tournament-images` directory to exist
- With docker-compose, this is created automatically
- If running `dotnet run` locally, ensure the directory exists
