using api.Gamification.Models;

namespace api.Gamification.Services;

public class BadgeDefinitionsService : IBadgeDefinitionsService
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

        // Placement Badges
        AddPlacementBadges(badges);

        // Team Victory Badges
        AddTeamVictoryBadges(badges);

        return badges;
    }

    private void AddKillStreakBadges(Dictionary<string, BadgeDefinition> badges)
    {
        var streakBadges = new[]
        {
            ("kill_streak_5", "First Blood", "5 kill streak in a single round", "Get 5 kills without dying in one round", BadgeTiers.Bronze, 5),
            ("kill_streak_10", "Double Digits", "10 kill streak in a single round", "Get 10 kills without dying in one round", BadgeTiers.Bronze, 10),
            ("kill_streak_15", "Killing Spree", "15 kill streak in a single round", "Get 15 kills without dying in one round", BadgeTiers.Silver, 15),
            ("kill_streak_20", "Rampage", "20 kill streak in a single round", "Get 20 kills without dying in one round", BadgeTiers.Silver, 20),
            ("kill_streak_25", "Unstoppable", "25 kill streak in a single round", "Get 25 kills without dying in one round", BadgeTiers.Gold, 25),
            ("kill_streak_30", "Godlike", "30 kill streak in a single round", "Get 30 kills without dying in one round", BadgeTiers.Gold, 30),
            ("kill_streak_50", "Legendary", "50+ kill streak in a single round", "Get 50+ kills without dying in one round", BadgeTiers.Legend, 50)
        };

        foreach (var (id, name, desc, uiDesc, tier, value) in streakBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                UIDescription = uiDesc,
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
            ("sharpshooter_bronze", "Bronze Sharpshooter", "1.0+ KPM sustained over 10 rounds", "Maintain 1+ kills per minute over 10 rounds", BadgeTiers.Bronze, 1.0, 10),
            ("sharpshooter_silver", "Silver Sharpshooter", "1.5+ KPM sustained over 25 rounds", "Maintain 1.5+ kills per minute over 25 rounds", BadgeTiers.Silver, 1.5, 25),
            ("sharpshooter_gold", "Gold Sharpshooter", "2.0+ KPM sustained over 50 rounds", "Maintain 2+ kills per minute over 50 rounds", BadgeTiers.Gold, 2.0, 50),
            ("sharpshooter_legend", "Legendary Marksman", "2.5+ KPM sustained over 100 rounds", "Maintain 2.5+ kills per minute over 100 rounds", BadgeTiers.Legend, 2.5, 100)
        };

        foreach (var (id, name, desc, uiDesc, tier, kpm, rounds) in kpmBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                UIDescription = uiDesc,
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
            ("elite_warrior_bronze", "Bronze Elite", "2.0+ KD ratio over 25 rounds", "Maintain 2+ kill/death ratio over 25 rounds", BadgeTiers.Bronze, 2.0, 25),
            ("elite_warrior_silver", "Silver Elite", "3.0+ KD ratio over 50 rounds", "Maintain 3+ kill/death ratio over 50 rounds", BadgeTiers.Silver, 3.0, 50),
            ("elite_warrior_gold", "Gold Elite", "4.0+ KD ratio over 100 rounds", "Maintain 4+ kill/death ratio over 100 rounds", BadgeTiers.Gold, 4.0, 100),
            ("elite_warrior_legend", "Legendary Elite", "5.0+ KD ratio over 200 rounds", "Maintain 5+ kill/death ratio over 200 rounds", BadgeTiers.Legend, 5.0, 200)
        };

        foreach (var (id, name, desc, uiDesc, tier, kd, rounds) in kdBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                UIDescription = uiDesc,
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
            UIDescription = "Rank in top 10% on a map (50+ rounds)",
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
            UIDescription = "Rank in top 3% on a map (100+ rounds)",
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
            UIDescription = "Rank in top 1% on a map (200+ rounds)",
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
            UIDescription = "Stay positive K/D in 80% of your last 50 rounds",
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
            UIDescription = "Show the biggest improvement in K/D over 30 days",
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
            UIDescription = "Keep consistent K/D performance over 100 rounds",
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
                UIDescription = $"Reach {kills:N0} total kills",
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
            badges[$"milestone_playtime_{hours}h"] = new BadgeDefinition
            {
                Id = $"milestone_playtime_{hours}h",
                Name = $"{name} ({hours}h Played)",
                Description = $"Play for {hours} hours total",
                UIDescription = $"Play for {hours} hours total",
                Tier = tier,
                Category = BadgeCategories.Milestone,
                Requirements = new Dictionary<string, object> { ["playtime_hours"] = hours }
            };
        }

        // Add score milestones
        var scoreMilestones = new[]
        {
            (10000, "Bronze Scorer", BadgeTiers.Bronze),
            (50000, "Silver Scorer", BadgeTiers.Silver),
            (100000, "Gold Scorer", BadgeTiers.Silver),
            (500000, "Master Scorer", BadgeTiers.Gold),
            (1000000, "Legendary Scorer", BadgeTiers.Legend)
        };

        foreach (var (score, name, tier) in scoreMilestones)
        {
            badges[$"total_score_{score}"] = new BadgeDefinition
            {
                Id = $"total_score_{score}",
                Name = $"{name} ({score:N0} Score)",
                Description = $"Achieve {score:N0} total score",
                UIDescription = $"Reach {score:N0} total score",
                Tier = tier,
                Category = BadgeCategories.Milestone,
                Requirements = new Dictionary<string, object> { ["total_score"] = score }
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
            UIDescription = "Be in top 10 most active players on a server",
            Tier = BadgeTiers.Silver,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> { ["server_rank_threshold"] = 10 }
        };

        badges["night_owl"] = new BadgeDefinition
        {
            Id = "night_owl",
            Name = "Night Owl",
            Description = "Most active 10pm-6am player",
            UIDescription = "Be most active player during night hours (10pm-6am)",
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
            UIDescription = "Be most active player during morning hours (6am-10am)",
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
            UIDescription = "Play for 6+ hours in a single session",
            Tier = BadgeTiers.Gold,
            Category = BadgeCategories.Social,
            Requirements = new Dictionary<string, object> { ["consecutive_hours"] = 6 }
        };
    }

    private void AddPlacementBadges(Dictionary<string, BadgeDefinition> badges)
    {
        var placementBadges = new[]
        {
            ("round_placement_1", "1st Place", "Finish 1st in a round", "Finish 1st place in a round", BadgeTiers.Gold),
            ("round_placement_2", "2nd Place", "Finish 2nd in a round", "Finish 2nd place in a round", BadgeTiers.Silver),
            ("round_placement_3", "3rd Place", "Finish 3rd in a round", "Finish 3rd place in a round", BadgeTiers.Bronze)
        };

        foreach (var (id, name, desc, uiDesc, tier) in placementBadges)
        {
            badges[id] = new BadgeDefinition
            {
                Id = id,
                Name = name,
                Description = desc,
                UIDescription = uiDesc,
                Tier = tier,
                Category = BadgeCategories.Performance,
                Requirements = new Dictionary<string, object>
                {
                    ["placement"] = int.Parse(id.Split('_').Last())
                }
            };
        }
    }

    private void AddTeamVictoryBadges(Dictionary<string, BadgeDefinition> badges)
    {
        // Regular Team Victory Badge
        badges["team_victory"] = new BadgeDefinition
        {
            Id = "team_victory",
            Name = "Team Victory",
            Description = "Win a round on the victorious team",
            UIDescription = "Be on the winning team when your team wins a round",
            Tier = BadgeTiers.Bronze, // Base tier - actual tier determined by performance
            Category = BadgeCategories.TeamPlay,
            Requirements = new Dictionary<string, object>
            {
                ["team_loyalty"] = "Stay on winning team",
                ["activity_requirement"] = "Active within 2 minutes of round end",
                ["performance_tiers"] = new Dictionary<string, string>
                {
                    [BadgeTiers.Legend] = "Exceptional contribution: 120%+ of median teammate activity × loyalty factor. Based on observations during round compared to team median, multiplied by team participation ratio (time spent on winning team).",
                    [BadgeTiers.Gold] = "Strong contribution: 100%+ of median teammate activity × loyalty factor. Your activity matched or exceeded the median teammate while maintaining good team loyalty.",
                    [BadgeTiers.Silver] = "Good contribution: 70%+ of median teammate activity × loyalty factor. Solid performance relative to your teammates with reasonable team participation.",
                    [BadgeTiers.Bronze] = "Team participation: 40%+ of median teammate activity × loyalty factor. Minimum contribution threshold - everyone active within 2 minutes of round end qualifies."
                }
            }
        };

        // Team Switched Victory Badge
        badges["team_victory_switched"] = new BadgeDefinition
        {
            Id = "team_victory_switched",
            Name = "Team Victory (Team Switched)",
            Description = "Win a round despite switching teams during gameplay",
            UIDescription = "Win a round after switching teams (spent majority time on winning team)",
            Tier = BadgeTiers.Bronze, // Base tier - actual tier determined by performance
            Category = BadgeCategories.TeamPlay,
            Requirements = new Dictionary<string, object>
            {
                ["team_switching"] = "Switched teams during round",
                ["majority_time"] = "Spent majority time on winning team",
                ["activity_requirement"] = "Active within 2 minutes of round end",
                ["performance_tiers"] = new Dictionary<string, string>
                {
                    [BadgeTiers.Gold] = "Exceptional contribution despite switching: 100%+ of median teammate activity × loyalty factor. Remarkable performance considering team switching penalty - you spent majority time on winning team and still matched median contribution.",
                    [BadgeTiers.Silver] = "Good contribution despite switching: 70%+ of median teammate activity × loyalty factor. Solid performance despite team switching - your majority time on winning team contributed meaningfully.",
                    [BadgeTiers.Bronze] = "Recognition for majority time on winning team: Awarded for spending majority of round time on the winning team, regardless of contribution level. Shows strategic team selection."
                }
            }
        };
    }
}