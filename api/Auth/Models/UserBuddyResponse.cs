namespace api.Auth.Models;

public class UserBuddyResponse
{
    public int Id { get; set; }
    public string BuddyPlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public PlayerInfoResponse? Player { get; set; }
}
