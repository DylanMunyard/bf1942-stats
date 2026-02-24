namespace api.PlayerRelationships.Models;

/// <summary>
/// DTOs for raw SQL query results.
/// Required for EF Core's SqlQueryRaw which needs actual entity types, not ValueTuples.
/// </summary>

/// <summary>
/// Result from counting sessions per player.
/// </summary>
public class SessionCountResult
{
    public string PlayerName { get; set; } = "";
    public int Count { get; set; }
}

/// <summary>
/// Result from hour-of-day distribution query.
/// </summary>
public class HourDistributionResult
{
    public int Hour { get; set; }
    public int Count { get; set; }
}

/// <summary>
/// Result from server affinity count query.
/// </summary>
public class CommonServerCountResult
{
    public int CommonServers { get; set; }
}

/// <summary>
/// Result from total unique servers count query.
/// </summary>
public class TotalServersResult
{
    public int TotalServers { get; set; }
}

/// <summary>
/// Result from ping statistics query.
/// </summary>
public class PingStatResult
{
    public string ServerGuid { get; set; } = "";
    public double? AvgPing { get; set; }
}

/// <summary>
/// Result from session statistics query.
/// </summary>
public class SessionStatResult
{
    public int SessionCount { get; set; }
    public long? TotalMinutes { get; set; }
    public double? AvgSessionMinutes { get; set; }
}

/// <summary>
/// Result from activity period query.
/// </summary>
public class ActivityPeriodResult
{
    public DateTime? FirstSeen { get; set; }
    public DateTime? LastSeen { get; set; }
    public int SessionCount { get; set; }
    public int ActiveDays { get; set; }
}

/// <summary>
/// Result from daily activity aggregation query.
/// </summary>
public class DailyActivityResult
{
    public DateTime? Date { get; set; }
    public int SessionCount { get; set; }
    public long? TotalMinutes { get; set; }
    public double? AvgKd { get; set; }
}
