# BF1942 Stats Project Overview

## Purpose
Battlefield 1942 player and server statistics tracking application. Collects, processes, and serves real-time and historical statistics for BF1942 game servers and players.

## Tech Stack
- **Backend**: ASP.NET Core 8.0 (C#)
- **Database**: SQLite (player tracking) + ClickHouse (analytics/metrics)
- **Caching**: Redis
- **Monitoring**: OpenTelemetry, Prometheus, Seq logging
- **Authentication**: JWT (RS256) + OAuth (Google)
- **Hosting**: Docker/Kubernetes ready

## Key Components
- **PlayerTracking**: SQLite-based player/server tracking
- **ClickHouse**: Time-series analytics (player_metrics, player_rounds, server_online_counts)
- **ServerStats**: Server analytics and insights API
- **StatsCollectors**: Background services for data collection
- **Gamification**: Achievement/badge system
- **Real-time Analytics**: Live server monitoring

## Architecture
- Microservice-style with background collectors
- Separate read/write ClickHouse URLs for scalability
- Redis for caching and event publishing
- OpenTelemetry for distributed tracing
- RESTful APIs with Swagger documentation