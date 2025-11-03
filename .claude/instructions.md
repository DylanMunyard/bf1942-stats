# Claude Instructions for bf1942-stats

## Project Overview

This is the API backend for bfstats.io. It provides an API layer interfacing with both analytics (Clickhouse) and operational (SQLite) databases.

## Tech Stack & Dependencies

- **Framework**: ASP.NET Core
- **Data Access**: Entity Framework (SQLite), Clickhouse ADO.NET
- **Logging**: Seq with OTEL sinks to Loki and Tempo
- **Real-time**: SignalR (junie-des-1942stats.Notifications project)

## Running the Project

```bash
# Start dev dependencies
docker-compose -f docker-compose.dev.yml up -d

# Port-forward remote Clickhouse (in k3s cluster)
kubectl port-forward clickhouse-staging-598945d9f5-8f79b 8123:8123 -n clickhouse-staging --context proxmox

# Run API
dotnet run  # Runs on localhost:9222
```

## Key Projects

- `junie-dest-1942stats` - Main API
- `junie-des-1942stats.Notifications` - SignalR hub for real-time events

## Critical Constraints

- **Performance-First**: Deployed on 2 vCPU/8G. Assume senior C# engineer optimizing for ultra-fast response times.
- **Query Strategy**: Prefer multiple primary-key queries over nested `.Include()` statements in LINQ. Always consider database performance.
- **Logging**: Add logging statements wherever they'd aid production investigation via logs and traces. Assume observability via Seq/Loki/Tempo.
