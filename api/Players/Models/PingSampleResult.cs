namespace api.Players.Models;

public class PingSampleResult
{
    public string ServerGuid { get; set; } = "";
    public double AveragePing { get; set; }
    public int SampleSize { get; set; }
}
