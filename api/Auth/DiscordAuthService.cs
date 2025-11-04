using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using api.Caching;
using System.Text.Json;

namespace api.Auth;

public interface IDiscordAuthService
{
    Task<DiscordUserPayload> ExchangeCodeForUserAsync(string code, string redirectUri, string? ipAddress = null);
}

public class DiscordAuthService : IDiscordAuthService
{
    private readonly ILogger<DiscordAuthService> _logger;
    private readonly ICacheService _cacheService;
    private readonly HttpClient _httpClient;
    private readonly string _clientId;
    private readonly string _clientSecret;

    public DiscordAuthService(
        IConfiguration configuration,
        ILogger<DiscordAuthService> logger,
        ICacheService cacheService,
        IHttpClientFactory httpClientFactory)
    {
        _logger = logger;
        _cacheService = cacheService;
        _httpClient = httpClientFactory.CreateClient();

        _clientId = configuration["DiscordOAuth:ClientId"] ?? throw new InvalidOperationException("DiscordOAuth:ClientId not configured");
        _clientSecret = configuration["DiscordOAuth:ClientSecret"] ?? throw new InvalidOperationException("DiscordOAuth:ClientSecret not configured");
    }

    public async Task<DiscordUserPayload> ExchangeCodeForUserAsync(string code, string redirectUri, string? ipAddress = null)
    {
        if (!await CheckRateLimitAsync(ipAddress))
        {
            throw new UnauthorizedAccessException("Too many authentication attempts");
        }

        try
        {
            if (string.IsNullOrWhiteSpace(code))
            {
                throw new ArgumentException("Authorization code is required");
            }

            var accessToken = await ExchangeCodeForAccessTokenAsync(code, redirectUri);
            var userPayload = await GetDiscordUserAsync(accessToken);

            if (string.IsNullOrEmpty(userPayload.Email))
            {
                throw new UnauthorizedAccessException("Discord account does not have a verified email");
            }

            _logger.LogInformation("Successful Discord authentication for email: {Email}", userPayload.Email);
            return userPayload;
        }
        catch (HttpRequestException ex)
        {
            _logger.LogError(ex, "Discord API request failed from IP: {IpAddress}", ipAddress);
            await IncrementRateLimitAsync(ipAddress);
            throw new UnauthorizedAccessException("Discord authentication failed");
        }
        catch (Exception ex) when (!(ex is UnauthorizedAccessException))
        {
            _logger.LogError(ex, "Discord token exchange failed from IP: {IpAddress}", ipAddress);
            await IncrementRateLimitAsync(ipAddress);
            throw new UnauthorizedAccessException("Discord authentication failed");
        }
    }

    private async Task<string> ExchangeCodeForAccessTokenAsync(string code, string redirectUri)
    {
        var formData = new Dictionary<string, string>
        {
            { "client_id", _clientId },
            { "client_secret", _clientSecret },
            { "grant_type", "authorization_code" },
            { "code", code },
            { "redirect_uri", redirectUri }
        };

        var response = await _httpClient.PostAsync(
            "https://discord.com/api/oauth2/token",
            new FormUrlEncodedContent(formData)
        );

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogError("Discord token exchange failed: {StatusCode} - {Error} - RedirectUri: {RedirectUri}",
                response.StatusCode, errorContent, redirectUri);
            throw new UnauthorizedAccessException($"Failed to exchange Discord authorization code: {response.StatusCode} - {errorContent}");
        }

        var content = await response.Content.ReadAsStringAsync();
        var tokenResponse = JsonSerializer.Deserialize<DiscordTokenResponse>(content);

        if (tokenResponse?.AccessToken == null)
        {
            throw new UnauthorizedAccessException("Invalid Discord token response");
        }

        return tokenResponse.AccessToken;
    }

    private async Task<DiscordUserPayload> GetDiscordUserAsync(string accessToken)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, "https://discord.com/api/users/@me");
        request.Headers.Add("Authorization", $"Bearer {accessToken}");

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var errorContent = await response.Content.ReadAsStringAsync();
            _logger.LogWarning("Discord user fetch failed: {StatusCode} - {Error}", response.StatusCode, errorContent);
            throw new UnauthorizedAccessException("Failed to fetch Discord user information");
        }

        var content = await response.Content.ReadAsStringAsync();
        var userResponse = JsonSerializer.Deserialize<DiscordUserResponse>(content);

        if (userResponse == null)
        {
            throw new UnauthorizedAccessException("Invalid Discord user response");
        }

        return new DiscordUserPayload
        {
            Id = userResponse.Id ?? throw new UnauthorizedAccessException("Discord user ID missing"),
            Username = userResponse.Username ?? "Unknown",
            Email = userResponse.Email ?? throw new UnauthorizedAccessException("Discord email missing"),
            Discriminator = userResponse.Discriminator,
            Avatar = userResponse.Avatar,
            Verified = userResponse.Verified ?? false
        };
    }

    private async Task<bool> CheckRateLimitAsync(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return true;

        var rateLimitKey = $"auth_attempts:{ipAddress}";
        var rateLimitData = await _cacheService.GetAsync<RateLimitData>(rateLimitKey);
        var attempts = rateLimitData?.Attempts ?? 0;
        return attempts < 20;
    }

    private async Task IncrementRateLimitAsync(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return;

        var rateLimitKey = $"auth_attempts:{ipAddress}";
        var rateLimitData = await _cacheService.GetAsync<RateLimitData>(rateLimitKey);
        var attempts = rateLimitData?.Attempts ?? 0;
        await _cacheService.SetAsync(rateLimitKey, new RateLimitData { Attempts = attempts + 1 }, TimeSpan.FromHours(1));
    }
}

public class DiscordTokenResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("access_token")]
    public string? AccessToken { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("token_type")]
    public string? TokenType { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("expires_in")]
    public int ExpiresIn { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("refresh_token")]
    public string? RefreshToken { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("scope")]
    public string? Scope { get; set; }
}

public class DiscordUserResponse
{
    [System.Text.Json.Serialization.JsonPropertyName("id")]
    public string? Id { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("username")]
    public string? Username { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("discriminator")]
    public string? Discriminator { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("avatar")]
    public string? Avatar { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("email")]
    public string? Email { get; set; }

    [System.Text.Json.Serialization.JsonPropertyName("verified")]
    public bool? Verified { get; set; }
}

public class DiscordUserPayload
{
    public string Id { get; set; } = "";
    public string Username { get; set; } = "";
    public string Email { get; set; } = "";
    public string? Discriminator { get; set; }
    public string? Avatar { get; set; }
    public bool Verified { get; set; }
}

public class RateLimitData
{
    public int Attempts { get; set; }
}
