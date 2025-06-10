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
        public NegativeScoreInsight? MostNegativeScorePlayers { get; set; }
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

    public class NegativeScoreInsight
    {
        public List<NegativeScorePlayer> Players { get; set; } = [];
    }

    public class NegativeScorePlayer
    {
        public string PlayerName { get; set; } = "";
        public int TotalNegativeScore { get; set; }
        public int NegativeObservationCount { get; set; }
        public double AverageNegativeScore { get; set; }
    }
} 