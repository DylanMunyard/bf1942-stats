# Code Style and Conventions

## C# Code Style
- **Naming**: PascalCase for classes, methods, properties; camelCase for parameters, local variables
- **Namespace**: `junie_des_1942stats.*` with underscore separation
- **Nullable**: Enabled project-wide (`<Nullable>enable</Nullable>`)
- **Implicit usings**: Enabled (`<ImplicitUsings>enable</ImplicitUsings>`)

## Project Structure
- **Controllers/**: API endpoints
- **Services/**: Business logic services
- **Models/**: Data transfer objects and domain models
- **ClickHouse/**: ClickHouse-specific services and interfaces
- **PlayerStats/**: Player-related functionality
- **ServerStats/**: Server-related functionality
- **Gamification/**: Achievement and badge systems
- **StatsCollectors/**: Background collection services

## Patterns
- **Dependency Injection**: Heavy use of DI container
- **Async/Await**: All I/O operations are async
- **Logging**: Structured logging with Serilog
- **Caching**: Redis-based caching with cache keys service
- **Error Handling**: Try-catch with proper logging and HTTP status codes

## Database Conventions
- **SQLite**: Entity Framework Core migrations
- **ClickHouse**: Raw SQL queries with parameterization
- **Connection Strings**: Environment variable based configuration