# BF1942 Stats

API backend for [bfstats.io](https://bfstats.io) – Battlefield 1942 player and server statistics tracking.

## Quick Start

### Prerequisites

- .NET 8+ SDK
- Docker & Docker Compose
- kubectl (for Clickhouse access)

### Running Locally

1. **Start dev dependencies:**
   ```bash
   docker-compose -f docker-compose.dev.yml up -d
   ```

2. **Port-forward Clickhouse** (analytics database in remote k3s cluster):
   ```bash
   kubectl port-forward clickhouse-staging-598945d9f5-8f79b 8123:8123 \
     -n clickhouse-staging --context proxmox
   ```

3. **Run the API:**
   ```bash
   dotnet run
   ```

The API will be available at `http://localhost:9222`.

### Backing Up the Database

To create a ClickHouse backup:

```sql
BACKUP DATABASE default TO Disk('backups', 'back-it-up')
```

This creates a backup in the `./clickhouse-backups/` folder. The ClickHouse deployment will automatically back up these ZIP files to the Azure storage container.

### Restoring Database from Backup

1. **Create the ClickHouse data folders:**
   ```bash
   mkdir -p ./clickhouse-data
   mkdir -p ./clickhouse-backups
   ```

2. **Extract the backup ZIP:**
   ```bash
   unzip -o back-it-up.zip -d ./clickhouse-backups/
   ```

3. **Restart ClickHouse to pick up the backup:**
   ```bash
   docker-compose -f docker-compose.dev.yml restart clickhouse
   ```

4. **Restore the database:**
   ```sql
   RESTORE DATABASE default FROM Disk('backups', 'back-it-up.zip')
   ```

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
- **Databases:** SQLite (operational), Clickhouse (analytics)
- **ORM:** Entity Framework (SQLite), Clickhouse ADO.NET
- **Logging:** Seq with OTEL sinks to Loki and Tempo
- **Real-time:** SignalR

## API Documentation

Swagger docs are available at `/swagger` when running locally.

## Performance Considerations

The application is designed to run efficiently on 2 vCPU / 8GB memory. Query optimization is critical:

- Prefer multiple primary-key queries over nested `.Include()` statements
- Add logging for production troubleshooting via Seq/Loki/Tempo
- Consider database performance for all data access patterns
