using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Security.Cryptography;
using Microsoft.Extensions.Configuration;
using Microsoft.IdentityModel.Tokens;
using api.PlayerTracking;

namespace api.Auth;

public interface ITokenService
{
    (string accessToken, DateTime expiresAt) CreateAccessToken(User user);
}

public class TokenService(IConfiguration configuration) : ITokenService
{
    private readonly IConfiguration _configuration = configuration;
    private readonly JwtSecurityTokenHandler _tokenHandler = new();
    private readonly SigningCredentials _signingCredentials = InitializeSigningCredentials(configuration);
    private readonly RsaSecurityKey _securityKey = InitializeSecurityKey(configuration);
    private readonly string _issuer = configuration["Jwt:Issuer"] ?? "";
    private readonly string _audience = configuration["Jwt:Audience"] ?? "";
    private readonly int _accessMinutes = int.TryParse(configuration["Jwt:AccessTokenMinutes"], out var m) ? m : 10080 /* 7 days */;

    private static RsaSecurityKey InitializeSecurityKey(IConfiguration configuration)
    {
        var privateKeyPem = TokenServiceConfigHelpers.ReadConfigStringOrFile(configuration, "Jwt:PrivateKey", "Jwt:PrivateKeyPath");
        if (string.IsNullOrWhiteSpace(privateKeyPem))
            throw new InvalidOperationException("JWT private key not configured. Set Jwt:PrivateKey (inline PEM) or Jwt:PrivateKeyPath (file path) for RS256.");

        var rsa = RSA.Create();
        rsa.ImportFromPem(privateKeyPem);
        return new RsaSecurityKey(rsa);
    }

    private static SigningCredentials InitializeSigningCredentials(IConfiguration configuration)
    {
        var securityKey = InitializeSecurityKey(configuration);
        return new SigningCredentials(securityKey, SecurityAlgorithms.RsaSha256);
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
