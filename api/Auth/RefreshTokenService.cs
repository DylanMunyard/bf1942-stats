using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using api.PlayerTracking;

namespace api.Auth;

public interface IRefreshTokenService
{
    Task<(string rawToken, RefreshToken entity)> CreateAsync(User user, string? ip, string? userAgent, CancellationToken ct = default);
    Task<(RefreshToken token, User user)> ValidateAsync(string rawToken, CancellationToken ct = default);
    Task<(string newRawToken, RefreshToken newEntity)> RotateAsync(RefreshToken current, string? ip, string? userAgent, CancellationToken ct = default);
    Task RevokeFamilyAsync(RefreshToken token, CancellationToken ct = default);
    void SetCookie(HttpResponse response, string rawToken, DateTime expiresAt);
    void ClearCookie(HttpResponse response);
}

public class RefreshTokenService : IRefreshTokenService
{
    private readonly PlayerTrackerDbContext _db;
    private readonly IConfiguration _config;
    private readonly ILogger<RefreshTokenService> _logger;
    private readonly string _cookieName;
    private readonly string? _cookieDomain;
    private readonly string _cookiePath;
    private readonly int _days;
    private readonly string _hmacSecret;
    private readonly bool _isDevelopment;

    public RefreshTokenService(PlayerTrackerDbContext db, IConfiguration config, ILogger<RefreshTokenService> logger)
    {
        _db = db;
        _config = config;
        _logger = logger;
        _cookieName = _config["RefreshToken:CookieName"] ?? "rt";
        _cookieDomain = _config["RefreshToken:CookieDomain"];
        _cookiePath = _config["RefreshToken:CookiePath"] ?? "/stats";
        _days = int.TryParse(_config["RefreshToken:Days"], out var d) ? d : 60;
        _hmacSecret = _config["RefreshToken:Secret"] ?? throw new InvalidOperationException("RefreshToken:Secret missing");
        _isDevelopment = (Environment.GetEnvironmentVariable("ASPNETCORE_ENVIRONMENT") ?? "Development") == "Development";
    }

    public async Task<(string rawToken, RefreshToken entity)> CreateAsync(User user, string? ip, string? userAgent, CancellationToken ct = default)
    {
        var raw = Guid.NewGuid().ToString("N") + Convert.ToHexString(RandomNumberGenerator.GetBytes(32));
        var hash = ComputeHash(raw);
        var now = DateTime.UtcNow;
        var entity = new RefreshToken
        {
            UserId = user.Id,
            TokenHash = hash,
            CreatedAt = now,
            ExpiresAt = now.AddDays(_days),
            IpAddress = ip,
            UserAgent = userAgent
        };
        _db.RefreshTokens.Add(entity);
        await _db.SaveChangesAsync(ct);
        return (raw, entity);
    }

    public async Task<(RefreshToken token, User user)> ValidateAsync(string rawToken, CancellationToken ct = default)
    {
        var hash = ComputeHash(rawToken);
        var token = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == hash, ct);
        if (token == null)
            throw new UnauthorizedAccessException("invalid");
        if (token.RevokedAt != null)
        {
            // reuse detection -> revoke family
            await RevokeFamilyAsync(token, ct);
            throw new UnauthorizedAccessException("reused");
        }
        if (token.ExpiresAt <= DateTime.UtcNow)
            throw new UnauthorizedAccessException("expired");

        var user = await _db.Users.FirstAsync(u => u.Id == token.UserId, ct);
        return (token, user);
    }

    public async Task<(string newRawToken, RefreshToken newEntity)> RotateAsync(RefreshToken current, string? ip, string? userAgent, CancellationToken ct = default)
    {
        if (current.RevokedAt != null) throw new UnauthorizedAccessException("revoked");

        var (newRaw, newEntity) = await CreateAsync(new User { Id = current.UserId, Email = string.Empty }, ip, userAgent, ct);

        // reload actual userId not needed; link family
        current.RevokedAt = DateTime.UtcNow;
        current.ReplacedByTokenHash = newEntity.TokenHash;
        await _db.SaveChangesAsync(ct);

        return (newRaw, newEntity);
    }

    public async Task RevokeFamilyAsync(RefreshToken token, CancellationToken ct = default)
    {
        // Walk forward revocations
        var toRevoke = new List<RefreshToken> { token };
        while (true)
        {
            var next = await _db.RefreshTokens.FirstOrDefaultAsync(t => t.TokenHash == token.ReplacedByTokenHash!, ct);
            if (next == null) break;
            toRevoke.Add(next);
            token = next;
        }

        foreach (var t in toRevoke)
        {
            if (t.RevokedAt == null) t.RevokedAt = DateTime.UtcNow;
        }
        await _db.SaveChangesAsync(ct);
    }

    public void SetCookie(HttpResponse response, string rawToken, DateTime expiresAt)
    {
        var opts = new CookieOptions
        {
            HttpOnly = true,
            Secure = !_isDevelopment,
            SameSite = SameSiteMode.Lax,
            Expires = expiresAt,
            MaxAge = expiresAt - DateTime.UtcNow,
            Path = _cookiePath
        };
        if (!string.IsNullOrEmpty(_cookieDomain)) opts.Domain = _cookieDomain;
        response.Cookies.Append(_cookieName, rawToken, opts);
    }

    public void ClearCookie(HttpResponse response)
    {
        response.Cookies.Delete(_cookieName, new CookieOptions
        {
            HttpOnly = true,
            Secure = !_isDevelopment,
            SameSite = SameSiteMode.Lax,
            Path = _cookiePath,
            Domain = _cookieDomain
        });
    }

    private string ComputeHash(string raw)
    {
        using var hmac = new HMACSHA256(Encoding.UTF8.GetBytes(_hmacSecret));
        var bytes = hmac.ComputeHash(Encoding.UTF8.GetBytes(raw));
        return Convert.ToHexString(bytes);
    }
}
