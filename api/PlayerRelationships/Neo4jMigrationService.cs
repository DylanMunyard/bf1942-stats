using System.Reflection;
using System.Text.RegularExpressions;
using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace api.PlayerRelationships;

/// <summary>
/// Manages Neo4j schema migrations similar to EF Core migrations.
/// Tracks applied migrations in a :Migration node and runs pending migrations on startup.
/// </summary>
public class Neo4jMigrationService(
    Neo4jService neo4jService,
    ILogger<Neo4jMigrationService> logger)
{
    private const string MigrationsPath = "api.PlayerRelationships.Migrations";

    /// <summary>
    /// Run all pending migrations. Should be called on application startup.
    /// </summary>
    public async Task MigrateAsync(CancellationToken cancellationToken = default)
    {
        logger.LogInformation("Checking for pending Neo4j migrations...");

        try
        {
            // Ensure migration tracking node exists
            await EnsureMigrationTrackingAsync();

            // Get applied migrations
            var appliedMigrations = await GetAppliedMigrationsAsync();
            logger.LogInformation("Found {Count} previously applied migrations", appliedMigrations.Count);

            // Get all migration files
            var allMigrations = GetAllMigrationFiles();
            logger.LogInformation("Found {Count} total migration files", allMigrations.Count);

            // Find pending migrations
            var pendingMigrations = allMigrations
                .Where(m => !appliedMigrations.Contains(m.Name))
                .OrderBy(m => m.Name)
                .ToList();

            if (pendingMigrations.Count == 0)
            {
                logger.LogInformation("No pending migrations. Schema is up to date.");
                return;
            }

            logger.LogInformation("Found {Count} pending migrations. Applying...", pendingMigrations.Count);

            // Apply each migration
            foreach (var migration in pendingMigrations)
            {
                await ApplyMigrationAsync(migration, cancellationToken);
            }

            logger.LogInformation("All migrations applied successfully");
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply Neo4j migrations");
            throw;
        }
    }

    /// <summary>
    /// Ensure the migration tracking node exists.
    /// Stores metadata about applied migrations.
    /// </summary>
    private async Task EnsureMigrationTrackingAsync()
    {
        await neo4jService.ExecuteWriteAsync(async tx =>
        {
            var query = @"
                MERGE (tracker:MigrationTracker {id: 'singleton'})
                ON CREATE SET tracker.createdAt = datetime(),
                              tracker.migrations = []
                RETURN tracker";

            await tx.RunAsync(query);
            return true;
        });
    }

    /// <summary>
    /// Get list of already applied migration names.
    /// </summary>
    private async Task<HashSet<string>> GetAppliedMigrationsAsync()
    {
        return await neo4jService.ExecuteReadAsync(async tx =>
        {
            var query = @"
                MATCH (tracker:MigrationTracker {id: 'singleton'})
                RETURN tracker.migrations AS migrations";

            var cursor = await tx.RunAsync(query);
            var record = await cursor.SingleOrDefaultAsync();

            if (record == null)
                return new HashSet<string>();

            var migrations = record["migrations"].As<List<string>>();
            return new HashSet<string>(migrations);
        });
    }

    /// <summary>
    /// Get all migration files from embedded resources.
    /// </summary>
    private List<MigrationFile> GetAllMigrationFiles()
    {
        var assembly = Assembly.GetExecutingAssembly();
        var migrationFiles = new List<MigrationFile>();

        // Get all embedded .cypher files in Migrations folder
        var resourceNames = assembly.GetManifestResourceNames()
            .Where(name => name.StartsWith(MigrationsPath) && name.EndsWith(".cypher"))
            .OrderBy(name => name);

        foreach (var resourceName in resourceNames)
        {
            // Extract filename from resource name
            // e.g., "api.PlayerRelationships.Migrations.001_InitialSchema.cypher" -> "001_InitialSchema.cypher"
            var fileName = resourceName.Substring(MigrationsPath.Length + 1);

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream == null)
            {
                logger.LogWarning("Failed to load migration resource: {ResourceName}", resourceName);
                continue;
            }

            using var reader = new StreamReader(stream);
            var content = reader.ReadToEnd();

            migrationFiles.Add(new MigrationFile
            {
                Name = fileName,
                Content = content
            });
        }

        return migrationFiles;
    }

    /// <summary>
    /// Apply a single migration and track it.
    /// </summary>
    private async Task ApplyMigrationAsync(MigrationFile migration, CancellationToken cancellationToken)
    {
        logger.LogInformation("Applying migration: {MigrationName}", migration.Name);

        var startTime = DateTime.UtcNow;

        try
        {
            // Parse and execute migration statements
            var statements = ParseCypherStatements(migration.Content);
            logger.LogDebug("Migration {Name} contains {Count} statements", migration.Name, statements.Count);

            foreach (var statement in statements)
            {
                await neo4jService.ExecuteWriteAsync(async tx =>
                {
                    await tx.RunAsync(statement);
                    return true;
                });
            }

            // Track the migration
            await neo4jService.ExecuteWriteAsync(async tx =>
            {
                var query = @"
                    MATCH (tracker:MigrationTracker {id: 'singleton'})
                    SET tracker.migrations = tracker.migrations + $migrationName,
                        tracker.lastMigration = $migrationName,
                        tracker.lastMigrationAt = datetime()
                    RETURN tracker";

                await tx.RunAsync(query, new { migrationName = migration.Name });
                return true;
            });

            var duration = DateTime.UtcNow - startTime;
            logger.LogInformation(
                "Successfully applied migration {Name} in {Duration}ms",
                migration.Name,
                duration.TotalMilliseconds);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Failed to apply migration: {MigrationName}", migration.Name);
            throw new InvalidOperationException($"Migration {migration.Name} failed: {ex.Message}", ex);
        }
    }

    /// <summary>
    /// Parse Cypher file into individual statements.
    /// Splits on semicolons, ignoring comments.
    /// </summary>
    private List<string> ParseCypherStatements(string content)
    {
        var statements = new List<string>();
        var lines = content.Split('\n');
        var currentStatement = new List<string>();

        foreach (var line in lines)
        {
            var trimmed = line.Trim();

            // Skip empty lines and comments
            if (string.IsNullOrWhiteSpace(trimmed) || trimmed.StartsWith("//"))
                continue;

            // Remove inline comments
            var commentIndex = trimmed.IndexOf("//");
            if (commentIndex >= 0)
                trimmed = trimmed.Substring(0, commentIndex).Trim();

            currentStatement.Add(trimmed);

            // If line ends with semicolon, we have a complete statement
            if (trimmed.EndsWith(";"))
            {
                var statement = string.Join("\n", currentStatement).Trim(';', ' ', '\n', '\r');
                if (!string.IsNullOrWhiteSpace(statement))
                {
                    statements.Add(statement);
                }
                currentStatement.Clear();
            }
        }

        // Handle last statement if it doesn't end with semicolon
        if (currentStatement.Count > 0)
        {
            var statement = string.Join("\n", currentStatement).Trim(';', ' ', '\n', '\r');
            if (!string.IsNullOrWhiteSpace(statement))
            {
                statements.Add(statement);
            }
        }

        return statements;
    }

    /// <summary>
    /// Get migration status (for admin endpoint).
    /// </summary>
    public async Task<MigrationStatus> GetStatusAsync()
    {
        var appliedMigrations = await GetAppliedMigrationsAsync();
        var allMigrations = GetAllMigrationFiles();

        var pendingMigrations = allMigrations
            .Where(m => !appliedMigrations.Contains(m.Name))
            .Select(m => m.Name)
            .OrderBy(n => n)
            .ToList();

        return new MigrationStatus
        {
            AppliedCount = appliedMigrations.Count,
            PendingCount = pendingMigrations.Count,
            AppliedMigrations = appliedMigrations.OrderBy(n => n).ToList(),
            PendingMigrations = pendingMigrations
        };
    }

    private class MigrationFile
    {
        public required string Name { get; init; }
        public required string Content { get; init; }
    }
}

public class MigrationStatus
{
    public int AppliedCount { get; set; }
    public int PendingCount { get; set; }
    public List<string> AppliedMigrations { get; set; } = [];
    public List<string> PendingMigrations { get; set; } = [];
    public bool IsUpToDate => PendingCount == 0;
}
