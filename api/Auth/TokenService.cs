using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Auth;

public interface ITokenService
{
    (string accessToken, DateTime expiresAt) CreateAccessToken(User user);
}

public class TokenService : ITokenService
{
    private readonly IConfiguration _configuration;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly SigningCredentials _signingCredentials;
    private readonly RsaSecurityKey _securityKey;
    private readonly string _issuer;
    private readonly string _audience;
    private readonly int _accessMinutes;

    public TokenService(IConfiguration configuration)
    {
        _configuration = configuration;

        _issuer = _configuration["Jwt:Issuer"] ?? "";
        _audience = _configuration["Jwt:Audience"] ?? "";
        _accessMinutes = int.TryParse(_configuration["Jwt:AccessTokenMinutes"], out var m) ? m : 10080 /* 7 days */;

        // RS256 only: load RSA private key from inline PEM or file path
        var privateKeyPem = TokenServiceConfigHelpers.ReadConfigStringOrFile(_configuration, "Jwt:PrivateKey", "Jwt:PrivateKeyPath");
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException("JWT private key not configured. Set Jwt:PrivateKey (inline PEM) or Jwt:PrivateKeyPath (file path) for RS256.");

        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        _securityKey = new RsaSecurityKey(rsa);
        _signingCredentials = new SigningCredentials(_securityKey, SecurityAlgorithms.RsaSha256);
    }

    public (string accessToken, DateTime expiresAt) CreateAccessToken(User user)
    {
        var now = DateTime.UtcNow;
        var expires = now.AddMinutes(_accessMinutes);

        var claims = new List<Claim>
        {
            new(JwtRegisteredClaimNames.Sub, user.Id.ToString()),
            new(JwtRegisteredClaimNames.Email, user.Email),
            new(ClaimTypes.NameIdentifier, user.Id.ToString()),
            new(ClaimTypes.Email, user.Email),
            new(JwtRegisteredClaimNames.Iat, DateTimeOffset.UtcNow.ToUnixTimeSeconds().ToString(), ClaimValueTypes.Integer64)
        };

        var token = new JwtSecurityToken(
            issuer: _issuer,
            audience: _audience,
            claims: claims,
            notBefore: now,
            expires: expires,
            signingCredentials: _signingCredentials
        );

        var jwt = _tokenHandler.WriteToken(token);
        return (jwt, expires);
    }
}

internal static class TokenServiceConfigHelpers
{
    public static string? ReadConfigStringOrFile(IConfiguration config, string valueKey, string pathKey)
    {
        var v = config[valueKey];
        if (!string.IsNullOrWhiteSpace(v)) return v;
        var path = config[pathKey];
        if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
        {
            return File.ReadAllText(path);
        }
        return null;
    }
}
