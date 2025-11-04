namespace api.PlayerStats.Models;

public class ObservationBucket
{
    public DateTime Timestamp { get; set; }
    public string PlayerName { get; set; } = "";
    public List<ObservationInfo> Observations { get; set; } = new();
}
