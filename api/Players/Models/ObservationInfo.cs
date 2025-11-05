namespace api.Players.Models;

public class ObservationInfo
{
    public DateTime Timestamp { get; set; }
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }
    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";
}
