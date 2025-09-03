using junie_des_1942stats.Gamification.Models;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Gamification.Services;

public class AchievementLabelingService
{
    private readonly BadgeDefinitionsService _badgeDefinitionsService;
    private readonly ILogger<AchievementLabelingService> _logger;
    private readonly Dictionary<string, AchievementLabel> _achievementLabels;

    public AchievementLabelingService(
        BadgeDefinitionsService badgeDefinitionsService,
        ILogger<AchievementLabelingService> logger)
    {
        _badgeDefinitionsService = badgeDefinitionsService;
        _logger = logger;
        _achievementLabels = InitializeAchievementLabels();
    }

    /// <summary>
    /// Get labeled achievement information for a list of achievement IDs
    /// </summary>
    public List<AchievementLabel> GetAchievementLabels(List<string> achievementIds)
    {
        var labels = new List<AchievementLabel>();

        foreach (var achievementId in achievementIds)
        {
            if (_achievementLabels.TryGetValue(achievementId, out var label))
            {
                labels.Add(label);
            }
            else
            {
                // Fallback for unknown achievement IDs
                labels.Add(new AchievementLabel
                {
                    AchievementId = achievementId,
                    AchievementType = DetermineAchievementType(achievementId),
                    Tier = DetermineTier(achievementId),
                    Category = DetermineCategory(achievementId),
                    DisplayName = GenerateDisplayName(achievementId)
                });
            }
        }

        return labels;
    }

    /// <summary>
    /// Initialize all achievement labels by deriving from badge definitions
    /// </summary>
    private Dictionary<string, AchievementLabel> InitializeAchievementLabels()
    {
        var labels = new Dictionary<string, AchievementLabel>();

        // Get all badge definitions and convert them to achievement labels
        var allBadges = _badgeDefinitionsService.GetAllBadges();

        foreach (var badge in allBadges)
        {
            labels[badge.Id] = new AchievementLabel
            {
                AchievementId = badge.Id,
                AchievementType = DetermineAchievementTypeFromBadge(badge),
                Tier = badge.Tier,
                Category = badge.Category,
                DisplayName = badge.Name
            };
        }

        return labels;
    }

    /// <summary>
    /// Determine achievement type based on badge definition
    /// </summary>
    private string DetermineAchievementTypeFromBadge(BadgeDefinition badge)
    {
        if (badge.Id.StartsWith("kill_streak_"))
            return AchievementTypes.KillStreak;
        if (badge.Id.StartsWith("total_kills_") || badge.Id.StartsWith("milestone_playtime_") || badge.Id.StartsWith("total_score_"))
            return AchievementTypes.Milestone;
        if (badge.Id.StartsWith("round_placement_"))
            return AchievementTypes.Placement;
        return AchievementTypes.Badge;
    }

    private string DetermineAchievementType(string achievementId)
    {
        if (achievementId.StartsWith("kill_streak_"))
            return AchievementTypes.KillStreak;
        if (achievementId.StartsWith("total_kills_") || achievementId.StartsWith("milestone_playtime_") || achievementId.StartsWith("total_score_"))
            return AchievementTypes.Milestone;
        if (achievementId.StartsWith("round_placement_"))
            return AchievementTypes.Placement;
        return AchievementTypes.Badge;
    }

    private string DetermineTier(string achievementId)
    {
        if (achievementId.Contains("_bronze"))
            return BadgeTiers.Bronze;
        if (achievementId.Contains("_silver"))
            return BadgeTiers.Silver;
        if (achievementId.Contains("_gold"))
            return BadgeTiers.Gold;
        if (achievementId.Contains("_legend"))
            return BadgeTiers.Legend;

        // Special handling for round placement achievements
        if (achievementId.StartsWith("round_placement_"))
        {
            return achievementId switch
            {
                "round_placement_1" => BadgeTiers.Gold,   // 1st place = Gold
                "round_placement_2" => BadgeTiers.Silver, // 2nd place = Silver  
                "round_placement_3" => BadgeTiers.Bronze, // 3rd place = Bronze
                _ => BadgeTiers.Bronze
            };
        }

        // Default tier based on achievement type
        return DetermineAchievementType(achievementId) == AchievementTypes.KillStreak
            ? BadgeTiers.Silver
            : BadgeTiers.Bronze;
    }

    private string DetermineCategory(string achievementId)
    {
        if (achievementId.StartsWith("kill_streak_"))
            return BadgeCategories.Performance;
        if (achievementId.StartsWith("total_kills_") || achievementId.StartsWith("milestone_playtime_") || achievementId.StartsWith("total_score_"))
            return BadgeCategories.Milestone;
        if (achievementId.StartsWith("round_placement_"))
            return BadgeCategories.Performance;
        if (achievementId.StartsWith("map_"))
            return BadgeCategories.MapMastery;
        if (achievementId.StartsWith("sharpshooter_") || achievementId.StartsWith("elite_warrior_"))
            return BadgeCategories.Performance;
        if (achievementId.StartsWith("consistent_") || achievementId.StartsWith("comeback_") || achievementId.StartsWith("rock_"))
            return BadgeCategories.Consistency;
        if (achievementId.StartsWith("server_") || achievementId.StartsWith("night_") || achievementId.StartsWith("early_") || achievementId.StartsWith("marathon_"))
            return BadgeCategories.Social;

        return BadgeCategories.Performance; // Default
    }

    /// <summary>
    /// Generate a display-friendly name for achievement IDs
    /// </summary>
    private string GenerateDisplayName(string achievementId)
    {
        // Special handling for placement achievements
        if (achievementId.StartsWith("round_placement_"))
        {
            return achievementId switch
            {
                "round_placement_1" => "1st Place",
                "round_placement_2" => "2nd Place", 
                "round_placement_3" => "3rd Place",
                _ => "Round Placement"
            };
        }

        // Default: replace underscores with spaces and apply title case
        return achievementId.Replace('_', ' ').ToTitleCase();
    }
}

/// <summary>
/// Extension method to convert string to title case
/// </summary>
public static class StringExtensions
{
    public static string ToTitleCase(this string str)
    {
        if (string.IsNullOrEmpty(str))
            return str;

        var words = str.Split(' ');
        for (int i = 0; i < words.Length; i++)
        {
            if (words[i].Length > 0)
            {
                words[i] = char.ToUpper(words[i][0]) + words[i].Substring(1).ToLower();
            }
        }
        return string.Join(" ", words);
    }
}