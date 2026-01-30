namespace api.Players.Models;

public class PlayerBestScores
{
    public List<BestScoreDetail> ThisWeek { get; set; } = [];
    public List<BestScoreDetail> Last30Days { get; set; } = [];
    public List<BestScoreDetail> AllTime { get; set; } = [];
}
