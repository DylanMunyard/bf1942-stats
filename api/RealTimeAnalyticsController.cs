using Microsoft.AspNetCore.Mvc;
using api.ClickHouse;
using api.ClickHouse.Models;

namespace api;

[ApiController]
[Route("stats/[controller]")]
public class RealTimeAnalyticsController(RealTimeAnalyticsService analyticsService) : ControllerBase
{

    [HttpGet("teamkillers")]
    public async Task<ActionResult<List<TeamKillerMetrics>>> GetTeamkillers()
    {
        try
        {
            var teamkillers = await analyticsService.MonitorTeamkillers();
            return Ok(teamkillers);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving teamkiller data: {ex.Message}");
        }
    }
}
