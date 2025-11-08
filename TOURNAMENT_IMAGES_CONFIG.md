# Tournament Images Configuration

The tournament images path is configurable via the `TOURNAMENT_IMAGES_PATH` environment variable.

## Quick Start - Development

```bash
# Terminal 1: Start all dev dependencies (FileBrowser + others)
docker-compose -f docker-compose.dev.yml up

# Terminal 2: Run API with images directory
TOURNAMENT_IMAGES_PATH=./tournament-images dotnet run
```

Both FileBrowser (in Docker) and your API (local) share the `./tournament-images` directory on your host machine via bind mount.

## Configuration by Environment

### Development (Recommended)
```bash
TOURNAMENT_IMAGES_PATH=./tournament-images dotnet run
```
- Directory: `./tournament-images/` on host
- Accessible to: FileBrowser (Docker) + API (local)
- Persistence: Files stay in git-ignored directory

### Production (Kubernetes/AKS)
```yaml
env:
  - name: TOURNAMENT_IMAGES_PATH
    value: "/data/tournament-images"
```
Uses PVC mounted at `/data/tournament-images`

## Environment Variable Details

| Variable | Default | Description |
|----------|---------|-------------|
| `TOURNAMENT_IMAGES_PATH` | `/data/tournament-images` | Full path to tournament images directory |

## Path Requirements

1. **Must exist** - The API logs a warning if the path doesn't exist, but won't crash
2. **Read-write access** - For the image indexing service to work (generates thumbnails)
3. **Persistent** - Use volumes/PVCs in production to persist images across restarts

## Behavior When Path Missing

If `TOURNAMENT_IMAGES_PATH` points to a non-existent directory:

```
WARNING: Tournament images directory not found at {ImagePath}. Static file serving disabled.
```

This means:
- `GET /images/tournament-maps/*` endpoints will return 404
- `GET /api/admin/images/*` endpoints will work but find no images
- Call `POST /api/admin/images/scan` after creating/mounting the directory to enable

## Best Practices

1. **Always set explicitly in production** - Don't rely on defaults
2. **Use PVCs for persistence** - Kubernetes deployments should mount PVCs
3. **Document your paths** - Add comments to deployment files showing the path logic
4. **Create directory beforehand** - Ensure the directory exists before starting the app

## Example: Multiple Environments

```bash
# Development
export TOURNAMENT_IMAGES_PATH=./tournament-images
dotnet run

# Staging
export TOURNAMENT_IMAGES_PATH=/mnt/images-staging
dotnet run

# Production (K8s)
# Set in deployment.yaml env section
value: "/data/tournament-images"
```

## Monitoring

Check logs for image path configuration:

```bash
# Docker logs
docker logs bf1942-stats | grep "Tournament images"

# K8s logs
kubectl logs -n bf42-stats -l app=bf42-stats | grep "Tournament images"
```

You should see one of:
- ✅ `Tournament images serving enabled at {path}`
- ⚠️ `Tournament images directory not found at {path}. Static file serving disabled.`
