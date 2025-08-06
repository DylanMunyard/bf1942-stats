using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using junie_des_1942stats.PlayerTracking;
using junie_des_1942stats.Services;

using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Controllers;

[ApiController]
[Route("stats/[controller]")]
public class AuthController : ControllerBase
{
    private readonly PlayerTrackerDbContext _context;
    private readonly IJwtService _jwtService;
    private readonly ILogger<AuthController> _logger;

    public AuthController(
        PlayerTrackerDbContext context,
        IJwtService jwtService,
        ILogger<AuthController> logger)
    {
        _context = context;
        _jwtService = jwtService;
        _logger = logger;
    }

    /// <summary>
    /// UPSERT user on authenticated login - creates new user or updates last login time
    /// Uses the authenticated user's email from JWT token
    /// </summary>
    [HttpPost("login")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> LoginUser()
    {
        try
        {
            var userEmail = User.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return BadRequest("Invalid token - no email claim found");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            var now = DateTime.UtcNow;

            if (user == null)
            {
                // Create new user
                user = new User
                {
                    Email = userEmail,
                    CreatedAt = now,
                    LastLoggedIn = now,
                    IsActive = true
                };

                _context.Users.Add(user);
                _logger.LogInformation("Creating new user with email: {Email}", userEmail);
            }
            else
            {
                // Update existing user's last login
                user.LastLoggedIn = now;
                user.IsActive = true; // Reactivate if previously deactivated
                _logger.LogDebug("Updating last login for user: {Email}", userEmail);
            }

            await _context.SaveChangesAsync();

            var token = _jwtService.GenerateToken(user.Id, user.Email);

            return Ok(new LoginResponse
            {
                Token = token,
                User = new UserResponse
                {
                    Id = user.Id,
                    Email = user.Email,
                    CreatedAt = user.CreatedAt,
                    LastLoggedIn = user.LastLoggedIn,
                    IsActive = user.IsActive
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during user login");
            return StatusCode(500, "An error occurred during login");
        }
    }

    /// <summary>
    /// Get user by email (for authenticated requests)
    /// </summary>
    [HttpGet("user/{email}")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> GetUser(string email)
    {
        try
        {
            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == email);
            
            if (user == null)
            {
                return NotFound("User not found");
            }

            return Ok(new UserResponse
            {
                Id = user.Id,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                LastLoggedIn = user.LastLoggedIn,
                IsActive = user.IsActive
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user with email: {Email}", email);
            return StatusCode(500, "An error occurred retrieving user");
        }
    }

    /// <summary>
    /// Get current authenticated user's profile including dashboard settings
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<UserProfileResponse>> GetProfile()
    {
        try
        {
            var userEmail = User.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return BadRequest("Invalid token - no email claim found");
            }

            var user = await _context.Users
                .Include(u => u.PlayerNames)
                .Include(u => u.FavoriteServers)
                    .ThenInclude(fs => fs.Server)
                .Include(u => u.Buddies)
                .FirstOrDefaultAsync(u => u.Email == userEmail);
            
            if (user == null)
            {
                return NotFound("User not found");
            }

            return Ok(new UserProfileResponse
            {
                Id = user.Id,
                Email = user.Email,
                CreatedAt = user.CreatedAt,
                LastLoggedIn = user.LastLoggedIn,
                IsActive = user.IsActive,
                PlayerNames = user.PlayerNames
                    .OrderBy(pn => pn.CreatedAt)
                    .Select(pn => new UserPlayerNameResponse
                    {
                        Id = pn.Id,
                        PlayerName = pn.PlayerName,
                        CreatedAt = pn.CreatedAt
                    })
                    .ToList(),
                FavoriteServers = user.FavoriteServers
                    .OrderBy(fs => fs.CreatedAt)
                    .Select(fs => new UserFavoriteServerResponse
                    {
                        Id = fs.Id,
                        ServerGuid = fs.ServerGuid,
                        ServerName = fs.Server.Name,
                        CreatedAt = fs.CreatedAt
                    })
                    .ToList(),
                Buddies = user.Buddies
                    .OrderBy(b => b.CreatedAt)
                    .Select(b => new UserBuddyResponse
                    {
                        Id = b.Id,
                        BuddyPlayerName = b.BuddyPlayerName,
                        CreatedAt = b.CreatedAt
                    })
                    .ToList()
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving user profile");
            return StatusCode(500, "An error occurred retrieving user profile");
        }
    }

    /// <summary>
    /// Get current user's player names
    /// </summary>
    [HttpGet("player-names")]
    [Authorize]
    public async Task<ActionResult<List<UserPlayerNameResponse>>> GetPlayerNames()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var playerNames = await _context.UserPlayerNames
                .Where(upn => upn.UserId == userId.Value)
                .OrderBy(upn => upn.CreatedAt)
                .Select(upn => new UserPlayerNameResponse
                {
                    Id = upn.Id,
                    PlayerName = upn.PlayerName,
                    CreatedAt = upn.CreatedAt
                })
                .ToListAsync();

            return Ok(playerNames);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving player names");
            return StatusCode(500, "An error occurred retrieving player names");
        }
    }

    /// <summary>
    /// Add a player name to current user's profile
    /// </summary>
    [HttpPost("player-names")]
    [Authorize]
    public async Task<ActionResult<UserPlayerNameResponse>> AddPlayerName([FromBody] AddPlayerNameRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            if (string.IsNullOrWhiteSpace(request.PlayerName))
                return BadRequest("Player name is required");

            // Check if player name already exists for this user
            var existing = await _context.UserPlayerNames
                .FirstOrDefaultAsync(upn => upn.UserId == userId.Value && upn.PlayerName == request.PlayerName);
            
            if (existing != null)
            {
                // Return the existing player name instead of an error
                return Ok(new UserPlayerNameResponse
                {
                    Id = existing.Id,
                    PlayerName = existing.PlayerName,
                    CreatedAt = existing.CreatedAt
                });
            }

            var userPlayerName = new UserPlayerName
            {
                UserId = userId.Value,
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
            return StatusCode(500, "An error occurred adding player name");
        }
    }

    /// <summary>
    /// Remove a player name from current user's profile
    /// </summary>
    [HttpDelete("player-names/{id}")]
    [Authorize]
    public async Task<ActionResult> RemovePlayerName(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var playerName = await _context.UserPlayerNames
                .FirstOrDefaultAsync(upn => upn.Id == id && upn.UserId == userId.Value);

            if (playerName == null)
                return NotFound("Player name not found");

            _context.UserPlayerNames.Remove(playerName);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing player name");
            return StatusCode(500, "An error occurred removing player name");
        }
    }

    /// <summary>
    /// Get current user's favorite servers
    /// </summary>
    [HttpGet("favorite-servers")]
    [Authorize]
    public async Task<ActionResult<List<UserFavoriteServerResponse>>> GetFavoriteServers()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var favoriteServers = await _context.UserFavoriteServers
                .Include(ufs => ufs.Server)
                .Where(ufs => ufs.UserId == userId.Value)
                .OrderBy(ufs => ufs.CreatedAt)
                .Select(ufs => new UserFavoriteServerResponse
                {
                    Id = ufs.Id,
                    ServerGuid = ufs.ServerGuid,
                    ServerName = ufs.Server.Name,
                    CreatedAt = ufs.CreatedAt
                })
                .ToListAsync();

            return Ok(favoriteServers);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving favorite servers");
            return StatusCode(500, "An error occurred retrieving favorite servers");
        }
    }

    /// <summary>
    /// Add a favorite server to current user's profile
    /// </summary>
    [HttpPost("favorite-servers")]
    [Authorize]
    public async Task<ActionResult<UserFavoriteServerResponse>> AddFavoriteServer([FromBody] AddFavoriteServerRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            if (string.IsNullOrWhiteSpace(request.ServerGuid))
                return BadRequest("Server GUID is required");

            // Check if server exists
            var server = await _context.Servers.FirstOrDefaultAsync(s => s.Guid == request.ServerGuid);
            if (server == null)
                return BadRequest("Server not found");

            // Check if server is already in favorites
            var existing = await _context.UserFavoriteServers
                .Include(ufs => ufs.Server)
                .FirstOrDefaultAsync(ufs => ufs.UserId == userId.Value && ufs.ServerGuid == request.ServerGuid);
            
            if (existing != null)
            {
                // Return the existing favorite server instead of an error
                return Ok(new UserFavoriteServerResponse
                {
                    Id = existing.Id,
                    ServerGuid = existing.ServerGuid,
                    ServerName = existing.Server.Name,
                    CreatedAt = existing.CreatedAt
                });
            }

            var userFavoriteServer = new UserFavoriteServer
            {
                UserId = userId.Value,
                ServerGuid = request.ServerGuid,
                CreatedAt = DateTime.UtcNow
            };

            _context.UserFavoriteServers.Add(userFavoriteServer);
            await _context.SaveChangesAsync();

            return CreatedAtAction(nameof(GetFavoriteServers), new UserFavoriteServerResponse
            {
                Id = userFavoriteServer.Id,
                ServerGuid = userFavoriteServer.ServerGuid,
                ServerName = server.Name,
                CreatedAt = userFavoriteServer.CreatedAt
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error adding favorite server");
            return StatusCode(500, "An error occurred adding favorite server");
        }
    }

    /// <summary>
    /// Remove a favorite server from current user's profile
    /// </summary>
    [HttpDelete("favorite-servers/{id}")]
    [Authorize]
    public async Task<ActionResult> RemoveFavoriteServer(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var favoriteServer = await _context.UserFavoriteServers
                .FirstOrDefaultAsync(ufs => ufs.Id == id && ufs.UserId == userId.Value);

            if (favoriteServer == null)
                return NotFound("Favorite server not found");

            _context.UserFavoriteServers.Remove(favoriteServer);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing favorite server");
            return StatusCode(500, "An error occurred removing favorite server");
        }
    }

    /// <summary>
    /// Get current user's buddies
    /// </summary>
    [HttpGet("buddies")]
    [Authorize]
    public async Task<ActionResult<List<UserBuddyResponse>>> GetBuddies()
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var buddies = await _context.UserBuddies
                .Where(ub => ub.UserId == userId.Value)
                .OrderBy(ub => ub.CreatedAt)
                .Select(ub => new UserBuddyResponse
                {
                    Id = ub.Id,
                    BuddyPlayerName = ub.BuddyPlayerName,
                    CreatedAt = ub.CreatedAt
                })
                .ToListAsync();

            return Ok(buddies);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error retrieving buddies");
            return StatusCode(500, "An error occurred retrieving buddies");
        }
    }

