using System;

namespace api.ClickHouse.Models;

public class TeamKillerMetrics
{
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public string TeamName { get; set; } = "";
    public string MapName { get; set; } = "";
    public int CurrentScore { get; set; }
    public int CurrentKills { get; set; }
    public int CurrentDeaths { get; set; }
    public int UnexplainedDropsLast10Min { get; set; }
    public int TotalPenaltiesLast10Min { get; set; }
    public double TkProbability { get; set; }
    public DateTime LastActivity { get; set; }
}