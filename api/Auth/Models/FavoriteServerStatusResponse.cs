namespace api.Auth.Models;

public class FavoriteServerStatusResponse
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public string? CurrentMap { get; set; }
    public string? JoinLink { get; set; }
}
