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
    /// Get current authenticated user's profile
    /// </summary>
    [HttpGet("profile")]
    [Authorize]
    public async Task<ActionResult<UserResponse>> GetProfile()
    {
        try
        {
            var userEmail = User.FindFirst("email")?.Value;
            if (string.IsNullOrEmpty(userEmail))
            {
                return BadRequest("Invalid token - no email claim found");
            }

            var user = await _context.Users.FirstOrDefaultAsync(u => u.Email == userEmail);
            
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
            _logger.LogError(ex, "Error retrieving user profile");
            return StatusCode(500, "An error occurred retrieving user profile");
        }
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