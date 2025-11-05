using System.Text.RegularExpressions;
using Microsoft.Extensions.Configuration;

namespace api.Servers;

public interface IBotDetectionService
{
    bool IsBotPlayer(string playerName, bool apiBotFlag);
}

public class BotDetectionService(IConfiguration configuration) : IBotDetectionService
{
    private readonly Models.BotDetectionConfig _config = configuration.GetSection("BotDetection").Get<Models.BotDetectionConfig>() ?? new Models.BotDetectionConfig();
    private readonly Regex _duplicateNamePattern = InitializeDuplicatePattern(configuration.GetSection("BotDetection").Get<Models.BotDetectionConfig>() ?? new Models.BotDetectionConfig());

    private static Regex InitializeDuplicatePattern(Models.BotDetectionConfig config)
    {
        // Create regex pattern for duplicate detection: name followed by underscore and number
        var escapedNames = config.DefaultPlayerNames.Select(Regex.Escape);
        var pattern = $@"^({string.Join("|", escapedNames)})_\d+$";
        return new Regex(pattern, RegexOptions.Compiled);
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