    /// <summary>
    /// Add a buddy to current user's profile
    /// </summary>
    [HttpPost("buddies")]
    [Authorize]
    public async Task<ActionResult<UserBuddyResponse>> AddBuddy([FromBody] AddBuddyRequest request)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            if (string.IsNullOrWhiteSpace(request.BuddyPlayerName))
                return BadRequest("Buddy player name is required");

            // Check if buddy already exists for this user
            var existing = await _context.UserBuddies
                .FirstOrDefaultAsync(ub => ub.UserId == userId.Value && ub.BuddyPlayerName == request.BuddyPlayerName);
            
            if (existing != null)
            {
                // Return the existing buddy instead of an error
                return Ok(new UserBuddyResponse
                {
                    Id = existing.Id,
                    BuddyPlayerName = existing.BuddyPlayerName,
                    CreatedAt = existing.CreatedAt
                });
            }

            var userBuddy = new UserBuddy
            {
                UserId = userId.Value,
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
            return StatusCode(500, "An error occurred adding buddy");
        }
    }

    /// <summary>
    /// Remove a buddy from current user's profile
    /// </summary>
    [HttpDelete("buddies/{id}")]
    [Authorize]
    public async Task<ActionResult> RemoveBuddy(int id)
    {
        try
        {
            var userId = GetCurrentUserId();
            if (userId == null) return BadRequest("Invalid token - no user ID found");

            var buddy = await _context.UserBuddies
                .FirstOrDefaultAsync(ub => ub.Id == id && ub.UserId == userId.Value);

            if (buddy == null)
                return NotFound("Buddy not found");

            _context.UserBuddies.Remove(buddy);
            await _context.SaveChangesAsync();

            return NoContent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error removing buddy");
            return StatusCode(500, "An error occurred removing buddy");
        }
    }


    private int? GetCurrentUserId()
    {
        var userIdClaim = User.FindFirst("id")?.Value ?? User.FindFirst("sub")?.Value;
        return int.TryParse(userIdClaim, out var userId) ? userId : null;
    }
}



