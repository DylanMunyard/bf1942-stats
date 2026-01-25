using api.AdminData.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace api.AdminData;

[ApiController]
[Route("stats/admin/data")]
[Authorize(Policy = "Admin")]
public class AdminDataController(IAdminDataService adminDataService) : ControllerBase
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
    public async Task<ActionResult<DeleteRoundResponse>> DeleteRound(string roundId)
    {
        // Get admin email from claims
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
