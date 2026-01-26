using api.AdminData.Models;
using api.Authorization;
using api.PlayerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.AdminData;

[ApiController]
[Route("stats/admin/data")]
[Authorize(Policy = "Support")]
public class AdminDataController(IAdminDataService adminDataService, PlayerTrackerDbContext dbContext) : ControllerBase
{
    [HttpPost("sessions/query")]
    public async Task<ActionResult<PagedResult<SuspiciousSessionResponse>>> QuerySuspiciousSessions(
        [FromBody] QuerySuspiciousSessionsRequest request)
    {
        var result = await adminDataService.QuerySuspiciousSessionsAsync(request);
        return Ok(result);
    }

    [HttpGet("rounds/{roundId}")]
    public async Task<ActionResult<RoundDetailResponse>> GetRoundDetail(string roundId)
    {
        var result = await adminDataService.GetRoundDetailAsync(roundId);
        if (result == null)
        {
            return NotFound($"Round {roundId} not found");
        }
        return Ok(result);
    }

    [HttpDelete("rounds/{roundId}")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<DeleteRoundResponse>> DeleteRound(string roundId)
    {
        var adminEmail = User.Claims
            .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(adminEmail))
        {
            return Unauthorized("Admin email not found in token");
        }

        try
        {
            var result = await adminDataService.DeleteRoundAsync(roundId, adminEmail);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("rounds/bulk-delete")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<BulkDeleteRoundsResponse>> BulkDeleteRounds([FromBody] BulkDeleteRoundsRequest? request)
    {
        var adminEmail = User.Claims
            .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(adminEmail))
        {
            return Unauthorized("Admin email not found in token");
        }

        if (request?.RoundIds == null || request.RoundIds.Count == 0)
        {
            return BadRequest("roundIds is required and must contain at least one round ID");
        }

        try
        {
            var result = await adminDataService.BulkDeleteRoundsAsync(request.RoundIds, adminEmail);
            return Ok(result);
        }
        catch (ArgumentException ex)
        {
            return BadRequest(ex.Message);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpPost("rounds/{roundId}/undelete")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<UndeleteRoundResponse>> UndeleteRound(string roundId)
    {
        var adminEmail = User.Claims
            .FirstOrDefault(c => c.Type == System.Security.Claims.ClaimTypes.Email)?.Value;

        if (string.IsNullOrEmpty(adminEmail))
        {
            return Unauthorized("Admin email not found in token");
        }

        try
        {
            var result = await adminDataService.UndeleteRoundAsync(roundId, adminEmail);
            return Ok(result);
        }
        catch (InvalidOperationException ex)
        {
            return NotFound(ex.Message);
        }
    }

    [HttpGet("audit-log")]
    public async Task<ActionResult<List<AuditLogEntry>>> GetAuditLog([FromQuery] int limit = 100)
    {
        var logs = await adminDataService.GetAuditLogAsync(limit);
        var entries = logs.Select(l => new AuditLogEntry(
            l.Id,
            l.Action,
            l.TargetType,
            l.TargetId,
            l.Details,
            l.AdminEmail,
            l.Timestamp
        )).ToList();
        return Ok(entries);
    }

    [HttpGet("users")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<List<UserWithRoleResponse>>> GetUsers()
    {
        var users = await dbContext.Users
            .OrderBy(u => u.Email)
            .Select(u => new UserWithRoleResponse(
                u.Id,
                u.Email,
                string.Equals(u.Email, AppRoles.AdminEmail, StringComparison.OrdinalIgnoreCase) ? AppRoles.Admin : (u.Role ?? AppRoles.User)))
            .ToListAsync();
        return Ok(users);
    }

    [HttpPut("users/{userId:int}/role")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> SetUserRole(int userId, [FromBody] SetUserRoleRequest? request)
    {
        if (string.IsNullOrEmpty(request?.Role))
            return BadRequest("role is required");
        if (request.Role != AppRoles.User && request.Role != AppRoles.Support)
            return BadRequest("role must be User or Support");

        var user = await dbContext.Users.FindAsync(userId);
        if (user == null)
            return NotFound("User not found");
        if (string.Equals(user.Email, AppRoles.AdminEmail, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Cannot change the admin user's role");

        user.Role = request.Role;
        await dbContext.SaveChangesAsync();
        return NoContent();
    }
}

public record AuditLogEntry(
    long Id,
    string Action,
    string TargetType,
    string TargetId,
    string? Details,
    string AdminEmail,
    NodaTime.Instant Timestamp
);
