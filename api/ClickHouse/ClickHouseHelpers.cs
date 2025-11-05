namespace api.ClickHouse;

/// <summary>
/// Shared utility methods for ClickHouse operations
/// </summary>
public static class ClickHouseHelpers
{
    /// <summary>
    /// Escapes and quotes a string for safe use in ClickHouse SQL queries
    /// </summary>
    /// <param name="value">The value to quote</param>
    /// <returns>A properly quoted and escaped string for SQL use</returns>
    public static string QuoteString(string value)
    {
        if (value == null)
            return "NULL";

        return $"'{value.Replace("'", "''")}'";
    }

    /// <summary>
    /// Escapes a string for safe use in ClickHouse SQL queries without adding quotes
    /// </summary>
    /// <param name="value">The value to escape</param>
    /// <returns>An escaped string for SQL use</returns>
    public static string EscapeString(string value)
    {
        if (value == null)
            return "";

        return value.Replace("'", "''");
    }
}
