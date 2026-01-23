using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Hourly player counts per server.
/// Collected from BFList API polling and aggregated to hourly granularity.
/// ~4.3M rows for 180 days of data across all servers.
/// </summary>
public class ServerOnlineCount
{
    public required string ServerGuid { get; set; }
    public Instant HourTimestamp { get; set; } // Truncated to hour
    public required string Game { get; set; } // bf1942, fh2, bfvietnam
    public double AvgPlayers { get; set; }
    public int PeakPlayers { get; set; }
    public int SampleCount { get; set; } // Number of 30s samples averaged
}
