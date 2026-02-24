namespace api.PlayerRelationships.Models;

/// <summary>
/// Configuration for alias detection weights.
/// Users can customize how much each dimension contributes to the final score.
/// </summary>
public class AliasDetectionWeights
{
    /// <summary>
    /// Weight for statistical similarity (K/D, kills, map dominance).
    /// Default: 0.25
    /// </summary>
    public double StatWeight { get; set; } = 0.25;

    /// <summary>
    /// Weight for behavioral patterns (play times, servers, ping).
    /// Default: 0.15
    /// </summary>
    public double BehavioralWeight { get; set; } = 0.15;

    /// <summary>
    /// Weight for network similarity (teammates, mutual connections).
    /// Default: 0.20
    /// </summary>
    public double NetworkWeight { get; set; } = 0.20;

    /// <summary>
    /// Weight for temporal consistency (co-sessions, inverted activity).
    /// Default: 0.10
    /// </summary>
    public double TemporalWeight { get; set; } = 0.10;

    /// <summary>
    /// Weight for account switchover patterns (most powerful signal).
    /// Default: 0.30
    /// </summary>
    public double SwitchoverWeight { get; set; } = 0.30;

    /// <summary>
    /// Validate that weights sum to approximately 1.0 (allowing 0.01 tolerance).
    /// </summary>
    public bool IsValid()
    {
        var sum = StatWeight + BehavioralWeight + NetworkWeight + TemporalWeight + SwitchoverWeight;
        return Math.Abs(sum - 1.0) < 0.01;
    }

    /// <summary>
    /// Normalize weights to sum to 1.0.
    /// </summary>
    public void Normalize()
    {
        var sum = StatWeight + BehavioralWeight + NetworkWeight + TemporalWeight + SwitchoverWeight;
        if (sum == 0) return;

        StatWeight /= sum;
        BehavioralWeight /= sum;
        NetworkWeight /= sum;
        TemporalWeight /= sum;
        SwitchoverWeight /= sum;
    }

    /// <summary>
    /// Get default weights.
    /// </summary>
    public static AliasDetectionWeights CreateDefaults()
    {
        return new AliasDetectionWeights
        {
            StatWeight = 0.25,
            BehavioralWeight = 0.15,
            NetworkWeight = 0.20,
            TemporalWeight = 0.10,
            SwitchoverWeight = 0.30
        };
    }
}

/// <summary>
/// Query parameters for alias detection comparison.
/// </summary>
public class ComparisonRequest
{
    public required string Player1 { get; set; }
    public required string Player2 { get; set; }
    public int LookBackDays { get; set; } = 90;

    /// <summary>
    /// Custom weights for the comparison (optional).
    /// If not provided, defaults are used.
    /// </summary>
    public AliasDetectionWeights? CustomWeights { get; set; }
}
