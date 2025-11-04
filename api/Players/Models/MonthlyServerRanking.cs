namespace api.PlayerStats.Models;

public class MonthlyServerRanking
{
    public int Year { get; set; }
    public int Month { get; set; }
    public int Rank { get; set; }
    public int TotalScore { get; set; }
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public double KDRatio { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public string MonthYearDisplay => $"{Year}-{Month:D2}";
}
