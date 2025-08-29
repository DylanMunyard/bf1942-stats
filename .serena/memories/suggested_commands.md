# Suggested Development Commands

## Build and Run
```bash
# Restore packages
dotnet restore

# Build the project
dotnet build

# Run the application
dotnet run --project junie-des-1942stats/junie-des-1942stats.csproj

# Format code
dotnet format

# Check for newer package versions
dotnet list package --outdated
```

## Database Operations
```bash
# Add new EF Core migration
dotnet ef migrations add <MigrationName> --project junie-des-1942stats

# Apply migrations
dotnet ef database update --project junie-des-1942stats

# Generate SQL script
dotnet ef migrations script --project junie-des-1942stats
```

## Development Tools
```bash
# User secrets (for local dev)
dotnet user-secrets set "Jwt:PrivateKeyPath" "/path/to/jwt-private.pem" --project junie-des-1942stats
dotnet user-secrets list --project junie-des-1942stats

# Generate JWT keys
openssl genrsa -out jwt-private.pem 2048
openssl rsa -in jwt-private.pem -pubout -out jwt-public.pem
```

## Git Commands
Standard git operations are used for version control.

## System Utilities
Standard Linux commands: ls, cd, grep, find, curl, etc.