using System;
using System.Collections.Generic;

namespace junie_des_1942stats.ServerStats.Models
{
    public class ServerInsights
    {
        public string? ServerGuid { get; set; }
        public string ServerName { get; set; } = "";
        public DateTime StartPeriod { get; set; }
        public DateTime EndPeriod { get; set; }
        public PingByHourInsight? PingByHour { get; set; }
        public ScoreVolatilityInsight? MostVolatilePlayers { get; set; }
        public ScoreConsistencyInsight? LeastConsistentPlayers { get; set; }
    }

    public class PingByHourInsight
    {
        public List<PingDataPoint> Data { get; set; } = [];
    }

    public class PingDataPoint
    {
        public int Hour { get; set; }
        public double AveragePing { get; set; }
        public double MedianPing { get; set; }
        public double P95Ping { get; set; }
    }

    public class ScoreVolatilityInsight
    {
        public List<VolatilePlayer> Players { get; set; } = [];
    }

    public class VolatilePlayer
    {
        public string PlayerName { get; set; } = "";
        public int TotalNegativeDeltas { get; set; }
        public int SessionsAnalyzed { get; set; }
        public double AverageNegativeDeltasPerSession { get; set; }
        public int LargestSingleDrop { get; set; }
    }

    public class ScoreConsistencyInsight
    {
        public List<InconsistentPlayer> Players { get; set; } = [];
    }

    public class InconsistentPlayer
    {
        public string PlayerName { get; set; } = "";
        public double PercentageDecreasingObservations { get; set; }
        public int TotalObservations { get; set; }
        public int DecreasingObservations { get; set; }
        public int SessionsAnalyzed { get; set; }
    }
} 