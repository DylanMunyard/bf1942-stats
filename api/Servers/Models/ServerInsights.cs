using api.Bflist;

namespace api.ServerStats.Models
{
    public class ServerInsights
    {
        public string? ServerGuid { get; set; }
        public string ServerName { get; set; } = "";
        public DateTime StartPeriod { get; set; }
        public DateTime EndPeriod { get; set; }
        public PlayersOnlineHistoryResponse? PlayersOnlineHistory { get; set; }
    }

    public class ServerMapsInsights
    {
        public string? ServerGuid { get; set; }
        public string ServerName { get; set; } = "";
        public DateTime StartPeriod { get; set; }
        public DateTime EndPeriod { get; set; }
        public List<PopularMapDataPoint> Maps { get; set; } = [];
    }


    public class PopularMapDataPoint
    {
        public string MapName { get; set; } = "";
        public double AveragePlayerCount { get; set; }
        public int PeakPlayerCount { get; set; }
        public int TotalPlayTime { get; set; } // Total minutes the map was active
        public double PlayTimePercentage { get; set; } // Percentage of total server time
        public int Team1Victories { get; set; } // Number of team 1 victories on this map
        public int Team2Victories { get; set; } // Number of team 2 victories on this map
        public string? Team1Label { get; set; } // Most common team 1 label on this map
        public string? Team2Label { get; set; } // Most common team 2 label on this map
    }
}
