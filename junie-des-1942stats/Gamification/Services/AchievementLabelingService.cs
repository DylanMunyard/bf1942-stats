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
                    DisplayName = achievementId.Replace('_', ' ').ToTitleCase()
                });
            }
        }

        return labels;
    }

    /// <summary>
    /// Initialize all achievement labels with their metadata
    /// </summary>
    private Dictionary<string, AchievementLabel> InitializeAchievementLabels()
    {
        var labels = new Dictionary<string, AchievementLabel>();

        // Kill Streak Achievements
        AddKillStreakLabels(labels);
        
        // Performance Badges
        AddPerformanceBadgeLabels(labels);
        
        // Map Mastery Badges
        AddMapMasteryLabels(labels);
        
        // Consistency Badges
        AddConsistencyLabels(labels);
        
        // Milestone Badges
        AddMilestoneLabels(labels);
        
        // Social Badges
        AddSocialLabels(labels);

        return labels;
    }

    private void AddKillStreakLabels(Dictionary<string, AchievementLabel> labels)
    {
        var killStreakLabels = new[]
        {
            ("kill_streak_5", "First Blood", BadgeTiers.Bronze, BadgeCategories.Performance),
            ("kill_streak_10", "Double Digits", BadgeTiers.Bronze, BadgeCategories.Performance),
            ("kill_streak_15", "Killing Spree", BadgeTiers.Silver, BadgeCategories.Performance),
            ("kill_streak_20", "Rampage", BadgeTiers.Silver, BadgeCategories.Performance),
            ("kill_streak_25", "Unstoppable", BadgeTiers.Gold, BadgeCategories.Performance),
            ("kill_streak_30", "Godlike", BadgeTiers.Gold, BadgeCategories.Performance),
            ("kill_streak_50", "Legendary", BadgeTiers.Legend, BadgeCategories.Performance)
        };

        foreach (var (id, name, tier, category) in killStreakLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.KillStreak,
                Tier = tier,
                Category = category,
                DisplayName = name
            };
        }
    }

    private void AddPerformanceBadgeLabels(Dictionary<string, AchievementLabel> labels)
    {
        // KPM-based badges
        var kpmLabels = new[]
        {
            ("sharpshooter_bronze", "Bronze Sharpshooter", BadgeTiers.Bronze),
            ("sharpshooter_silver", "Silver Sharpshooter", BadgeTiers.Silver),
            ("sharpshooter_gold", "Gold Sharpshooter", BadgeTiers.Gold),
            ("sharpshooter_legend", "Legendary Marksman", BadgeTiers.Legend)
        };

        foreach (var (id, name, tier) in kpmLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Badge,
                Tier = tier,
                Category = BadgeCategories.Performance,
                DisplayName = name
            };
        }

        // KD-based badges
        var kdLabels = new[]
        {
            ("elite_warrior_bronze", "Bronze Elite", BadgeTiers.Bronze),
            ("elite_warrior_silver", "Silver Elite", BadgeTiers.Silver),
            ("elite_warrior_gold", "Gold Elite", BadgeTiers.Gold),
            ("elite_warrior_legend", "Legendary Elite", BadgeTiers.Legend)
        };

        foreach (var (id, name, tier) in kdLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Badge,
                Tier = tier,
                Category = BadgeCategories.Performance,
                DisplayName = name
            };
        }
    }

    private void AddMapMasteryLabels(Dictionary<string, AchievementLabel> labels)
    {
        var mapLabels = new[]
        {
            ("map_specialist", "Map Specialist", BadgeTiers.Silver),
            ("map_dominator", "Map Dominator", BadgeTiers.Gold),
            ("map_legend", "Map Legend", BadgeTiers.Legend)
        };

        foreach (var (id, name, tier) in mapLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Badge,
                Tier = tier,
                Category = BadgeCategories.MapMastery,
                DisplayName = name
            };
        }
    }

    private void AddConsistencyLabels(Dictionary<string, AchievementLabel> labels)
    {
        var consistencyLabels = new[]
        {
            ("consistent_killer", "Consistent Killer", BadgeTiers.Silver),
            ("comeback_king", "Comeback King", BadgeTiers.Gold),
            ("rock_solid", "Rock Solid", BadgeTiers.Gold)
        };

        foreach (var (id, name, tier) in consistencyLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Badge,
                Tier = tier,
                Category = BadgeCategories.Consistency,
                DisplayName = name
            };
        }
    }

    private void AddMilestoneLabels(Dictionary<string, AchievementLabel> labels)
    {
        // Kill milestones
        var killMilestones = new[] { 100, 500, 1000, 2500, 5000, 10000, 25000, 50000 };
        var killNames = new[] { "Centurion", "Veteran", "Elite", "Master", "Warlord", "Legend", "Immortal", "God of War" };
        var killTiers = new[] { BadgeTiers.Bronze, BadgeTiers.Bronze, BadgeTiers.Silver, BadgeTiers.Silver, 
                                BadgeTiers.Gold, BadgeTiers.Gold, BadgeTiers.Legend, BadgeTiers.Legend };

        for (int i = 0; i < killMilestones.Length; i++)
        {
            var id = $"total_kills_{killMilestones[i]}";
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Milestone,
                Tier = killTiers[i],
                Category = BadgeCategories.Milestone,
                DisplayName = $"{killNames[i]} ({killMilestones[i]:N0} Kills)"
            };
        }

        // Playtime milestones
        var playtimeMilestones = new[] { 10, 50, 100, 500, 1000 };
        var playtimeNames = new[] { "Recruit", "Soldier", "Veteran", "Elite", "Legend" };
        var playtimeTiers = new[] { BadgeTiers.Bronze, BadgeTiers.Bronze, BadgeTiers.Silver, 
                                   BadgeTiers.Gold, BadgeTiers.Legend };

        for (int i = 0; i < playtimeMilestones.Length; i++)
        {
            var id = $"playtime_{playtimeMilestones[i]}h";
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Milestone,
                Tier = playtimeTiers[i],
                Category = BadgeCategories.Milestone,
                DisplayName = $"{playtimeNames[i]} ({playtimeMilestones[i]}h Played)"
            };
        }

        // Score milestones
        var scoreMilestones = new[] { 10000, 50000, 100000, 500000, 1000000 };
        var scoreTiers = new[] { BadgeTiers.Bronze, BadgeTiers.Silver, BadgeTiers.Silver, 
                                BadgeTiers.Gold, BadgeTiers.Legend };

        for (int i = 0; i < scoreMilestones.Length; i++)
        {
            var id = $"total_score_{scoreMilestones[i]}";
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Milestone,
                Tier = scoreTiers[i],
                Category = BadgeCategories.Milestone,
                DisplayName = $"{scoreMilestones[i]:N0} Total Score"
            };
        }
    }

    private void AddSocialLabels(Dictionary<string, AchievementLabel> labels)
    {
        var socialLabels = new[]
        {
            ("server_regular", "Server Regular", BadgeTiers.Silver),
            ("night_owl", "Night Owl", BadgeTiers.Bronze),
            ("early_bird", "Early Bird", BadgeTiers.Bronze),
            ("marathon_warrior", "Marathon Warrior", BadgeTiers.Gold)
        };

        foreach (var (id, name, tier) in socialLabels)
        {
            labels[id] = new AchievementLabel
            {
                AchievementId = id,
                AchievementType = AchievementTypes.Badge,
                Tier = tier,
                Category = BadgeCategories.Social,
                DisplayName = name
            };
        }
    }

    private string DetermineAchievementType(string achievementId)
    {
        if (achievementId.StartsWith("kill_streak_"))
            return AchievementTypes.KillStreak;
        if (achievementId.StartsWith("total_kills_") || achievementId.StartsWith("playtime_") || achievementId.StartsWith("total_score_"))
            return AchievementTypes.Milestone;
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
        
        // Default tier based on achievement type
        return DetermineAchievementType(achievementId) == AchievementTypes.KillStreak 
            ? BadgeTiers.Silver 
            : BadgeTiers.Bronze;
    }

    private string DetermineCategory(string achievementId)
    {
        if (achievementId.StartsWith("kill_streak_"))
            return BadgeCategories.Performance;
        if (achievementId.StartsWith("total_kills_") || achievementId.StartsWith("playtime_") || achievementId.StartsWith("total_score_"))
            return BadgeCategories.Milestone;
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