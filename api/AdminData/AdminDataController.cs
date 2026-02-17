using api.AdminData.Models;
using api.Authorization;
using api.PlayerRelationships;
using api.PlayerTracking;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace api.AdminData;

[ApiController]
[Route("stats/admin/data")]
[Authorize(Policy = "Support")]
public class AdminDataController(
    IAdminDataService adminDataService, 
    PlayerTrackerDbContext dbContext,
    PlayerRelationshipEtlService? relationshipEtlService = null,
    Neo4jMigrationService? neo4jMigrationService = null) : ControllerBase
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

    /// <summary>Get a key-value entry from app_data (e.g. site_notice for the notice banner).</summary>
    [HttpGet("app-data/{key}")]
    public async Task<ActionResult<AppDataRow>> GetAppData(string key)
    {
        var row = await adminDataService.GetAppDataAsync(key);
        if (row == null)
            return NotFound();
        return Ok(row);
    }

    /// <summary>Upsert a key-value entry. Body: { "value": "..." }.</summary>
    [HttpPut("app-data/{key}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> SetAppData(string key, [FromBody] SetAppDataRequest? request)
    {
        if (request == null)
            return BadRequest("body with value is required");
        await adminDataService.SetAppDataAsync(key, request.Value ?? "");
        return NoContent();
    }

    /// <summary>Delete a key-value entry.</summary>
    [HttpDelete("app-data/{key}")]
    [Authorize(Policy = "Admin")]
    public async Task<IActionResult> DeleteAppData(string key)
    {
        await adminDataService.DeleteAppDataAsync(key);
        return NoContent();
    }

    /// <summary>
    /// Get Neo4j migration status.
    /// Shows which migrations are applied and which are pending.
    /// </summary>
    [HttpGet("neo4j/migrations")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<MigrationStatus>> GetNeo4jMigrationStatus()
    {
        if (neo4jMigrationService == null)
        {
            return BadRequest(new
            {
                error = "Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json."
            });
        }

        var status = await neo4jMigrationService.GetStatusAsync();
        return Ok(status);
    }

    /// <summary>
    /// Manually run Neo4j migrations.
    /// Normally migrations run on startup, but this allows re-running if needed.
    /// </summary>
    [HttpPost("neo4j/migrations/run")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult> RunNeo4jMigrations()
    {
        if (neo4jMigrationService == null)
        {
            return BadRequest(new
            {
                error = "Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json."
            });
        }

        try
        {
            await neo4jMigrationService.MigrateAsync();
            var status = await neo4jMigrationService.GetStatusAsync();
            return Ok(new
            {
                message = "Migrations completed successfully",
                status
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new
            {
                error = $"Migration failed: {ex.Message}"
            });
        }
    }

    /// <summary>
    /// Sync player relationships to Neo4j for the last N days.
    /// This detects co-play sessions and updates the graph database.
    /// </summary>
    [HttpPost("neo4j/sync")]
    [Authorize(Policy = "Admin")]
    public async Task<ActionResult<Neo4jSyncResponse>> SyncPlayerRelationships(
        [FromBody] Neo4jSyncRequest? request)
    {
        if (relationshipEtlService == null)
        {
            return BadRequest(new Neo4jSyncResponse
            {
                Success = false,
                ErrorMessage = "Neo4j integration is not enabled. Configure Neo4j settings in appsettings.json."
            });
        }

        var days = request?.Days ?? 7;
        if (days < 1 || days > 365)
        {
            return BadRequest(new Neo4jSyncResponse
            {
                Success = false,
                ErrorMessage = "Days must be between 1 and 365"
            });
        }

        var toTimestamp = DateTime.UtcNow;
        var fromTimestamp = toTimestamp.AddDays(-days);

        try
        {
            // Sync player-player relationships
            var relationshipResult = await relationshipEtlService.SyncRelationshipsAsync(
                fromTimestamp,
                toTimestamp);

            // Sync player-server relationships
            await relationshipEtlService.SyncPlayerServerRelationshipsAsync(
                fromTimestamp,
                toTimestamp);

            return Ok(new Neo4jSyncResponse
            {
                Success = true,
                RelationshipsProcessed = relationshipResult.RelationshipsProcessed,
                RoundsProcessed = relationshipResult.RoundsProcessed,
                DurationMs = (int)relationshipResult.Duration.TotalMilliseconds,
                FromDate = fromTimestamp,
                ToDate = toTimestamp
            });
        }
        catch (Exception ex)
        {
            return StatusCode(500, new Neo4jSyncResponse
            {
                Success = false,
                ErrorMessage = $"Sync failed: {ex.Message}"
            });
        }
    }
}

public class SetAppDataRequest
{
    public string? Value { get; set; }
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

public class Neo4jSyncRequest
{
    /// <summary>Number of days to sync (default: 7, max: 365)</summary>
    public int Days { get; set; } = 7;
}

public class Neo4jSyncResponse
{
    public bool Success { get; set; }
    public int RelationshipsProcessed { get; set; }
    public int RoundsProcessed { get; set; }
    public int DurationMs { get; set; }
    public DateTime? FromDate { get; set; }
    public DateTime? ToDate { get; set; }
    public string? ErrorMessage { get; set; }
}
