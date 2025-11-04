namespace api.Auth.Models;

public class UserPlayerNameResponse
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public PlayerInfoResponse? Player { get; set; }
}
