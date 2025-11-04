namespace api.PlayerStats.Models;

public class HourlyActivity
{
    public int Hour { get; set; }
    public int MinutesActive { get; set; }
    public string FormattedHour => $"{Hour:D2}:00 - {Hour:D2}:59";
}
