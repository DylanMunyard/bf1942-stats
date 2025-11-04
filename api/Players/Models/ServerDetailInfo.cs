namespace api.PlayerStats.Models;

public class ServerDetailInfo
{
    public string Guid { get; set; } = "";
    public string Name { get; set; } = "";
    public string Address { get; set; } = "";
    public int Port { get; set; }
    public string Country { get; set; } = "";
    public string CountryCode { get; set; } = "";
    public int MaxPlayers { get; set; }
    public string GameId { get; set; } = "";
}
