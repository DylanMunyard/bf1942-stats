namespace api.ServerStats.Models;

public class BotDetectionConfig
{
    public List<string> DefaultPlayerNames { get; set; } = new()
    {
        "BFPlayer",
        "Player",
        "BFSoldier"
    };

    public List<string> ExclusionList { get; set; } = new();
}
