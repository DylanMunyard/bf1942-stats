namespace api.Auth.Models;

public class UserProfileResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoggedIn { get; set; }
    public bool IsActive { get; set; }
    public List<UserPlayerNameResponse> PlayerNames { get; set; } = [];
    public List<UserFavoriteServerResponse> FavoriteServers { get; set; } = [];
    public List<UserBuddyResponse> Buddies { get; set; } = [];
}
