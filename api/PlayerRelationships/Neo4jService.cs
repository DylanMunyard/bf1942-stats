using Microsoft.Extensions.Logging;
using Neo4j.Driver;

namespace api.PlayerRelationships;

/// <summary>
/// Low-level Neo4j driver service for managing connections and executing queries.
/// Implements IAsyncDisposable for proper cleanup.
/// </summary>
public class Neo4jService : IAsyncDisposable
{
    private readonly IDriver _driver;
    private readonly string _database;
    private readonly ILogger<Neo4jService> _logger;

    /// <summary>
    /// Get the underlying Neo4j driver instance.
    /// Useful for services that need direct driver access.
    /// </summary>
    public IDriver Driver => _driver;

    public Neo4jService(Neo4jConfiguration config, ILogger<Neo4jService> logger)
    {
        _logger = logger;
        _database = config.Database;
        
        _driver = GraphDatabase.Driver(
            config.Uri,
            AuthTokens.Basic(config.Username, config.Password),
            o => o.WithMaxConnectionPoolSize(50)
                  .WithConnectionTimeout(TimeSpan.FromSeconds(15))
        );
        
        _logger.LogInformation(
            "Neo4j driver initialized: {Uri}, Database: {Database}",
            config.Uri,
            config.Database);
    }

    /// <summary>
    /// Execute a write transaction (CREATE, MERGE, SET, DELETE).
    /// </summary>
    public async Task<T> ExecuteWriteAsync<T>(Func<IAsyncQueryRunner, Task<T>> work)
    {
        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));
        return await session.ExecuteWriteAsync(work);
    }

    /// <summary>
    /// Execute a read transaction (MATCH, RETURN).
    /// </summary>
    public async Task<T> ExecuteReadAsync<T>(Func<IAsyncQueryRunner, Task<T>> work)
    {
        await using var session = _driver.AsyncSession(o => o.WithDatabase(_database));
        return await session.ExecuteReadAsync(work);
    }

    /// <summary>
    /// Verify connectivity to Neo4j.
    /// </summary>
    public async Task<bool> VerifyConnectivityAsync()
    {
        try
        {
            await _driver.VerifyConnectivityAsync();
            _logger.LogInformation("Neo4j connectivity verified");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to verify Neo4j connectivity");
            return false;
        }
    }

    public async ValueTask DisposeAsync()
    {
        await _driver.DisposeAsync();
        GC.SuppressFinalize(this);
    }
}
