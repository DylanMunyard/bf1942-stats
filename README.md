# BF1942 Stats

API backend for [bfstats.io](https://bfstats.io) – Battlefield 1942 player and server statistics tracking.

## Quick Start

### Prerequisites

- .NET 8+ SDK
- Docker & Docker Compose

### Running Locally

1. **Start dev dependencies:**
   ```bash
   docker-compose -f docker-compose.dev.yml up -d
   ```

2. **Run the API:**
   ```bash
   dotnet run
   ```

The API will be available at `http://localhost:9222`.

### Backing Up the Database

The SQLite database file lives at `./api/playertracker.db` in local dev (or `DB_PATH` in production).

To back up locally:
1. Stop the API (or ensure no writes are happening).
2. Copy `playertracker.db` and any `playertracker.db-wal` / `playertracker.db-shm` files alongside it.

### Restoring Database from Backup

1. Stop the API.
2. Replace `playertracker.db` (and optional `-wal` / `-shm`) with your backup copy.
3. Restart the API.

## Configuration

Local development requires these environment variables or user secrets:

```bash
# JWT signing key (required)
export Jwt__PrivateKeyPath=/path/to/jwt-private.pem
export Jwt__Issuer=https://localhost:5001
export Jwt__Audience=http://localhost:5173

# Refresh token secret (required)
export RefreshToken__Secret=<base64-encoded-secret>
```

Or use `dotnet user-secrets`:

```bash
dotnet user-secrets set "Jwt:PrivateKeyPath" "/path/to/jwt-private.pem"
dotnet user-secrets set "Jwt:Issuer" "https://localhost:5001"
dotnet user-secrets set "Jwt:Audience" "http://localhost:5173"
dotnet user-secrets set "RefreshToken:Secret" "<base64-encoded-secret>"
```

See [DEPLOYMENT.md](./DEPLOYMENT.md) for key generation instructions.

## Projects

- `junie-dest-1942stats` – Main API
- `junie-des-1942stats.Notifications` – SignalR hub for real-time events

## Tech Stack

- **Framework:** ASP.NET Core
- **Databases:** SQLite
- **ORM:** Entity Framework Core
- **Logging:** Seq with OTEL sinks to Loki and Tempo
- **Real-time:** SignalR

## API Documentation

Swagger docs are available at `/swagger` when running locally.

## Performance Considerations

The application is designed to run efficiently on 2 vCPU / 8GB memory. Query optimization is critical:

- Prefer multiple primary-key queries over nested `.Include()` statements
- Add logging for production troubleshooting via Seq/Loki/Tempo
- Consider database performance for all data access patterns
