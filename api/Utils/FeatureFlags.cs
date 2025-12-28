using Microsoft.Extensions.Configuration;

namespace api.Utils;

/// <summary>
/// Represents the data source for analytics queries.
/// Used to toggle between ClickHouse and SQLite during migration.
/// </summary>
public enum QuerySource
{
    ClickHouse,
    SQLite
}

/// <summary>
/// Selects query source (ClickHouse or SQLite) per endpoint.
/// Enables gradual migration with per-endpoint rollback capability.
/// </summary>
public interface IQuerySourceSelector
{
    /// <summary>
    /// Gets the data source to use for a specific endpoint.
    /// </summary>
    /// <param name="endpointName">The endpoint or method name to check.</param>
    /// <returns>The query source to use (ClickHouse or SQLite).</returns>
    QuerySource GetSource(string endpointName);
}

/// <summary>
/// Configuration-driven query source selector.
/// Reads from appsettings.json section "ClickHouseMigration:UseSqlite:{endpointName}".
/// </summary>
public class QuerySourceSelector(IConfiguration config) : IQuerySourceSelector
{
    /// <inheritdoc/>
    public QuerySource GetSource(string endpointName)
    {
        // Check config for per-endpoint overrides
        // Default to ClickHouse during migration
        var key = $"ClickHouseMigration:UseSqlite:{endpointName}";
        return config.GetValue<bool>(key) ? QuerySource.SQLite : QuerySource.ClickHouse;
    }
}
