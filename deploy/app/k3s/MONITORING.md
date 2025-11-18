# Monitoring Setup

## ClickHouse Plugin Installation

1. Install the Grafana ClickHouse plugin (in Grafana UI)

### ClickHouse Datasource Configuration

In Grafana add a ClickHouse datasource:

- **Server:** `clickhouse-service.clickhouse` (no `http://` prefix)
- **Port:** `9000`
- **Protocol:** Native
- **User:** `default`
- **Password:** (leave empty)

**Important:** Do not include `http://` when using the native protocol (port 9000). The native protocol uses raw TCP.

Alternatively, for HTTP protocol:
- **Server:** `http://clickhouse-aks-external.monitoring`
- **Port:** `8123`
- **Protocol:** HTTP

## ASP.NET Core Monitoring

Import the ASP.NET Core dashboard:
- Dashboard ID: **19924**
- URL: https://grafana.com/grafana/dashboards/19924-asp-net-core/
