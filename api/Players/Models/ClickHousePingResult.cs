namespace api.Players.Models;

public class ClickHousePingResult
{
    public string server_guid { get; set; } = "";
    public double average_ping { get; set; }
    public int sample_size { get; set; }
}
