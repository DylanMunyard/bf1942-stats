# Observability Stack Setup

This project uses Grafana Loki for logging and Tempo for distributed tracing, replacing the previous Seq setup.

## Local Development Setup

### Prerequisites
- Docker and Docker Compose
- .NET 8 SDK

### Starting the Observability Stack

1. Start the observability services:
```bash
docker-compose -f docker-compose.dev.yml up -d
```

This will start:
- **Redis** on port 6379 (for caching and event publishing)
- **Loki** on port 3100 (for log aggregation)
- **Tempo** on port 3200 (for distributed tracing)
- **Grafana** on port 3000 (for visualization)

2. Access Grafana at http://localhost:3000
   - Username: `admin`
   - Password: `admin`

### Running the Applications

The applications are already configured to use the local observability stack through the updated `launchSettings.json` files.

1. Start the main application:
```bash
cd junie-des-1942stats
dotnet run
```

2. Start the notifications service:
```bash
cd junie-des-1942stats.Notifications
dotnet run
```

### Environment Variables

The following environment variables are configured in `launchSettings.json`:

- `LOKI_URL`: URL for Loki log aggregation (default: http://localhost:3100)
- `TEMPO_URL`: URL for Tempo tracing (default: http://localhost:3200)
- `OTLP_ENDPOINT`: OTLP endpoint for traces (default: http://localhost:4318/v1/traces)
- `REDIS_CONNECTION_STRING`: Redis connection string (default: localhost:6379)

### Viewing Logs and Traces

1. **Logs**: In Grafana, go to Explore → Select Loki datasource → Query logs by service:
   ```
   {service="junie-des-1942stats"}
   {service="junie-des-1942stats.Notifications"}
   ```

2. **Traces**: In Grafana, go to Explore → Select Tempo datasource → Search for traces

3. **Correlation**: Logs and traces are automatically correlated through service names and trace IDs

### Stopping the Stack

```bash
docker-compose -f docker-compose.dev.yml down
```

To remove volumes as well:
```bash
docker-compose -f docker-compose.dev.yml down -v
```

## Production Deployment

For production, you'll need to:

1. Update the environment variables to point to your production Loki and Tempo instances
2. Configure proper authentication and TLS
3. Set up retention policies for logs and traces
4. Configure alerting rules in Grafana

## Migration from Seq

The applications have been updated to use:
- **Serilog.Sinks.Grafana.Loki** instead of Serilog.Sinks.Seq for logging
- **OpenTelemetry OTLP exporter** pointing to Tempo instead of Seq for tracing

All existing log levels, filters, and enrichers remain the same.
