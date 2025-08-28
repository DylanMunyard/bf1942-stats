using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using junie_des_1942stats.Services.Auth;
using Microsoft.Extensions.Configuration;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Services.OAuth;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AuthController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly IGoogleAuthService _googleAuthService;
    private readonly ILogger<AuthController> _logger;
    private readonly ITokenService _tokenService;
    private readonly IRefreshTokenService _refreshTokenService;
    private readonly IConfiguration _configuration;

    public AuthController(
        PlayerTrackerDbContext context,
        IGoogleAuthService googleAuthService,
        ILogger<AuthController> logger,
        ITokenService tokenService,
        IRefreshTokenService refreshTokenService,
        IConfiguration configuration)
    {
        _context = context;
        _googleAuthService = googleAuthService;
        _logger = logger;
        _tokenService = tokenService;
        _refreshTokenService = refreshTokenService;
        _configuration = configuration;
    }

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();

            var googlePayload = await _googleAuthService.ValidateGoogleTokenAsync(request.GoogleIdToken, ipAddress);
            var user = await CreateOrUpdateUserAsync(googlePayload.Email, googlePayload.Name);

            var (accessToken, expiresAt) = _tokenService.CreateAccessToken(user);
            var (rawRefresh, rtEntity) = await _refreshTokenService.CreateAsync(user, ipAddress, Request.Headers.UserAgent.ToString());
            _refreshTokenService.SetCookie(Response, rawRefresh, rtEntity.ExpiresAt);

            return Ok(new LoginResponse
            {
                User = new UserDto { Id = user.Id, Email = user.Email, Name = googlePayload.Name ?? user.Email },
                AccessToken = accessToken,
                ExpiresAt = expiresAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            _logger.LogWarning(ex, "Google token validation failed");
            return Unauthorized(new { message = "Invalid Google token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Login error");
            return StatusCode(500, new { message = "Login failed" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            EnforceCsrfForCookieEndpoints();
            var raw = Request.Cookies[_configuration["RefreshToken:CookieName"] ?? "rt"];
            if (string.IsNullOrEmpty(raw)) return Unauthorized(new { message = "Missing refresh token" });

            var (token, user) = await _refreshTokenService.ValidateAsync(raw);
            var (newRaw, newEntity) = await _refreshTokenService.RotateAsync(token, GetClientIpAddress(), Request.Headers.UserAgent.ToString());
            _refreshTokenService.SetCookie(Response, newRaw, newEntity.ExpiresAt);

            var (accessToken, expiresAt) = _tokenService.CreateAccessToken(user);
            return Ok(new { accessToken, expiresAt });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid refresh token" });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Refresh error");
            return StatusCode(500, new { message = "Refresh failed" });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            EnforceCsrfForCookieEndpoints();
            var raw = Request.Cookies[_configuration["RefreshToken:CookieName"] ?? "rt"];
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    var (token, _) = await _refreshTokenService.ValidateAsync(raw);
                    await _refreshTokenService.RevokeFamilyAsync(token);
                }
                catch { /* ignore */ }
            }
            _refreshTokenService.ClearCookie(Response);
            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Logout error");
            return StatusCode(500);
        }
    }

    [HttpGet("me")]
    [Authorize]
    public IActionResult Me()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        var email = User.FindFirstValue(ClaimTypes.Email);
        if (string.IsNullOrEmpty(userId)) return Unauthorized();
        var id = int.Parse(userId);
        var user = _context.Users.FirstOrDefault(u => u.Id == id);
        if (user == null) return NotFound();
        return Ok(new { user = new UserDto { Id = user.Id, Email = user.Email, Name = email ?? user.Email } });
    }

    // Helper method to get current user from JWT claims
    private async Task<User?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var id))
            return null;

        return await _context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    private void EnforceCsrfForCookieEndpoints()
    {
        var origin = Request.Headers["Origin"].FirstOrDefault();
        var referer = Request.Headers["Referer"].FirstOrDefault();
        var allowedOrigin = _configuration["Cors:AllowedOrigins"];
        if (!string.IsNullOrEmpty(allowedOrigin))
        {
            if (!string.Equals(origin, allowedOrigin, StringComparison.OrdinalIgnoreCase) &&
                !(referer != null && referer.StartsWith(allowedOrigin, StringComparison.OrdinalIgnoreCase)))
            {
                throw new UnauthorizedAccessException("CSRF");
            }
        }
    }

    [HttpGet("profile")]
    [Authorize]
    public async Task<IActionResult> GetProfile()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var userWithData = await _context.Users
                .Include(u => u.PlayerNames)
                    .ThenInclude(pn => pn.Player)
                .Include(u => u.FavoriteServers)
                    .ThenInclude(fs => fs.Server)
                .Include(u => u.Buddies)
                    .ThenInclude(b => b.Player)
                .FirstOrDefaultAsync(u => u.Id == user.Id);

            if (userWithData == null)
                return NotFound(new { message = "User not found" });

            return Ok(new UserProfileResponse
            {
                Id = userWithData.Id,
                Email = userWithData.Email,
                CreatedAt = userWithData.CreatedAt,
                LastLoggedIn = userWithData.LastLoggedIn,
                IsActive = userWithData.IsActive,
                PlayerNames = (await Task.WhenAll(userWithData.PlayerNames
                    .OrderBy(pn => pn.CreatedAt)
                    .Select(async pn => new UserPlayerNameResponse
                    {
                        Id = pn.Id,
                        PlayerName = pn.PlayerName,
                        CreatedAt = pn.CreatedAt,
                        Player = pn.Player != null ? await EnrichPlayerInfoAsync(pn.Player) : null
                    }))).ToList(),
                FavoriteServers = (await Task.WhenAll(userWithData.FavoriteServers
                    .OrderBy(fs => fs.CreatedAt)
                    .Select(async fs => await EnrichFavoriteServerInfoAsync(fs)))).ToList(),
                Buddies = (await Task.WhenAll(userWithData.Buddies
                    .OrderBy(b => b.CreatedAt)
                    .Select(async b => new UserBuddyResponse
                    {
                        Id = b.Id,
                        BuddyPlayerName = b.BuddyPlayerName,
                        CreatedAt = b.CreatedAt,
                        Player = b.Player != null ? await EnrichPlayerInfoAsync(b.Player) : null
                    }))).ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile");
            return StatusCode(500, new { message = "Error retrieving profile" });
        }
    }

    // User Management Endpoints - all use Bearer token auth
    [HttpGet("player-names")]
    [Authorize]
    public async Task<IActionResult> GetPlayerNames()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var userPlayerNames = await _context.UserPlayerNames
                .Include(upn => upn.Player)
                .Where(upn => upn.UserId == user.Id)
                .OrderBy(upn => upn.CreatedAt)
                .ToListAsync();

            var playerNames = (await Task.WhenAll(userPlayerNames.Select(async upn => new UserPlayerNameResponse
            {
                Id = upn.Id,
                PlayerName = upn.PlayerName,
                CreatedAt = upn.CreatedAt,
                Player = upn.Player != null ? await EnrichPlayerInfoAsync(upn.Player) : null
            }))).ToList();

            return Ok(playerNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving player names");
            return StatusCode(500, new { message = "Error retrieving player names" });
        }
    }

    [HttpPost("player-names")]
    [Authorize]
    public async Task<IActionResult> AddPlayerName([FromBody] AddPlayerNameRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            if (string.IsNullOrWhiteSpace(request.PlayerName))
                return BadRequest(new { message = "Player name is required" });

            var existing = await _context.UserPlayerNames
                .FirstOrDefaultAsync(upn => upn.UserId == user.Id && upn.PlayerName == request.PlayerName);

            if (existing != null)
            {
                return Ok(new UserPlayerNameResponse
                {
                    Id = existing.Id,
                    PlayerName = existing.PlayerName,
                    CreatedAt = existing.CreatedAt
                });
            }

            var userPlayerName = new UserPlayerName
            {
                UserId = user.Id,
                PlayerName = request.PlayerName,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserPlayerNames.Add(userPlayerName);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPlayerNames), new UserPlayerNameResponse
            {
                Id = userPlayerName.Id,
                PlayerName = userPlayerName.PlayerName,
                CreatedAt = userPlayerName.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding player name");
            return StatusCode(500, new { message = "Error adding player name" });
        }
    }

    [HttpDelete("player-names/{id}")]
    [Authorize]
    public async Task<IActionResult> RemovePlayerName(int id)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var playerName = await _context.UserPlayerNames
                .FirstOrDefaultAsync(upn => upn.Id == id && upn.UserId == user.Id);

            if (playerName == null)
                return NotFound(new { message = "Player name not found" });

            _context.UserPlayerNames.Remove(playerName);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player name");
            return StatusCode(500, new { message = "Error removing player name" });
        }
    }

    [HttpGet("favorite-servers")]
    [Authorize]
    public async Task<IActionResult> GetFavoriteServers()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var favoriteServers = await _context.UserFavoriteServers
                .Include(ufs => ufs.Server)
                .Where(ufs => ufs.UserId == user.Id)
                .OrderBy(ufs => ufs.CreatedAt)
                .ToListAsync();

            var enrichedFavoriteServers = await Task.WhenAll(favoriteServers
                .Select(async fs => await EnrichFavoriteServerInfoAsync(fs)));

            return Ok(enrichedFavoriteServers.ToList());
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving favorite servers");
            return StatusCode(500, new { message = "Error retrieving favorite servers" });
        }
    }

    [HttpPost("favorite-servers")]
    [Authorize]
    public async Task<IActionResult> AddFavoriteServer([FromBody] AddFavoriteServerRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            if (string.IsNullOrWhiteSpace(request.ServerGuid))
                return BadRequest(new { message = "Server GUID is required" });

            var server = await _context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
            if (server == null)
                return BadRequest(new { message = "Server not found" });

            var existing = await _context.UserFavoriteServers
                .Include(ufs => ufs.Server)
                .FirstOrDefaultAsync(ufs => ufs.UserId == user.Id && ufs.ServerGuid == request.ServerGuid);

            if (existing != null)
            {
                return Ok(await EnrichFavoriteServerInfoAsync(existing));
            }

            var userFavoriteServer = new UserFavoriteServer
            {
                UserId = user.Id,
                ServerGuid = request.ServerGuid,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserFavoriteServers.Add(userFavoriteServer);
            await _context.SaveChangesAsync();

            userFavoriteServer.Server = server;

            return CreatedAtAction(nameof(GetFavoriteServers), await EnrichFavoriteServerInfoAsync(userFavoriteServer));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite server");
            return StatusCode(500, new { message = "Error adding favorite server" });
        }
    }

    [HttpDelete("favorite-servers/{id}")]
    [Authorize]
    public async Task<IActionResult> RemoveFavoriteServer(int id)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var favoriteServer = await _context.UserFavoriteServers
                .FirstOrDefaultAsync(ufs => ufs.Id == id && ufs.UserId == user.Id);

            if (favoriteServer == null)
                return NotFound(new { message = "Favorite server not found" });

            _context.UserFavoriteServers.Remove(favoriteServer);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite server");
            return StatusCode(500, new { message = "Error removing favorite server" });
        }
    }

    [HttpGet("buddies")]
    [Authorize]
    public async Task<IActionResult> GetBuddies()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var userBuddies = await _context.UserBuddies
                .Include(ub => ub.Player)
                .Where(ub => ub.UserId == user.Id)
                .OrderBy(ub => ub.CreatedAt)
                .ToListAsync();

            var buddies = (await Task.WhenAll(userBuddies.Select(async ub => new UserBuddyResponse
            {
                Id = ub.Id,
                BuddyPlayerName = ub.BuddyPlayerName,
                CreatedAt = ub.CreatedAt,
                Player = ub.Player != null ? await EnrichPlayerInfoAsync(ub.Player) : null
            }))).ToList();

            return Ok(buddies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving buddies");
            return StatusCode(500, new { message = "Error retrieving buddies" });
        }
    }

    [HttpPost("buddies")]
    [Authorize]
    public async Task<IActionResult> AddBuddy([FromBody] AddBuddyRequest request)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            if (string.IsNullOrWhiteSpace(request.BuddyPlayerName))
                return BadRequest(new { message = "Buddy player name is required" });

            var existing = await _context.UserBuddies
                .FirstOrDefaultAsync(ub => ub.UserId == user.Id && ub.BuddyPlayerName == request.BuddyPlayerName);

            if (existing != null)
            {
                return Ok(new UserBuddyResponse
                {
                    Id = existing.Id,
                    BuddyPlayerName = existing.BuddyPlayerName,
                    CreatedAt = existing.CreatedAt
                });
            }

            var userBuddy = new UserBuddy
            {
                UserId = user.Id,
                BuddyPlayerName = request.BuddyPlayerName,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserBuddies.Add(userBuddy);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetBuddies), new UserBuddyResponse
            {
                Id = userBuddy.Id,
                BuddyPlayerName = userBuddy.BuddyPlayerName,
                CreatedAt = userBuddy.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding buddy");
            return StatusCode(500, new { message = "Error adding buddy" });
        }
    }

    [HttpDelete("buddies/{id}")]
    [Authorize]
    public async Task<IActionResult> RemoveBuddy(int id)
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var buddy = await _context.UserBuddies
                .FirstOrDefaultAsync(ub => ub.Id == id && ub.UserId == user.Id);

            if (buddy == null)
                return NotFound(new { message = "Buddy not found" });

            _context.UserBuddies.Remove(buddy);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing buddy");
            return StatusCode(500, new { message = "Error removing buddy" });
        }
    }

    [HttpGet("dashboard")]
    [Authorize]
    public async Task<IActionResult> GetDashboard()
    {
        try
        {
            var user = await GetCurrentUserAsync();
            if (user == null)
                return StatusCode(500, new { message = "User not found" });

            var now = DateTime.UtcNow;
            var activeThreshold = now.AddMinutes(-5);

            // Get online buddies
            var onlineBuddies = await _context.UserBuddies
                .Where(ub => ub.UserId == user.Id)
                .Join(_context.PlayerSessions.Include(ps => ps.Server),
                      ub => ub.BuddyPlayerName,
                      ps => ps.PlayerName,
                      (ub, ps) => ps)
                .Where(ps => ps.IsActive && ps.LastSeenTime >= activeThreshold)
                .OrderByDescending(ps => ps.LastSeenTime)
                .Select(session => new OnlineBuddyResponse
                {
                    PlayerName = session.PlayerName,
                    ServerName = session.Server.Name,
                    ServerGuid = session.ServerGuid,
                    CurrentMap = session.MapName,
                    JoinLink = session.Server.JoinLink,
                    SessionDurationMinutes = (int)(now - session.StartTime).TotalMinutes,
                    CurrentScore = session.TotalScore,
                    CurrentKills = session.TotalKills,
                    CurrentDeaths = session.TotalDeaths,
                    JoinedAt = session.StartTime
                })
                .ToListAsync();

            // Get offline buddies
            var onlineBuddyNames = onlineBuddies.Select(ob => ob.PlayerName).ToHashSet();
            var offlineBuddies = await _context.UserBuddies
                .Include(ub => ub.Player)
                .Where(ub => ub.UserId == user.Id && !onlineBuddyNames.Contains(ub.BuddyPlayerName))
                .Where(ub => ub.Player != null)
                .OrderBy(ub => ub.BuddyPlayerName)
                .Select(ub => new OfflineBuddyResponse
                {
                    PlayerName = ub.BuddyPlayerName,
                    LastSeen = ub.Player.LastSeen,
                    LastSeenIso = ub.Player.LastSeen.ToString("O"),
                    TotalPlayTimeMinutes = ub.Player.TotalPlayTimeMinutes,
                    AddedAt = ub.CreatedAt
                })
                .ToListAsync();

            // Get favorite server statuses
            var favoriteServers = await _context.UserFavoriteServers
                .Include(fs => fs.Server)
                .Where(fs => fs.UserId == user.Id)
                .Select(fs => new FavoriteServerStatusResponse
                {
                    Id = fs.Id,
                    ServerGuid = fs.ServerGuid,
                    ServerName = fs.Server.Name,
                    CurrentPlayers = _context.PlayerSessions
                        .Count(ps => ps.ServerGuid == fs.ServerGuid && ps.IsActive && ps.LastSeenTime >= activeThreshold),
                    MaxPlayers = fs.Server.MaxPlayers,
                    CurrentMap = fs.Server.MapName,
                    JoinLink = fs.Server.JoinLink
                })
                .ToListAsync();

            return Ok(new DashboardResponse
            {
                OnlineBuddies = onlineBuddies,
                OfflineBuddies = offlineBuddies,
                FavoriteServers = favoriteServers
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving dashboard data");
            return StatusCode(500, new { message = "Error retrieving dashboard data" });
        }
    }

    private async Task<User> CreateOrUpdateUserAsync(string email, string name)
    {
        var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
        var now = DateTime.UtcNow;

        if (user == null)
        {
            user = new User
            {
                Email = email,
                CreatedAt = now,
                LastLoggedIn = now,
                IsActive = true
            };
            _context.Users.Add(user);
            _logger.LogInformation("Creating new user with email: {Email}", email);
        }
        else
        {
            user.LastLoggedIn = now;
            user.IsActive = true;
            _logger.LogDebug("Updating last login for user: {Email}", email);
        }

        await _context.SaveChangesAsync();
        return user;
    }

    private string GetClientIpAddress()
    {
        var forwardedFor = Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrEmpty(forwardedFor))
        {
            return forwardedFor.Split(',')[0].Trim();
        }

        var realIp = Request.Headers["X-Real-IP"].FirstOrDefault();
        if (!string.IsNullOrEmpty(realIp))
        {
            return realIp;
        }

        return Request.HttpContext.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    private async Task<PlayerInfoResponse> EnrichPlayerInfoAsync(Player player)
    {
        var now = DateTime.UtcNow;
        var activeThreshold = now.AddMinutes(-5);

        var activeSession = await _context.PlayerSessions
            .Include(ps => ps.Server)
            .Where(ps => ps.PlayerName == player.Name &&
                         ps.IsActive &&
                         ps.LastSeenTime >= activeThreshold)
            .OrderByDescending(ps => ps.LastSeenTime)
            .FirstOrDefaultAsync();

        var isOnline = activeSession != null;
        var currentServer = isOnline ? activeSession!.Server.Name : null;

        return new PlayerInfoResponse
        {
            Name = player.Name,
            FirstSeen = player.FirstSeen,
            LastSeen = player.LastSeen,
            TotalPlayTimeMinutes = player.TotalPlayTimeMinutes,
            AiBot = player.AiBot,
            IsOnline = isOnline,
            LastSeenIso = player.LastSeen.ToString("O"),
            CurrentServer = currentServer,
            CurrentMap = isOnline ? activeSession!.MapName : null,
            CurrentSessionScore = isOnline ? activeSession!.TotalScore : null,
            CurrentSessionKills = isOnline ? activeSession!.TotalKills : null,
            CurrentSessionDeaths = isOnline ? activeSession!.TotalDeaths : null
        };
    }

    private async Task<UserFavoriteServerResponse> EnrichFavoriteServerInfoAsync(UserFavoriteServer favoriteServer)
    {
        var now = DateTime.UtcNow;
        var activeThreshold = now.AddMinutes(-5);

        var activeSessions = await _context.PlayerSessions
            .Include(ps => ps.Player)
            .Where(ps => ps.ServerGuid == favoriteServer.ServerGuid &&
                         ps.IsActive &&
                         ps.Player.AiBot == false &&
                         ps.LastSeenTime >= activeThreshold)
            .OrderByDescending(ps => ps.LastSeenTime)
            .ToListAsync();

        var activeSessionsCount = activeSessions.Count;

        return new UserFavoriteServerResponse
        {
            Id = favoriteServer.Id,
            ServerGuid = favoriteServer.ServerGuid,
            ServerName = favoriteServer.Server.Name,
            CreatedAt = favoriteServer.CreatedAt,
            ActiveSessions = activeSessionsCount,
            CurrentMap = favoriteServer.Server.MapName,
            MaxPlayers = favoriteServer.Server.MaxPlayers,
            JoinLink = favoriteServer.Server.JoinLink
        };
    }
}

// Simple request/response models
public class LoginRequest
{
    public string GoogleIdToken { get; set; } = "";
}

public class LoginResponse
{
    public UserDto User { get; set; } = new();
    public string AccessToken { get; set; } = "";
    public DateTime ExpiresAt { get; set; }
}

public class UserDto
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public string Name { get; set; } = "";
}

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

