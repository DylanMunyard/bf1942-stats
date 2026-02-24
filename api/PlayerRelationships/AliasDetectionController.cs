using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging;
using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

using ActivityTimeline = Models.ActivityTimeline;

/// <summary>
/// API endpoints for player alias/similarity detection.
/// Helps identify players with multiple accounts using statistical, behavioral, and network analysis.
/// </summary>
[ApiController]
[Route("stats/alias-detection")]
public class AliasDetectionController(
    PlayerAliasDetectionService detectionService,
    ILogger<AliasDetectionController> logger) : ControllerBase
{
    /// <summary>
    /// Compare two players for alias/similarity patterns.
    /// Returns comprehensive analysis including stat similarity, behavior patterns, network analysis, and red flags.
    /// Analyzes all historical data to catch dormant accounts and identify patterns regardless of how long they've been offline.
    /// </summary>
    /// <param name="player1">First player name</param>
    /// <param name="player2">Second player name to compare</param>
    /// <param name="lookBackDays">Number of days to analyze. Default 3650 (10 years) for comprehensive historical analysis.</param>
    /// <param name="request">Optional: Custom weights for similarity dimensions</param>
    /// <returns>Detailed suspicion report</returns>
    [HttpGet("compare")]
    public async Task<ActionResult<PlayerAliasSuspicionReport>> ComparePlayersAsync(
        [FromQuery] string player1,
        [FromQuery] string player2,
        [FromQuery] int lookBackDays = 3650,
        [FromBody] AliasDetectionWeights? customWeights = null)
    {
        if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
            return BadRequest("Both player1 and player2 parameters are required");

        if (player1.Equals(player2, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Cannot compare a player with themselves");

        if (lookBackDays < 0)
            lookBackDays = 0;

        try
        {
            var report = await detectionService.ComparePlayers(player1, player2, lookBackDays, customWeights);
            return Ok(report);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error comparing players {Player1} and {Player2}", player1, player2);
            return StatusCode(500, new { error = "An error occurred while analyzing players" });
        }
    }

    /// <summary>
    /// Find potential aliases for a player by comparing against a list of candidates.
    /// Returns top suspects sorted by suspicion score.
    /// </summary>
    /// <param name="targetPlayer">The player to find potential aliases for</param>
    /// <param name="limit">Maximum number of top suspects to return. Default 10, max 50.</param>
    /// <param name="lookBackDays">Number of days to analyze from each player's last activity. Default 3650 (10 years).</param>
    /// <returns>Batch comparison report with top suspects</returns>
    [HttpGet("{targetPlayer}/potential-aliases")]
    public async Task<ActionResult<PlayerAliasBatchReport>> FindPotentialAliasesAsync(
        string targetPlayer,
        [FromQuery] int limit = 10,
        [FromQuery] int lookBackDays = 3650)
    {
        if (string.IsNullOrWhiteSpace(targetPlayer))
            return BadRequest("targetPlayer parameter is required");

        if (limit < 1 || limit > 50)
            limit = 10;

        try
        {
            // TODO: Implement getting a list of candidate players (recently active similar skill levels, etc)
            // For now, this endpoint is a placeholder
            return StatusCode(501, new { error = "Batch comparison not yet implemented" });
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error finding potential aliases for {TargetPlayer}", targetPlayer);
            return StatusCode(500, new { error = "An error occurred while analyzing potential aliases" });
        }
    }

    /// <summary>
    /// Get detailed explanation of a suspicion report.
    /// </summary>
    [HttpPost("explain")]
    public ActionResult<string> ExplainReport([FromBody] PlayerAliasSuspicionReport report)
    {
        if (report == null)
            return BadRequest("Report is required");

        var explanation = GenerateExplanation(report);
        return Ok(explanation);
    }

    /// <summary>
    /// Get default weights used for alias detection.
    /// Can be customized and passed back to the compare endpoint.
    /// </summary>
    [HttpGet("weights")]
    public ActionResult<object> GetDefaultWeights()
    {
        var defaults = AliasDetectionWeights.CreateDefaults();
        return Ok(new
        {
            message = "Default weights for alias detection. Customize and pass to /compare endpoint.",
            weights = defaults,
            description = new
            {
                statWeight = "Statistical similarity (K/D, kills, maps)",
                behavioralWeight = "Behavioral patterns (play times, servers, ping)",
                networkWeight = "Network similarity (teammates, connections)",
                temporalWeight = "Temporal consistency (co-sessions, activity gaps)",
                switchoverWeight = "Account switchover patterns (most powerful signal)"
            },
            usage = "POST /stats/alias-detection/compare with customWeights in body"
        });
    }

    /// <summary>
    /// Get just the activity timeline for two players.
    /// Useful for visualizing account switchover patterns.
    /// </summary>
    [HttpGet("timeline")]
    public async Task<ActionResult<ActivityTimeline>> GetActivityTimeline(
        [FromQuery] string player1,
        [FromQuery] string player2,
        [FromQuery] int lookBackDays = 3650)
    {
        if (string.IsNullOrWhiteSpace(player1) || string.IsNullOrWhiteSpace(player2))
            return BadRequest("Both player1 and player2 parameters are required");

        if (player1.Equals(player2, StringComparison.OrdinalIgnoreCase))
            return BadRequest("Cannot compare a player with themselves");

        try
        {
            var timeline = await detectionService.GetActivityTimelineAsync(player1, player2, lookBackDays);
            return Ok(timeline);
        }
        catch (Exception ex)
        {
            logger.LogError(ex, "Error getting activity timeline for {Player1} and {Player2}", player1, player2);
            return StatusCode(500, new { error = "An error occurred while analyzing activity timeline" });
        }
    }

    private static string GenerateExplanation(PlayerAliasSuspicionReport report)
    {
        var lines = new List<string>
        {
            $"Alias Detection Report: {report.Player1} vs {report.Player2}",
            $"Overall Suspicion Score: {report.OverallSimilarityScore:P0} ({report.SuspicionLevel})",
            $"Analysis Confidence: {report.AnalysisConfidence:P0}",
            "",
            "BREAKDOWN BY DIMENSION:",
            $"• Statistics Similarity: {report.StatAnalysis.Score:P0} - {report.StatAnalysis.Analysis}",
            $"• Behavioral Match: {report.BehavioralAnalysis.Score:P0} - {report.BehavioralAnalysis.Analysis}",
            $"• Network Overlap: {report.NetworkAnalysis.Score:P0} - {report.NetworkAnalysis.Analysis}",
            $"• Temporal Consistency: {report.TemporalAnalysis.Score:P0} - {report.TemporalAnalysis.Analysis}",
            ""
        };

        if (report.RedFlags.Any())
        {
            lines.Add("🚩 RED FLAGS (Suggest same player):");
            foreach (var flag in report.RedFlags)
                lines.Add($"  ⚠️  {flag}");
            lines.Add("");
        }

        if (report.GreenFlags.Any())
        {
            lines.Add("✅ GREEN FLAGS (Suggest different players):");
            foreach (var flag in report.GreenFlags)
                lines.Add($"  ✓ {flag}");
            lines.Add("");
        }

        lines.Add("RECOMMENDATION:");
        lines.Add(report.SuspicionLevel switch
        {
            AliasSuspicionLevel.VeryLikely => "⚠️  VERY LIKELY ALIASES - Strong evidence suggests same player. Consider investigation.",
            AliasSuspicionLevel.Likely => "⚠️  LIKELY ALIASES - Multiple similar patterns detected. Worth investigating.",
            AliasSuspicionLevel.Potential => "❓ POTENTIAL ALIASES - Some similarities found, but inconclusive. Manually review.",
            _ => "✓ PROBABLY DIFFERENT - Insufficient evidence of alias relationship.",
        });

        return string.Join("\n", lines);
    }
}
