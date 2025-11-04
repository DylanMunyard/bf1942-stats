using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace junie_des_1942stats.ServerStats;

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

public interface IBotDetectionService
{
    bool IsBotPlayer(string playerName, bool apiBotFlag);
}

public class BotDetectionService : IBotDetectionService
{
    private readonly BotDetectionConfig _config;
    private readonly Regex _duplicateNamePattern;

    public BotDetectionService(IConfiguration configuration)
    {
        _config = configuration.GetSection("BotDetection").Get<BotDetectionConfig>() ?? new BotDetectionConfig();

        // Create regex pattern for duplicate detection: name followed by underscore and number
        var escapedNames = _config.DefaultPlayerNames.Select(Regex.Escape);
        var pattern = $@"^({string.Join("|", escapedNames)})_\d+$";
        _duplicateNamePattern = new Regex(pattern, RegexOptions.Compiled);
    }

    public bool IsBotPlayer(string playerName, bool apiBotFlag)
    {
        // If API says it's a bot, it's a bot
        if (apiBotFlag)
            return true;

        // Check if player is in exclusion list
        if (_config.ExclusionList.Contains(playerName))
            return false;

        // Check for exact match with default names
        if (_config.DefaultPlayerNames.Contains(playerName))
            return true;

        // Check for duplicate collision pattern (e.g., BFPlayer_0, Player_10)
        return _duplicateNamePattern.IsMatch(playerName);
    }
}
