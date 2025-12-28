using System.Diagnostics;

namespace api.Telemetry;

public static class ActivitySources
{
    public static readonly ActivitySource PlayerStats = new("junie-des-1942stats.PlayerStats");
    public static readonly ActivitySource Database = new("junie-des-1942stats.Database");
    public static readonly ActivitySource BfListApi = new("junie-des-1942stats.BfListApi");
    public static readonly ActivitySource Cache = new("junie-des-1942stats.Cache");
    public static readonly ActivitySource ClickHouse = new("junie-des-1942stats.ClickHouse");
    public static readonly ActivitySource StatsCollection = new("junie-des-1942stats.StatsCollection");
    public static readonly ActivitySource ClickHouseSync = new("junie-des-1942stats.ClickHouseSync");
    public static readonly ActivitySource Gamification = new("junie-des-1942stats.Gamification");
    public static readonly ActivitySource RankingCalculation = new("junie-des-1942stats.RankingCalculation");
    public static readonly ActivitySource AggregateCalculation = new("junie-des-1942stats.AggregateCalculation");
    public static readonly ActivitySource SqliteAnalytics = new("BfStats.SqliteAnalytics");
    public static readonly ActivitySource Backfill = new("BfStats.Backfill");
}
