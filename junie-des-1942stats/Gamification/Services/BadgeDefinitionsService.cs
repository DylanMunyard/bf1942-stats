using junie_des_1942stats.Gamification.Models;

namespace junie_des_1942stats.Gamification.Services;

public class BadgeDefinitionsService
{
    private readonly Dictionary<string, BadgeDefinition> _badgeDefinitions;

    public BadgeDefinitionsService()
    {
        _badgeDefinitions = InitializeBadgeDefinitions();
    }

    public BadgeDefinition? GetBadgeDefinition(string badgeId)
    {
        return _badgeDefinitions.TryGetValue(badgeId, out var badge) ? badge : null;
    }

    public List<BadgeDefinition> GetAllBadges()
    {
        return _badgeDefinitions.Values.ToList();
    }

    public List<BadgeDefinition> GetBadgesByCategory(string category)
    {
        return _badgeDefinitions.Values.Where(b => b.Category == category).ToList();
    }

    public List<BadgeDefinition> GetBadgesByTier(string tier)
    {
        return _badgeDefinitions.Values.Where(b => b.Tier == tier).ToList();
    }

    private Dictionary<string, BadgeDefinition> InitializeBadgeDefinitions()
    {
        var badges = new Dictionary<string, BadgeDefinition>();

        // Kill Streak Achievements
        AddKillStreakBadges(badges);
        
        // Performance Badges (KPM-based)
        AddPerformanceBadges(badges);
        
        // Map Mastery Badges
        AddMapMasteryBadges(badges);
        
        // Consistency Badges
        AddConsistencyBadges(badges);
        
        // Milestone Badges
        AddMilestoneBadges(badges);
        
        // Social Badges
        AddSocialBadges(badges);

        return badges;
    }

