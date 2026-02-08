# Railway Migration (replacing AKS + Jenkins)

## Architecture on Railway

Railway project with **4 services**:

| Railway Service   | Dockerfile                         | Port | Description                    |
|-------------------|------------------------------------|------|--------------------------------|
| `api`             | `deploy/Dockerfile.api`            | 8080 | Main API                       |
| `notifications`   | `deploy/Dockerfile.notifications`  | 8080 | SignalR hub                    |
| `redis`           | (Railway template)                 | 6379 | Caching                        |
| `redis-commander` | (optional, Railway template)       | 80   | Redis admin UI                 |

## Step-by-step Setup

### 1. Create Railway Project

```bash
# Install CLI
npm install -g @railway/cli

# Login
railway login

# Create project
railway init
```

Or create via the Railway dashboard at https://railway.com/dashboard.

### 2. Add Redis

In the Railway dashboard:
1. Click **+ New** → **Database** → **Redis**
2. This gives you a `REDIS_URL` variable automatically (e.g. `redis://default:...@...railway.internal:6379`)
3. Note the **internal hostname** (e.g. `redis.railway.internal:6379`) for connecting from other services

### 3. Create the API Service

In the Railway dashboard:
1. Click **+ New** → **GitHub Repo** → select `bf1942-stats`
2. Name the service `api`
3. Go to **Settings** → **Build**:
   - Builder: **Dockerfile**
   - Dockerfile Path: `deploy/Dockerfile.api`
4. Go to **Settings** → **Networking**:
   - Generate a Railway domain (for testing)
   - Or add custom domain `bfstats.io` later (step 6)

#### API Volume (SQLite + Assets)

