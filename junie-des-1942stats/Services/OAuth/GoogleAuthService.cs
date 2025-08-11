using Google.Apis.Auth;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using junie_des_1942stats.Caching;

namespace junie_des_1942stats.Services.OAuth;

public interface IGoogleAuthService
{
    Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string idToken, string? ipAddress = null);
}

public class GoogleAuthService : IGoogleAuthService
{
    private readonly ILogger<GoogleAuthService> _logger;
    private readonly ICacheService _cacheService;
    private readonly GoogleJsonWebSignature.ValidationSettings _validationSettings;

    public GoogleAuthService(
        IConfiguration configuration,
        ILogger<GoogleAuthService> logger,
        ICacheService cacheService)
    {
        _logger = logger;
        _cacheService = cacheService;

        var clientId = configuration["GoogleOAuth:ClientId"] ?? throw new InvalidOperationException("GoogleOAuth:ClientId not configured");

        _validationSettings = new GoogleJsonWebSignature.ValidationSettings
        {
            Audience = new[] { clientId },
            IssuedAtClockTolerance = TimeSpan.FromMinutes(5),
            ExpirationTimeClockTolerance = TimeSpan.FromMinutes(5)
        };
    }


    public async Task<GoogleJsonWebSignature.Payload> ValidateGoogleTokenAsync(string idToken, string? ipAddress = null)
    {
        // Rate limiting check
        if (!await CheckRateLimitAsync(ipAddress))
        {
            throw new UnauthorizedAccessException("Too many authentication attempts");
        }

        try
        {
            // Basic token format validation
            if (string.IsNullOrWhiteSpace(idToken) || !idToken.Contains('.'))
            {
                throw new InvalidJwtException("Invalid token format");
            }

            // Validate the Google ID token using Google's library
            var payload = await GoogleJsonWebSignature.ValidateAsync(idToken, _validationSettings);

            // Additional security checks
            if (string.IsNullOrEmpty(payload.Email) || !payload.EmailVerified)
            {
                throw new UnauthorizedAccessException("Email not verified");
            }

            // Check token age (Google ID tokens shouldn't be older than 1 hour)
            if (payload.IssuedAtTimeSeconds.HasValue)
            {
                var issuedAt = DateTimeOffset.FromUnixTimeSeconds(payload.IssuedAtTimeSeconds.Value);
                if (DateTimeOffset.UtcNow.Subtract(issuedAt) > TimeSpan.FromHours(1))
                {
                    throw new UnauthorizedAccessException("Token too old");
                }
            }

            _logger.LogInformation("Successful Google token validation for email: {Email}", payload.Email);
            return payload;
        }
        catch (InvalidJwtException ex)
        {
            _logger.LogWarning(ex, "Invalid Google JWT token from IP: {IpAddress}", ipAddress);
            await IncrementRateLimitAsync(ipAddress);
            throw new UnauthorizedAccessException("Invalid Google token");
        }
        catch (Exception ex) when (!(ex is UnauthorizedAccessException))
        {
            _logger.LogError(ex, "Google token validation failed from IP: {IpAddress}", ipAddress);
            await IncrementRateLimitAsync(ipAddress);
            throw new UnauthorizedAccessException("Token validation failed");
        }
    }

    private async Task<bool> CheckRateLimitAsync(string? ipAddress)
    {
        if (string.IsNullOrEmpty(ipAddress)) return true;

        var rateLimitKey = $"auth_attempts:{ipAddress}";
        var rateLimitData = await _cacheService.GetAsync<RateLimitData>(rateLimitKey);
        var attempts = rateLimitData?.Attempts ?? 0;
        return attempts < 20; // Max 20 attempts per IP per hour (more lenient than before)
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

public class RateLimitData
{
    public int Attempts { get; set; }
}