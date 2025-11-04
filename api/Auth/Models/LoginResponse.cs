namespace api.Auth.Models;

public class LoginResponse
{
    public UserDto User { get; set; } = new();
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}
