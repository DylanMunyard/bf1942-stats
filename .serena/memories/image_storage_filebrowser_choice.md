# Image Storage Solution: FileBrowser

## Decision
**Chosen Solution:** FileBrowser for tournament image management

## Why FileBrowser
- **Minimal overhead**: ~10MB image, lightweight resource footprint
- **Simple scope**: Upload and folder organization only - no unnecessary features
- **Easy k3s deployment**: Single container with persistent volume mount
- **Basic auth included**: Built-in authentication for access control
- **Folder-based organization**: Visual folder structure for organizing tournament images
- **HAProxy compatible**: Simple to expose behind existing k3s cluster LB

## Architecture
- Deployed as k3s container with persistent volume
- Exposed via haproxy LB (existing k3s cluster infrastructure)
- Accessible from UI app for image uploads
- Images stored in organized folder structure (by tournament/map/etc.)

## Integration Point
- UI app will interact with FileBrowser for uploading tournament organizer images (maps, etc.)
- Images served directly from FileBrowser endpoint
- No need for CDN distribution or edge caching (internal k3s cluster usage)

## Alternatives Considered
- **MinIO**: More polished, S3-compatible, but overkill for upload-only use case
- **Nextcloud**: Full-featured but too heavy for requirements
- **Lychee**: Photo gallery focus, unnecessary overhead
