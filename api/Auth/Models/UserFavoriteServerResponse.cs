namespace api.Auth.Models;

public class UserFavoriteServerResponse
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ActiveSessions { get; set; }
    public string? CurrentMap { get; set; }
    public int? MaxPlayers { get; set; }
    public string? JoinLink { get; set; }
}