/// <summary>
/// Response model for user login including JWT token
/// </summary>
public class LoginResponse
{
    public string Token { get; set; } = "";
    public UserResponse User { get; set; } = new();
}

/// <summary>
/// Response model for user data
/// </summary>
public class UserResponse
{
    public int Id { get; set; }
    public string Email { get; set; } = "";
    public DateTime CreatedAt { get; set; }
    public DateTime LastLoggedIn { get; set; }
    public bool IsActive { get; set; }
}

/// <summary>
/// Extended response model for user profile including dashboard settings
/// </summary>
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

/// <summary>
/// Request model for adding a player name
/// </summary>
public class AddPlayerNameRequest
{
    public string PlayerName { get; set; } = "";
}

/// <summary>
/// Response model for user player name
/// </summary>
public class UserPlayerNameResponse
{
    public int Id { get; set; }
    public string PlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request model for adding a favorite server
/// </summary>
public class AddFavoriteServerRequest
{
    public string ServerGuid { get; set; } = "";
}

/// <summary>
/// Response model for user favorite server
/// </summary>
public class UserFavoriteServerResponse
{
    public int Id { get; set; }
    public string ServerGuid { get; set; } = "";
    public string ServerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}

/// <summary>
/// Request model for adding a buddy
/// </summary>
public class AddBuddyRequest
{
    public string BuddyPlayerName { get; set; } = "";
}

/// <summary>
/// Response model for user buddy
/// </summary>
public class UserBuddyResponse
{
    public int Id { get; set; }
    public string BuddyPlayerName { get; set; } = "";
    public DateTime CreatedAt { get; set; }
}