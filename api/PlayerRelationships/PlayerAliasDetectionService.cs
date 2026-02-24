using api.PlayerStats;
using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Comprehensive player alias/similarity detection service.
/// Combines statistical, behavioral, network, and temporal analysis.
/// </summary>
public class PlayerAliasDetectionService(
    ISqlitePlayerStatsService statsService,
    IPlayerRelationshipService relationshipService,
    StatSimilarityCalculator statCalculator,
    BehavioralPatternAnalyzer behavioralAnalyzer,
    Neo4jNetworkAnalyzer networkAnalyzer,
    ActivityTimelineAnalyzer timelineAnalyzer)
{
    /// <summary>
    /// Compare two players for alias/similarity patterns.
    /// Returns comprehensive report with scores and red flags.
    /// </summary>
    public async Task<PlayerAliasSuspicionReport> ComparePlayers(
        string player1,
        string player2,
        int lookBackDays = 90,
        AliasDetectionWeights? customWeights = null)
    {
        var startTime = DateTime.UtcNow;

        // Use custom weights or defaults
        var weights = customWeights ?? AliasDetectionWeights.CreateDefaults();
        if (!weights.IsValid())
            weights.Normalize();

        // Calculate all similarity dimensions
        var statAnalysis = await statCalculator.CalculateSimilarityAsync(player1, player2, lookBackDays);
        var behavioralAnalysis = await behavioralAnalyzer.AnalyzeBehaviorAsync(player1, player2, lookBackDays);
        var (networkAnalysis, temporalAnalysis) = await networkAnalyzer.AnalyzeNetworkAndTemporalAsync(player1, player2, lookBackDays);
        var activityTimeline = await timelineAnalyzer.AnalyzeTimelineAsync(player1, player2, lookBackDays);

        // Calculate overall similarity score using configurable weights
        var overallScore = (statAnalysis.Score * weights.StatWeight) +
                          (behavioralAnalysis.Score * weights.BehavioralWeight) +
                          (networkAnalysis.Score * weights.NetworkWeight) +
                          (temporalAnalysis.Score * weights.TemporalWeight) +
                          (activityTimeline.SwitchoverSuspicionScore * weights.SwitchoverWeight);

        // Determine suspicion level
        var suspicionLevel = overallScore switch
        {
            >= 0.85 => AliasSuspicionLevel.VeryLikely,
            >= 0.70 => AliasSuspicionLevel.Likely,
            >= 0.50 => AliasSuspicionLevel.Potential,
            _ => AliasSuspicionLevel.Unrelated
        };

        // Build red flags and green flags
        var (redFlags, greenFlags) = IdentifyFlags(
            statAnalysis, behavioralAnalysis, networkAnalysis, temporalAnalysis,
            overallScore);

        // Add switchover-specific red flags if detected
        if (activityTimeline.SwitchoverSuspicionScore > 0.40)
        {
            redFlags.Add($"🚩 Suspicious switchover: {activityTimeline.Gap.PatternDescription}");
            if (activityTimeline.Gap.OverlapRatio == 0)
                redFlags.Add("Zero temporal overlap - accounts never played simultaneously");
            if (activityTimeline.Gap.SwitchoverWindowDays <= 3)
                redFlags.Add($"⚠️  TIGHT SWITCHOVER: Only {activityTimeline.Gap.SwitchoverWindowDays} day(s) between accounts");
        }

        // Calculate analysis confidence based on data volume
        var confidence = CalculateConfidence(
            statAnalysis, behavioralAnalysis, networkAnalysis);

        var report = new PlayerAliasSuspicionReport
        {
            Player1 = player1,
            Player2 = player2,
            OverallSimilarityScore = overallScore,
            SuspicionLevel = suspicionLevel,
            StatAnalysis = statAnalysis,
            BehavioralAnalysis = behavioralAnalysis,
            NetworkAnalysis = networkAnalysis,
            TemporalAnalysis = temporalAnalysis,
            ActivityTimeline = activityTimeline,
            RedFlags = redFlags,
            GreenFlags = greenFlags,
            AnalysisTimestamp = DateTime.UtcNow,
            DaysAnalyzed = lookBackDays,
            AnalysisConfidence = confidence
        };

        return report;
    }

    /// <summary>
    /// Get just the activity timeline for two players without full alias detection analysis.
    /// </summary>
    public async Task<ActivityTimeline> GetActivityTimelineAsync(
        string player1,
        string player2,
        int lookBackDays = 180)
    {
        return await timelineAnalyzer.AnalyzeTimelineAsync(player1, player2, lookBackDays);
    }

    /// <summary>
    /// Find potential aliases for a given player by comparing against other players.
    /// Returns top N suspects sorted by suspicion score.
    /// </summary>
    public async Task<PlayerAliasBatchReport> FindPotentialAliasesAsync(
        string targetPlayer,
        List<string> candidatePlayers,
        int lookBackDays = 90,
        int topCount = 10)
    {
        var comparisons = new List<PlayerAliasSuspicionReport>();

        foreach (var candidate in candidatePlayers)
        {
            if (candidate.Equals(targetPlayer, StringComparison.OrdinalIgnoreCase))
                continue;

            try
            {
                var comparison = await ComparePlayers(targetPlayer, candidate, lookBackDays);
                comparisons.Add(comparison);
            }
            catch
            {
                // Skip candidates with data issues
            }
        }

        var topSuspects = comparisons
            .OrderByDescending(c => c.OverallSimilarityScore)
            .Take(topCount)
            .ToList();

        return new PlayerAliasBatchReport
        {
            TargetPlayer = targetPlayer,
            Comparisons = comparisons,
            TopSuspects = topSuspects
        };
    }

    /// <summary>
    /// Identify red flags and green flags indicating whether players are aliases.
    /// </summary>
    private static (List<string> RedFlags, List<string> GreenFlags) IdentifyFlags(
        StatSimilarityAnalysis statAnalysis,
        BehavioralAnalysis behavioralAnalysis,
        NetworkAnalysis networkAnalysis,
        TemporalAnalysis temporalAnalysis,
        double overallScore)
    {
        var redFlags = new List<string>();
        var greenFlags = new List<string>();

        // RED FLAGS (indicate same person)

        if (statAnalysis.KdRatioDifference > 0.85)
            redFlags.Add("K/D ratios nearly identical");

        if (statAnalysis.MapPerformanceSimilarity > 0.80)
            redFlags.Add("Identical map performance patterns");

        if (behavioralAnalysis.PlayTimeOverlapScore > 0.75)
            redFlags.Add("Play at nearly identical times of day");

        if (behavioralAnalysis.ServerAffinityScore > 0.70)
            redFlags.Add("Strong server affinity match");

        if (behavioralAnalysis.PingConsistencyScore > 0.85)
            redFlags.Add("Nearly identical ping on same servers (same location)");

        if (networkAnalysis.TeammateOverlapPercentage > 0.70)
            redFlags.Add("Very high teammate overlap");

        if (!networkAnalysis.HasDirectConnection && networkAnalysis.TeammateOverlapPercentage > 0.60)
            redFlags.Add("High teammate overlap but no direct co-session (classic alias pattern)");

        if (temporalAnalysis.TemporalOverlapMinutes == 0 && networkAnalysis.TeammateOverlapPercentage > 0.50)
            redFlags.Add("Zero temporal overlap with high teammate overlap (likely same person)");

        if (statAnalysis.KillRateDifference > 0.80)
            redFlags.Add("Kill rate patterns nearly identical");

        // NOTE: Switchover pattern red flags are now added by the caller
        // based on ActivityTimeline analysis before IdentifyFlags is called

        // GREEN FLAGS (indicate different people)

        if (temporalAnalysis.SignificantTemporalOverlap)
            greenFlags.Add("Played together in multiple sessions");

        if (behavioralAnalysis.PlayTimeOverlapScore < 0.25)
            greenFlags.Add("Play at significantly different times");

        if (behavioralAnalysis.PingConsistencyScore < 0.30 && behavioralAnalysis.ServerAffinityScore > 0.50)
            greenFlags.Add("Very different pings on same servers (different locations)");

        if (statAnalysis.MapPerformanceSimilarity < 0.40)
            greenFlags.Add("Map-specific performance differs significantly");

        if (networkAnalysis.HasDirectConnection && !redFlags.Any(f => f.Contains("zero temporal")))
            greenFlags.Add("Played together - suggests different accounts");

        if (statAnalysis.KdRatioDifference < 0.30)
            greenFlags.Add("K/D ratios significantly different");

        if (behavioralAnalysis.ServerAffinityScore < 0.30)
            greenFlags.Add("Different server preferences");

        // Conflicting signals
        if (redFlags.Count > 0 && greenFlags.Count > 0)
        {
            if (overallScore < 0.60)
                greenFlags.Add("Multiple contradicting signals suggest they are different players");
            else
                redFlags.Add("Multiple matching signals despite some differences");
        }

        return (redFlags, greenFlags);
    }

    /// <summary>
    /// Calculate confidence in the analysis based on data volume.
    /// </summary>
    private static double CalculateConfidence(
        StatSimilarityAnalysis statAnalysis,
        BehavioralAnalysis behavioralAnalysis,
        NetworkAnalysis networkAnalysis)
    {
        var confidence = 0.5; // Base confidence

        // More data = higher confidence
        if (!statAnalysis.Analysis.Contains("Insufficient"))
            confidence += 0.25;

        if (!behavioralAnalysis.Analysis.Contains("Insufficient"))
            confidence += 0.15;

        if (networkAnalysis.SharedTeammateCount > 5)
            confidence += 0.10;

        return Math.Min(1.0, confidence);
    }
}
