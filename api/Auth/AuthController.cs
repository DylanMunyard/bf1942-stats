using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using api.Auth.Models;
using Microsoft.Extensions.Configuration;
using api.PlayerTracking;
using Microsoft.Extensions.Logging;

namespace api.Auth;

[ApiController]
[Route("stats/[controller]")]
public class AuthController(
    PlayerTrackerDbContext context,
    IDiscordAuthService discordAuthService,
    ILogger<AuthController> logger,
    ITokenService tokenService,
    IRefreshTokenService refreshTokenService,
    IConfiguration configuration) : ControllerBase
{

    [HttpPost("login")]
    public async Task<IActionResult> Login([FromBody] LoginRequest request)
    {
        try
        {
            var ipAddress = GetClientIpAddress();
            string email;
            string name;

            if (!string.IsNullOrEmpty(request.DiscordCode) && !string.IsNullOrEmpty(request.RedirectUri))
            {
                var discordPayload = await discordAuthService.ExchangeCodeForUserAsync(request.DiscordCode, request.RedirectUri, ipAddress);
                email = discordPayload.Email;
                name = discordPayload.Username;
            }
            else
            {
                return BadRequest(new { message = "Discord code and redirect URI are required" });
            }

            var user = await CreateOrUpdateUserAsync(email, name);

            var (accessToken, expiresAt) = tokenService.CreateAccessToken(user);
            var (rawRefresh, rtEntity) = await refreshTokenService.CreateAsync(user, ipAddress, Request.Headers.UserAgent.ToString());
            refreshTokenService.SetCookie(Response, rawRefresh, rtEntity.ExpiresAt);

            return Ok(new LoginResponse
            {
                User = new UserDto { Id = user.Id, Email = user.Email, Name = name },
                AccessToken = accessToken,
                ExpiresAt = expiresAt
            });
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogWarning(ex, "Authentication failed");
            return Unauthorized(new { message = "Invalid authentication credentials" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Login error");
            return StatusCode(500, new { message = "Login failed" });
        }
    }

    [HttpPost("refresh")]
    public async Task<IActionResult> Refresh()
    {
        try
        {
            EnforceCsrfForCookieEndpoints();
            var raw = Request.Cookies[configuration["RefreshToken:CookieName"] ?? "rt"];
            if (string.IsNullOrEmpty(raw)) return Unauthorized(new { message = "Missing refresh token" });

            var (token, user) = await refreshTokenService.ValidateAsync(raw);
            var (newRaw, newEntity) = await refreshTokenService.RotateAsync(token, GetClientIpAddress(), Request.Headers.UserAgent.ToString());
            refreshTokenService.SetCookie(Response, newRaw, newEntity.ExpiresAt);

            var (accessToken, expiresAt) = tokenService.CreateAccessToken(user);
            return Ok(new { accessToken, expiresAt });
        }
        catch (UnauthorizedAccessException)
        {
            return Unauthorized(new { message = "Invalid refresh token" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Refresh error");
            return StatusCode(500, new { message = "Refresh failed" });
        }
    }

    [HttpPost("logout")]
    public async Task<IActionResult> Logout()
    {
        try
        {
            EnforceCsrfForCookieEndpoints();
            var raw = Request.Cookies[configuration["RefreshToken:CookieName"] ?? "rt"];
            if (!string.IsNullOrEmpty(raw))
            {
                try
                {
                    var (token, _) = await refreshTokenService.ValidateAsync(raw);
                    await refreshTokenService.RevokeFamilyAsync(token);
                }
                catch { /* ignore */ }
            }
            refreshTokenService.ClearCookie(Response);
            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Logout error");
            return StatusCode(500);
        }
    }

    // Helper method to get current user from JWT claims
    private async Task<User?> GetCurrentUserAsync()
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier) ?? User.FindFirstValue("sub");
        if (string.IsNullOrEmpty(userId) || !int.TryParse(userId, out var id))
            return null;

        return await context.Users.FirstOrDefaultAsync(u => u.Id == id);
    }

    private void EnforceCsrfForCookieEndpoints()
    {
        var origin = Request.Headers["Origin"].FirstOrDefault();
        var referer = Request.Headers["Referer"].FirstOrDefault();
        var allowedOrigin = configuration["Cors:AllowedOrigins"];
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

            var userWithData = await context.Users
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
            logger.LogError(ex, "Error retrieving user profile");
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

            var userPlayerNames = await context.UserPlayerNames
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
            logger.LogError(ex, "Error retrieving player names");
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

            var existing = await context.UserPlayerNames
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

            context.UserPlayerNames.Add(userPlayerName);
            await context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetPlayerNames), new UserPlayerNameResponse
            {
                Id = userPlayerName.Id,
                PlayerName = userPlayerName.PlayerName,
                CreatedAt = userPlayerName.CreatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding player name");
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

            var playerName = await context.UserPlayerNames
                .FirstOrDefaultAsync(upn => upn.Id == id && upn.UserId == user.Id);

            if (playerName == null)
                return NotFound(new { message = "Player name not found" });

            context.UserPlayerNames.Remove(playerName);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing player name");
            return StatusCode(500, new { message = "Error removing player name" });
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

            var server = await context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
            if (server == null)
                return BadRequest(new { message = "Server not found" });

            var existing = await context.UserFavoriteServers
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

            context.UserFavoriteServers.Add(userFavoriteServer);
            await context.SaveChangesAsync();

            userFavoriteServer.Server = server;

            return Ok(await EnrichFavoriteServerInfoAsync(userFavoriteServer));
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding favorite server");
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

            var favoriteServer = await context.UserFavoriteServers
                .FirstOrDefaultAsync(ufs => ufs.Id == id && ufs.UserId == user.Id);

            if (favoriteServer == null)
                return NotFound(new { message = "Favorite server not found" });

            context.UserFavoriteServers.Remove(favoriteServer);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing favorite server");
            return StatusCode(500, new { message = "Error removing favorite server" });
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

            var existing = await context.UserBuddies
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

            context.UserBuddies.Add(userBuddy);
            await context.SaveChangesAsync();

            return Ok(new UserBuddyResponse
            {
                Id = userBuddy.Id,
                BuddyPlayerName = userBuddy.BuddyPlayerName,
                CreatedAt = userBuddy.CreatedAt
            });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error adding buddy");
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

            var buddy = await context.UserBuddies
                .FirstOrDefaultAsync(ub => ub.Id == id && ub.UserId == user.Id);

            if (buddy == null)
                return NotFound(new { message = "Buddy not found" });

            context.UserBuddies.Remove(buddy);
            await context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error removing buddy");
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
            var onlineBuddies = await context.UserBuddies
                .Where(ub => ub.UserId == user.Id)
                .Join(context.PlayerSessions.Include(ps => ps.Server),
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
                    RoundId = session.RoundId,
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
            var offlineBuddies = await context.UserBuddies
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
            var favoriteServers = await context.UserFavoriteServers
                .Include(fs => fs.Server)
                .Where(fs => fs.UserId == user.Id)
                .Select(fs => new FavoriteServerStatusResponse
                {
                    Id = fs.Id,
                    ServerGuid = fs.ServerGuid,
                    ServerName = fs.Server.Name,
                    CurrentPlayers = context.PlayerSessions
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
            logger.LogError(ex, "Error retrieving dashboard data");
            return StatusCode(500, new { message = "Error retrieving dashboard data" });
        }
    }

    private async Task<User> CreateOrUpdateUserAsync(string email, string name)
    {
        var user = await context.Users.FirstOrDefaultAsync(u => u.Email == email);
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
            context.Users.Add(user);
            logger.LogInformation("Creating new user with email: {Email}", email);
        }
        else
        {
            user.LastLoggedIn = now;
            user.IsActive = true;
            logger.LogDebug("Updating last login for user: {Email}", email);
        }

        await context.SaveChangesAsync();
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

        var activeSession = await context.PlayerSessions
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

        var activeSessions = await context.PlayerSessions
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
