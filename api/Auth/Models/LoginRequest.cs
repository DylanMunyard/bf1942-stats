namespace api.Auth.Models;

public class LoginRequest
{
    public string? DiscordCode { get; set; }
    public string? RedirectUri { get; set; }
}
