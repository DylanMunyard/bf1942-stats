# BF1942 Stats Project Overview

## Purpose
Battlefield 1942 player and server statistics tracking application. Collects, processes, and serves real-time and historical statistics for BF1942 game servers and players.

## Tech Stack
- **Backend**: ASP.NET Core 8.0 (C#)
- **Database**: SQLite
- **Caching**: Redis
- **Monitoring**: OpenTelemetry, Prometheus, Seq logging
- **Authentication**: JWT (RS256) + OAuth (Google)
- **Hosting**: Docker/Kubernetes ready

## Key Components
- **PlayerTracking**: SQLite-based player/server tracking
- **ServerStats**: Server analytics and insights API
- **StatsCollectors**: Background services for data collection
- **Gamification**: Achievement/badge system

## Architecture
- Microservice-style with background collectors
- Redis for caching and event publishing
- OpenTelemetry for distributed tracing
- RESTful APIs with Swagger documentation