    private void AddKillStreakBadges(Dictionary<string, BadgeDefinition> badges)
    {
        var streakBadges = new[]
        {
            ("kill_streak_5", "First Blood", "5 kill streak in a single round", BadgeTiers.Bronze, 5),
            ("kill_streak_10", "Double Digits", "10 kill streak in a single round", BadgeTiers.Bronze, 10),
            ("kill_streak_15", "Killing Spree", "15 kill streak in a single round", BadgeTiers.Silver, 15),
            ("kill_streak_20", "Rampage", "20 kill streak in a single round", BadgeTiers.Silver, 20),
            ("kill_streak_25", "Unstoppable", "25 kill streak in a single round", BadgeTiers.Gold, 25),
            ("kill_streak_30", "Godlike", "30 kill streak in a single round", BadgeTiers.Gold, 30),
            ("kill_streak_50", "Legendary", "50+ kill streak in a single round", BadgeTiers.Legend, 50)
        };

        foreach (var (id, name, desc, tier, value) in streakBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                Tier = tier,
                Category = BadgeCategories.Performance,
                Requirements = new Dictionary<string, object> { ["streak_count"] = value }
            };
        }
    }

    private void AddPerformanceBadges(Dictionary<string, BadgeDefinition> badges)
    {
        var kpmBadges = new[]
        {
            ("sharpshooter_bronze", "Bronze Sharpshooter", "1.0+ KPM sustained over 10 rounds", BadgeTiers.Bronze, 1.0, 10),
            ("sharpshooter_silver", "Silver Sharpshooter", "1.5+ KPM sustained over 25 rounds", BadgeTiers.Silver, 1.5, 25),
            ("sharpshooter_gold", "Gold Sharpshooter", "2.0+ KPM sustained over 50 rounds", BadgeTiers.Gold, 2.0, 50),
            ("sharpshooter_legend", "Legendary Marksman", "2.5+ KPM sustained over 100 rounds", BadgeTiers.Legend, 2.5, 100)
        };

        foreach (var (id, name, desc, tier, kpm, rounds) in kpmBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                Tier = tier,
                Category = BadgeCategories.Performance,
                Requirements = new Dictionary<string, object> 
                { 
                    ["min_kpm"] = kpm,
                    ["min_rounds"] = rounds
                }
            };
        }

        // KD Ratio badges
        var kdBadges = new[]
        {
            ("elite_warrior_bronze", "Bronze Elite", "2.0+ KD ratio over 25 rounds", BadgeTiers.Bronze, 2.0, 25),
            ("elite_warrior_silver", "Silver Elite", "3.0+ KD ratio over 50 rounds", BadgeTiers.Silver, 3.0, 50),
            ("elite_warrior_gold", "Gold Elite", "4.0+ KD ratio over 100 rounds", BadgeTiers.Gold, 4.0, 100),
            ("elite_warrior_legend", "Legendary Elite", "5.0+ KD ratio over 200 rounds", BadgeTiers.Legend, 5.0, 200)
        };

        foreach (var (id, name, desc, tier, kd, rounds) in kdBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                Tier = tier,
                Category = BadgeCategories.Performance,
                Requirements = new Dictionary<string, object> 
                { 
                    ["min_kd_ratio"] = kd,
                    ["min_rounds"] = rounds
                }
            };
        }
    }

    private void AddMapMasteryBadges(Dictionary<string, BadgeDefinition> badges)
    {
        badges["map_specialist"] = new BadgeDefinition
        {
            Id = "map_specialist",
            Name = "Map Specialist",
            Description = "Top 10% KD ratio on specific map (min 50 rounds)",
            Tier = BadgeTiers.Silver,
            Category = BadgeCategories.MapMastery,
            Requirements = new Dictionary<string, object> 
            { 
                ["min_percentile"] = 90,
                ["min_rounds"] = 50
            }
        };

        badges["map_dominator"] = new BadgeDefinition
        {
            Id = "map_dominator",
            Name = "Map Dominator",
            Description = "Top 3% KD ratio on specific map (min 100 rounds)",
            Tier = BadgeTiers.Gold,
            Category = BadgeCategories.MapMastery,
            Requirements = new Dictionary<string, object> 
            { 
                ["min_percentile"] = 97,
                ["min_rounds"] = 100
            }
        };

        badges["map_legend"] = new BadgeDefinition
        {
            Id = "map_legend",
            Name = "Map Legend",
            Description = "Top 1% KD ratio on specific map (min 200 rounds)",
            Tier = BadgeTiers.Legend,
            Category = BadgeCategories.MapMastery,
            Requirements = new Dictionary<string, object> 
            { 
                ["min_percentile"] = 99,
                ["min_rounds"] = 200
            }
        };
    }

    private void AddConsistencyBadges(Dictionary<string, BadgeDefinition> badges)
    {
        badges["consistent_killer"] = new BadgeDefinition
        {
            Id = "consistent_killer",
            Name = "Consistent Killer",
            Description = "Positive KD in 80% of last 50 rounds",
            Tier = BadgeTiers.Silver,
            Category = BadgeCategories.Consistency,
            Requirements = new Dictionary<string, object> 
            { 
                ["positive_kd_percentage"] = 80,
                ["rounds_window"] = 50
            }
        };

        badges["comeback_king"] = new BadgeDefinition
        {
            Id = "comeback_king",
            Name = "Comeback King",
            Description = "Most improved player (30-day KD trend)",
            Tier = BadgeTiers.Gold,
            Category = BadgeCategories.Consistency,
            Requirements = new Dictionary<string, object> 
            { 
                ["improvement_period_days"] = 30,
                ["min_improvement_factor"] = 1.5
            }
        };

        badges["rock_solid"] = new BadgeDefinition
        {
            Id = "rock_solid",
            Name = "Rock Solid",
            Description = "Low variance in KD ratio over 100 rounds",
            Tier = BadgeTiers.Gold,
            Category = BadgeCategories.Consistency,
            Requirements = new Dictionary<string, object> 
            { 
                ["max_kd_variance"] = 0.3,
                ["min_rounds"] = 100
            }
        };
    }

    private void AddMilestoneBadges(Dictionary<string, BadgeDefinition> badges)
    {
        var killMilestones = new[]
        {
            (100, "Centurion", BadgeTiers.Bronze),
            (500, "Veteran", BadgeTiers.Bronze),
            (1000, "Elite", BadgeTiers.Silver),
            (2500, "Master", BadgeTiers.Silver),
            (5000, "Warlord", BadgeTiers.Gold),
            (10000, "Legend", BadgeTiers.Gold),
            (25000, "Immortal", BadgeTiers.Legend),
            (50000, "God of War", BadgeTiers.Legend)
        };

        foreach (var (kills, name, tier) in killMilestones)
        {
            badges[$"total_kills_{kills}"] = new BadgeDefinition
            {
                Id = $"total_kills_{kills}",
                Name = $"{name} ({kills:N0} Kills)",
                Description = $"Achieve {kills:N0} total kills",
                Tier = tier,
                Category = BadgeCategories.Milestone,
                Requirements = new Dictionary<string, object> { ["total_kills"] = kills }
            };
        }

        var playtimeMilestones = new[]
        {
            (10, "Recruit", BadgeTiers.Bronze),
            (50, "Soldier", BadgeTiers.Bronze),
            (100, "Veteran", BadgeTiers.Silver),
            (500, "Elite", BadgeTiers.Gold),
            (1000, "Legend", BadgeTiers.Legend)
        };

        foreach (var (hours, name, tier) in playtimeMilestones)
        {
            badges[$"playtime_{hours}h"] = new BadgeDefinition
            {
                Id = $"playtime_{hours}h",
                Name = $"{name} ({hours}h Played)",
                Description = $"Play for {hours} hours total",
                Tier = tier,
                Category = BadgeCategories.Milestone,
                Requirements = new Dictionary<string, object> { ["playtime_hours"] = hours }
            };
        }
    }

    private void AddSocialBadges(Dictionary<string, BadgeDefinition> badges)
    {
        badges["server_regular"] = new BadgeDefinition
        {
            Id = "server_regular",
            Name = "Server Regular",
            Description = "Top 10 playtime on specific server",
            Tier = BadgeTiers.Silver,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> { ["server_rank_threshold"] = 10 }
        };

        badges["night_owl"] = new BadgeDefinition
        {
            Id = "night_owl",
            Name = "Night Owl",
            Description = "Most active 10pm-6am player",
            Tier = BadgeTiers.Bronze,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> 
            { 
                ["time_start"] = 22,
                ["time_end"] = 6,
                ["min_hours"] = 50
            }
        };

        badges["early_bird"] = new BadgeDefinition
        {
            Id = "early_bird",
            Name = "Early Bird",
            Description = "Most active 6am-10am player",
            Tier = BadgeTiers.Bronze,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> 
            { 
                ["time_start"] = 6,
                ["time_end"] = 10,
                ["min_hours"] = 50
            }
        };

        badges["marathon_warrior"] = new BadgeDefinition
        {
            Id = "marathon_warrior",
            Name = "Marathon Warrior",
            Description = "Play for 6+ consecutive hours",
            Tier = BadgeTiers.Gold,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> { ["consecutive_hours"] = 6 }
        };
    }
} 