namespace junie_des_1942stats.ServerStats.Models
{
    public class ServerInsights
    {
        public string? ServerGuid { get; set; }
        public string ServerName { get; set; } = "";
        public DateTime StartPeriod { get; set; }
        public DateTime EndPeriod { get; set; }
        public PingByHourInsight? PingByHour { get; set; }

        // Current period player count data
        public List<PlayerCountDataPoint> PlayerCountHistory { get; set; } = [];
        
        // Comparison period player count data (for the same time range in the previous period)
        public List<PlayerCountDataPoint> PlayerCountHistoryComparison { get; set; } = [];
        
        public PlayerCountSummary? PlayerCountSummary { get; set; }

        // Maps analysis
        public List<PopularMapDataPoint> Maps { get; set; } = [];
    }

    public class PlayerCountDataPoint
    {
        public DateTime Timestamp { get; set; }
        public int PlayerCount { get; set; }
        public int UniquePlayersStarted { get; set; } // Players who started rounds in this period
    }

    public class PlayerCountSummary
    {
        public double AveragePlayerCount { get; set; }
        public int PeakPlayerCount { get; set; }
        public DateTime PeakTimestamp { get; set; }
        public int? ChangePercentFromPreviousPeriod { get; set; }
        public int TotalUniquePlayersInPeriod { get; set; }
    }

    public class PingByHourInsight
    {
        public List<PingDataPoint> Data { get; set; } = [];
    }

    public class PingDataPoint
    {
        public DateTime TimePeriod { get; set; }
        public double AveragePing { get; set; }
        public double MedianPing { get; set; }
        public double P95Ping { get; set; }

        // Legacy property for backward compatibility
        public int Hour => TimePeriod.Hour;
    }

    public class PopularMapDataPoint
    {
        public string MapName { get; set; } = "";
        public double AveragePlayerCount { get; set; }
        public int PeakPlayerCount { get; set; }
        public int TotalPlayTime { get; set; } // Total minutes the map was active
        public double PlayTimePercentage { get; set; } // Percentage of total server time
    }
}