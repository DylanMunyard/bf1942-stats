namespace junie_des_1942stats.ServerStats.Models
{
    public class ServerInsights
    {
        public string? ServerGuid { get; set; }
        public string ServerName { get; set; } = "";
        public DateTime StartPeriod { get; set; }
        public DateTime EndPeriod { get; set; }
        public PingByHourInsight? PingByHour { get; set; }
        
        // New improved player count data structure
        public List<PlayerCountDataPoint> PlayerCountHistory { get; set; } = [];
        public PlayerCountSummary? PlayerCountSummary { get; set; }
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
} 