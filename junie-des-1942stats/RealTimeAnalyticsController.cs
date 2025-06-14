using Microsoft.AspNetCore.Mvc;
using junie_des_1942stats.ClickHouse;
using junie_des_1942stats.ClickHouse.Models;

namespace junie_des_1942stats;

[ApiController]
[Route("stats/[controller]")]
public class RealTimeAnalyticsController : ControllerBase
{
    private readonly RealTimeAnalyticsService _analyticsService;

    public RealTimeAnalyticsController(RealTimeAnalyticsService analyticsService)
    {
        _analyticsService = analyticsService;
    }

    [HttpGet("teamkillers")]
    public async Task<ActionResult<List<TeamKillerMetrics>>> GetTeamkillers()
    {
        try
        {
            var teamkillers = await _analyticsService.MonitorTeamkillers();
            return Ok(teamkillers);
        }
        catch (Exception ex)
        {
            return StatusCode(500, $"Error retrieving teamkiller data: {ex.Message}");
        }
    }
} 