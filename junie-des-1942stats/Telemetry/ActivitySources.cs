using System.Diagnostics;

namespace junie_des_1942stats.Telemetry;

public static class ActivitySources
{
    public static readonly ActivitySource PlayerStats = new("junie-des-1942stats.PlayerStats");
    public static readonly ActivitySource Database = new("junie-des-1942stats.Database");
    public static readonly ActivitySource BfListApi = new("junie-des-1942stats.BfListApi");
    public static readonly ActivitySource Cache = new("junie-des-1942stats.Cache");
    public static readonly ActivitySource ClickHouse = new("junie-des-1942stats.ClickHouse");
    public static readonly ActivitySource StatsCollection = new("junie-des-1942stats.StatsCollection");
}