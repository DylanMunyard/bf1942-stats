using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Top 3 scores per player per time period.
/// Periods: 'this_week', 'last_30_days', 'all_time'.
/// </summary>
public class PlayerBestScore
{
    public required string PlayerName { get; set; }
    public required string Period { get; set; } // 'this_week', 'last_30_days', 'all_time'
    public int Rank { get; set; } // 1, 2, or 3
    public int FinalScore { get; set; }
    public int FinalKills { get; set; }
    public int FinalDeaths { get; set; }
    public required string MapName { get; set; }
    public required string ServerGuid { get; set; }
    public Instant RoundEndTime { get; set; }
    public required string RoundId { get; set; }
}
