namespace api.ClickHouse.Models;

/// <summary>
/// Player metric record for ClickHouse
/// </summary>
public class PlayerMetric
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string PlayerName { get; set; } = "";
    public int Score { get; set; }
    public ushort Kills { get; set; }
    public ushort Deaths { get; set; }
    public ushort Ping { get; set; }
    public string TeamName { get; set; } = "";
    public string MapName { get; set; } = "";
    public string GameType { get; set; } = "";
    public bool IsBot { get; set; }
    public string Game { get; set; } = "";
}

/// <summary>
/// Server online count record for ClickHouse
/// </summary>
public class ServerOnlineCount
{
    public DateTime Timestamp { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public ushort PlayersOnline { get; set; }
    public string MapName { get; set; } = "";
    public string Game { get; set; } = "";
}
