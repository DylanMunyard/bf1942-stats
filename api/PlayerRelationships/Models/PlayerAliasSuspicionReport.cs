namespace api.PlayerRelationships.Models;

/// <summary>
/// Comprehensive player alias/similarity detection report.
/// Combines statistical, behavioral, network, and temporal analysis.
/// </summary>
public class PlayerAliasSuspicionReport
{
    public required string Player1 { get; set; }
    public required string Player2 { get; set; }

    /// <summary>
    /// Overall similarity score (0-1). Higher = more similar.
    /// </summary>
    public double OverallSimilarityScore { get; set; }

    /// <summary>
    /// Classification of suspicion level.
    /// </summary>
    public AliasSuspicionLevel SuspicionLevel { get; set; }

    /// <summary>
    /// Individual similarity analyses by dimension.
    /// </summary>
    public required StatSimilarityAnalysis StatAnalysis { get; set; }
    public required BehavioralAnalysis BehavioralAnalysis { get; set; }
    public required NetworkAnalysis NetworkAnalysis { get; set; }
    public required TemporalAnalysis TemporalAnalysis { get; set; }

    /// <summary>
    /// Suspicious patterns indicating same player.
    /// </summary>
    public required List<string> RedFlags { get; set; }

    /// <summary>
    /// Patterns indicating they're different players.
    /// </summary>
    public required List<string> GreenFlags { get; set; }

    /// <summary>
    /// Activity timeline showing account switchover patterns.
    /// </summary>
    public ActivityTimeline? ActivityTimeline { get; set; }

    /// <summary>
    /// When the analysis was performed.
    /// </summary>
    public DateTime AnalysisTimestamp { get; set; }

    /// <summary>
    /// Number of days of data included in analysis.
    /// </summary>
    public int DaysAnalyzed { get; set; }

    /// <summary>
    /// Confidence in the analysis (0-1). Based on data volume.
    /// </summary>
    public double AnalysisConfidence { get; set; }
}

/// <summary>
/// Classification of alias suspicion level.
/// </summary>
public enum AliasSuspicionLevel
{
    /// <summary>
    /// Score < 0.50 - Very unlikely to be same player
    /// </summary>
    Unrelated,

    /// <summary>
    /// Score 0.50-0.70 - Could be related, worth investigating
    /// </summary>
    Potential,

    /// <summary>
    /// Score 0.70-0.85 - Probably the same person
    /// </summary>
    Likely,

    /// <summary>
    /// Score > 0.85 - Almost certainly the same person
    /// </summary>
    VeryLikely
}

/// <summary>
/// Statistical performance similarity analysis.
/// Weight: 30%
/// </summary>
public record StatSimilarityAnalysis(
    double Score,
    double KdRatioDifference,
    double KillRateDifference,
    double ScorePerRoundDifference,
    double MapPerformanceSimilarity,
    double ServerPerformanceSimilarity,
    string Analysis,
    bool HasSufficientData = true
);

/// <summary>
/// Behavioral pattern similarity analysis.
/// Weight: 20%
/// </summary>
public record BehavioralAnalysis(
    double Score,
    double PlayTimeOverlapScore,      // 0-1, hour-of-day similarity
    double ServerAffinityScore,        // 0-1, % of same servers
    double PingConsistencyScore,       // 0-1, ping variance on same servers
    double SessionPatternScore,        // 0-1, session frequency/duration similarity
    string Analysis,
    bool HasSufficientData = true
);

/// <summary>
/// Network relationship similarity analysis (Neo4j).
/// Weight: 25%
/// </summary>
public record NetworkAnalysis(
    double Score,
    int SharedTeammateCount,
    double TeammateOverlapPercentage,  // 0-1, Jaccard similarity
    double MutualConnectionScore,
    bool HasDirectConnection,          // Should be false if aliases
    double NetworkShapeSimilarity,
    string Analysis,
    bool HasSufficientData = true
);

/// <summary>
/// Temporal consistency analysis.
/// Weight: 15%
/// </summary>
public record TemporalAnalysis(
    double Score,
    int TemporalOverlapMinutes,        // Should be 0 or near-0 for aliases
    bool SignificantTemporalOverlap,   // Red flag if true
    double InvertedActivityScore,      // Do they play at opposite times?
    double ActivityGapConsistency,     // Similar gaps between sessions
    string Analysis,
    bool HasSufficientData = true
);

/// <summary>
/// Response for batch alias comparison.
/// </summary>
public class PlayerAliasBatchReport
{
    public required string TargetPlayer { get; set; }
    public required List<PlayerAliasSuspicionReport> Comparisons { get; set; }

    /// <summary>
    /// Sorted by suspicion score (highest first).
    /// </summary>
    public required List<PlayerAliasSuspicionReport> TopSuspects { get; set; }
}