// Request/Response models for user management
public class AddPlayerNameRequest
{
    public string PlayerName { get; set; } = "";
}

public class UserPlayerNameResponse
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public PlayerInfoResponse? Player { get; set; }
}

public class AddFavoriteServerRequest
{
    public string ServerGuid { get; set; } = "";
}

public class UserFavoriteServerResponse
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public int ActiveSessions { get; set; }
    public string? CurrentMap { get; set; }
    public int? MaxPlayers { get; set; }
    public string? JoinLink { get; set; }
}

public class AddBuddyRequest
{
    public string BuddyPlayerName { get; set; } = "";
}

public class UserBuddyResponse
{
    public int Id { get; set; }
    public string BuddyPlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public PlayerInfoResponse? Player { get; set; }
}

public class PlayerInfoResponse
{
    public string Name { get; set; } = "";
    public DateTime FirstSeen { get; set; }
    public DateTime LastSeen { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public bool AiBot { get; set; }
    public bool IsOnline { get; set; }
    public string LastSeenIso { get; set; } = "";
    public string? CurrentServer { get; set; }
    public string? CurrentMap { get; set; }
    public int? CurrentSessionScore { get; set; }
    public int? CurrentSessionKills { get; set; }
    public int? CurrentSessionDeaths { get; set; }
}

public class DashboardResponse
{
    public List<OnlineBuddyResponse> OnlineBuddies { get; set; } = [];
    public List<OfflineBuddyResponse> OfflineBuddies { get; set; } = [];
    public List<FavoriteServerStatusResponse> FavoriteServers { get; set; } = [];
}

public class OnlineBuddyResponse
{
    public string PlayerName { get; set; } = "";
    public string ServerName { get; set; } = "";
    public string ServerGuid { get; set; } = "";
    public string? CurrentMap { get; set; }
    public string? JoinLink { get; set; }
    public int SessionDurationMinutes { get; set; }
    public int CurrentScore { get; set; }
    public int CurrentKills { get; set; }
    public int CurrentDeaths { get; set; }
    public DateTime JoinedAt { get; set; }
}

public class OfflineBuddyResponse
{
    public string PlayerName { get; set; } = "";
    public DateTime LastSeen { get; set; }
    public string LastSeenIso { get; set; } = "";
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime AddedAt { get; set; }
}

public class FavoriteServerStatusResponse
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public int CurrentPlayers { get; set; }
    public int? MaxPlayers { get; set; }
    public string? CurrentMap { get; set; }
    public string? JoinLink { get; set; }
}