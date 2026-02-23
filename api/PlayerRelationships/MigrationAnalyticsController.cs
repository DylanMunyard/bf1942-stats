using api.PlayerRelationships.Models;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;

namespace api.PlayerRelationships;

[ApiController]
[Route("stats/analytics/migrations")]
public class MigrationAnalyticsController(
    IPlayerRelationshipService relationshipService,
    ILogger<MigrationAnalyticsController> logger) : ControllerBase
{
    /// <summary>
    /// Get player migration flow data for visualization.
    /// </summary>
    [HttpGet("flow")]
    public async Task<ActionResult<PlayerMigrationFlow>> GetMigrationFlow(
        [FromQuery] DateTime? startDate = null,
        [FromQuery] DateTime? endDate = null,
        [FromQuery] string? game = null)
    {
        try
        {
            var end = endDate ?? DateTime.UtcNow;
            var start = startDate ?? end.AddDays(-30);
            
            var flow = await relationshipService.GetPlayerMigrationFlowAsync(start, end, game);
            return Ok(flow);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting migration flow");
            return StatusCode(500, "An error occurred while analyzing migrations");
        }
    }

    /// <summary>
    /// Get server lifecycle analysis.
    /// </summary>
    [HttpGet("server-lifecycle")]
    public async Task<ActionResult<List<ServerNode>>> GetServerLifecycle(
        [FromQuery] int daysBack = 90)
    {
        try
        {
            var analysis = await relationshipService.GetServerLifecycleAnalysisAsync(daysBack);
            return Ok(analysis);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting server lifecycle analysis");
            return StatusCode(500, "An error occurred while analyzing server lifecycle");
        }
    }
}