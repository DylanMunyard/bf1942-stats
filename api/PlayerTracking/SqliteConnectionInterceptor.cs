using System.Data.Common;
using Microsoft.EntityFrameworkCore.Diagnostics;
using Microsoft.Extensions.Logging;

namespace api.PlayerTracking;

/// <summary>
/// Interceptor that configures SQLite PRAGMA settings on each new connection.
/// This is necessary because SQLite PRAGMAs like busy_timeout are connection-specific
/// and must be set each time a new connection is opened.
/// </summary>
public class SqliteConnectionInterceptor(ILogger<SqliteConnectionInterceptor> logger, int busyTimeoutMs = 5000)
    : DbConnectionInterceptor
{
    public override void ConnectionOpened(DbConnection connection, ConnectionEndEventData eventData)
    {
        ConfigureConnection(connection);
        base.ConnectionOpened(connection, eventData);
    }

    public override async Task ConnectionOpenedAsync(DbConnection connection, ConnectionEndEventData eventData, CancellationToken cancellationToken = default)
    {
        ConfigureConnection(connection);
        await base.ConnectionOpenedAsync(connection, eventData, cancellationToken);
    }

    private void ConfigureConnection(DbConnection connection)
    {
        try
        {
            using var command = connection.CreateCommand();

            // Set busy_timeout - wait for locks instead of failing immediately with SQLITE_BUSY
            command.CommandText = $"PRAGMA busy_timeout = {busyTimeoutMs};";
            command.ExecuteNonQuery();
        }
        catch (Exception ex)
        {
            logger.LogWarning(ex, "Failed to configure SQLite PRAGMA settings on connection");
        }
    }
}
