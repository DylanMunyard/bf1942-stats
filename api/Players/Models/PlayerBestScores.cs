namespace api.PlayerStats.Models;

// Best scores for different time periods
public class PlayerBestScores
{
    public List<BestScoreDetail> ThisWeek { get; set; } = new();
    public List<BestScoreDetail> Last30Days { get; set; } = new();
    public List<BestScoreDetail> AllTime { get; set; } = new();
}
