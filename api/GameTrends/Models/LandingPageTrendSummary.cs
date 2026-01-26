using api.Analytics.Models;

namespace api.GameTrends.Models;

/// <summary>
/// Optimized summary for landing page display
/// </summary>
public class LandingPageTrendSummary
{
    public SmartPredictionInsights Insights { get; set; } = new();
    public DateTime GeneratedAt { get; set; }
}