1. Go to **Settings** → **Volumes**
2. Click **+ New Volume**
3. Mount path: `/mnt/data`
4. Size: start with available (Railway max is 50GB per volume on Pro plan — see [volume limits note](#volume-limits) below)

#### API Environment Variables

Set these in the **Variables** tab:

```env
# App config
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_ENVIRONMENT=Production
DB_PATH=/mnt/data/playertracker.db
ASSETS_STORAGE_PATH=/mnt/data/assets

# Redis (use Railway reference variables for auth)
REDIS_CONNECTION_STRING=${{Redis.REDISHOST}}:${{Redis.REDISPORT}},password=${{Redis.REDISPASSWORD}}

# Gamification
ENABLE_GAMIFICATION_PROCESSING=true
GAMIFICATION_MAX_CONCURRENT_ROUNDS=10
PLAYER_INSIGHTS_MAX_CONCURRENT_QUERIES=10

# JWT / Auth
Jwt__Issuer=https://bfstats.io
Jwt__Audience=https://bfstats.io
Jwt__PrivateKey=<your PEM private key>

# Refresh Token
RefreshToken__CookieName=rt
RefreshToken__CookieDomain=bfstats.io
RefreshToken__CookiePath=/stats
RefreshToken__Days=60
RefreshToken__Secret=<your refresh token secret>

# Discord OAuth
DiscordOAuth__ClientId=<your discord client id>
DiscordOAuth__ClientSecret=<your discord client secret>

# CORS
Cors__AllowedOrigins=https://bfstats.io

# Bot Detection
BotDetection__DefaultPlayerNames__0=BFPlayer
BotDetection__DefaultPlayerNames__1=Player
BotDetection__DefaultPlayerNames__2=BFSoldier

# Server Filtering
ServerFiltering__StuckServers__0=Tragic! [USA] - Dallas

# Discord Webhooks (optional)
DiscordSuspicious__RoundWebhookUrl=<webhook url>
DiscordSuspicious__ScoreThreshold=150
DiscordAIQuality__WebhookUrl=<webhook url>

# Azure OpenAI
AzureOpenAI__Endpoint=https://dylan-ml3fg2xi-eastus.cognitiveservices.azure.com/
AzureOpenAI__DeploymentName=gpt-4o-mini
AzureOpenAI__MaxTokens=4096
AzureOpenAI__Temperature=0.7
AzureOpenAI__ApiKey=<your azure openai key>

# Observability (optional — Railway has built-in logging)
# APPLICATIONINSIGHTS_CONNECTION_STRING=<if you want to keep App Insights>
# APPLICATIONINSIGHTS_SAMPLING_RATIO=0.5
```

### 4. Create the Notifications Service

1. Click **+ New** → **GitHub Repo** → select `bf1942-stats` (same repo)
2. Name the service `notifications`
3. Go to **Settings** → **Build**:
   - Builder: **Dockerfile**
   - Dockerfile Path: `deploy/Dockerfile.notifications`
4. Watch Paths: set to `notifications/**` to avoid rebuilds when only API code changes

#### Notifications Environment Variables

```env
ASPNETCORE_URLS=http://+:8080
ASPNETCORE_ENVIRONMENT=Production

# Redis (use Railway reference variables for auth)
ConnectionStrings__Redis=${{Redis.REDISHOST}}:${{Redis.REDISPORT}},password=${{Redis.REDISPASSWORD}}

# Internal API URL (Railway private networking)
ApiBaseUrl=http://api.railway.internal:8080

# JWT (same keys as API)
Jwt__Issuer=https://bfstats.io
Jwt__Audience=https://bfstats.io
Jwt__PrivateKey=<same PEM private key as API>
```

### 5. Configure Healthchecks

For both services, go to **Settings** → **Deploy**:
- Healthcheck Path: `/health`
- Healthcheck Timeout: `300` seconds
- Restart Policy: **On Failure**

### 6. Custom Domain + Routing

Railway assigns each service its own domain. To replicate the AKS ingress routing (`/stats` → API, `/hub` → notifications):

**Option A: Single domain with path-based routing (recommended)**
- Point `bfstats.io` to the **API** service as the primary custom domain
- The API already serves `/stats`, `/health`, `/swagger`
- For `/hub` (notifications), you'll need a subdomain approach since Railway doesn't support path-based routing across services

**Option B: Subdomain split**
- `bfstats.io` → API service (serves `/stats`, `/health`, `/swagger`)
- `ws.bfstats.io` → Notifications service (serves `/hub`)
- Update the frontend to connect SignalR to `ws.bfstats.io/hub`

**Option C: Railway reverse proxy (if supported)**
- Check if Railway's networking supports path-based routing to different services
- As of 2025, this is not natively supported — use Option B

**Setting up custom domains:**
1. Go to the service → **Settings** → **Networking** → **Custom Domain**
2. Add `bfstats.io` (API) and `ws.bfstats.io` (notifications)
3. Configure DNS CNAME records as Railway instructs
4. Railway handles TLS automatically (replaces your cert-manager + Let's Encrypt setup)

### 7. Watch Paths (prevent unnecessary rebuilds)

For the **API** service, in Settings → Build → Watch Paths:
```
api/**
```

For the **notifications** service:
```
notifications/**
```

This way changes to the API don't trigger a notifications rebuild and vice versa.

> **Note:** Since `notifications` has a `ProjectReference` to `api`, you may also want to include `api/**` in the notifications watch paths so it rebuilds when shared code changes.

## Volume Limits

Railway Pro plan volumes max out at **50GB per volume**. Your current AKS PVC is 128GB. Options:

1. **Check actual usage** — you may be well under 50GB
2. **Offload assets to object storage** — move `/mnt/data/assets` to Cloudflare R2, S3, or similar. Keep only the SQLite DB on the volume.
3. **Contact Railway** — Enterprise plans may offer larger volumes

## Migration Checklist

- [ ] Create Railway project
- [ ] Add Redis service
- [ ] Create API service with `deploy/Dockerfile.api`
- [ ] Attach volume at `/mnt/data` to API service
- [ ] Copy SQLite database to the volume (see [Database Migration](#database-migration))
- [ ] Copy assets to the volume (or migrate to object storage)
- [ ] Set all API environment variables
- [ ] Create notifications service with `deploy/Dockerfile.notifications`
- [ ] Set all notifications environment variables
- [ ] Configure healthchecks on both services
- [ ] Set up custom domain(s) and DNS
- [ ] Update frontend to point to new API / WebSocket URLs
- [ ] Update Discord OAuth redirect URLs to new domain
- [ ] Verify end-to-end functionality
- [ ] Decommission AKS cluster + Jenkins pipeline

## Database Migration

To copy your SQLite database from AKS to Railway:

```bash
# 1. Download from AKS
kubectl cp bf42-stats/bf42-stats-0:/mnt/data/playertracker.db ./playertracker.db -c nginx

# 2. Upload to Railway volume via railway CLI or a temporary upload endpoint
#    Option A: Use railway shell
railway shell -s api
#    Then from inside the container, use curl/wget to fetch the DB
#    from a temporary upload location (e.g. presigned S3 URL)

#    Option B: Use rsync via railway's volume mount
#    Railway doesn't support direct file copy to volumes yet.
#    The common approach is to create a temporary seed endpoint in your app
#    or use a database initialization script that downloads from a URL.
```

## Cost Comparison

| Resource       | AKS (current)                | Railway (estimated)           |
|----------------|------------------------------|-------------------------------|
| Compute        | AKS node pool + management   | Pay per usage (vCPU + memory) |
| Storage        | 128Gi managed premium PVC    | Volume (included in Pro)      |
| Redis          | Self-managed pod             | Railway Redis template        |
| TLS/Certs      | cert-manager + Let's Encrypt | Included automatically        |
| CI/CD          | Jenkins + Docker Hub         | Built-in (deploy on push)     |
| Load Balancer  | Azure LB + NGINX ingress     | Included                      |
| Observability  | App Insights ($)             | Built-in logs (free)          |

## What You're Dropping

- Jenkins pipeline (replaced by Railway's GitHub-triggered deploys)
- Docker Hub image registry (Railway builds images internally)
- AKS cluster + node pools
- NGINX ingress controller
- cert-manager
- Azure service principal for CI/CD
- K8s manifests (deployment.yaml, service.yaml, ingress.yaml, etc.)
- sqlite-tools sidecar, sqlite-browser, redis-commander deployments (optional to recreate)
