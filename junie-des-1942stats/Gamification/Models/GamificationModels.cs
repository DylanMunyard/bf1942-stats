namespace junie_des_1942stats.Gamification.Models;

public class Achievement
{
    public string PlayerName { get; set; } = "";
    public string AchievementType { get; set; } = ""; // 'kill_streak', 'badge', 'milestone'
    public string AchievementId { get; set; } = ""; // 'kill_streak_15', 'sharpshooter_gold'
    public string AchievementName { get; set; } = "";
    public string Tier { get; set; } = ""; // 'bronze', 'silver', 'gold', 'legend'
    public uint Value { get; set; }
    public DateTime AchievedAt { get; set; }
    public DateTime ProcessedAt { get; set; }
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public string RoundId { get; set; } = "";
    public string Metadata { get; set; } = ""; // JSON for additional context
    public DateTime Version { get; set; } // Version field for ReplacingMergeTree deduplication
}

public class KillStreak
{
    public string PlayerName { get; set; } = "";
    public int StreakCount { get; set; }
    public DateTime StreakStart { get; set; }
    public DateTime StreakEnd { get; set; }
    public string ServerGuid { get; set; } = "";
    public string MapName { get; set; } = "";
    public string RoundId { get; set; } = "";
    public bool IsActive { get; set; }
}

public class PlayerGameStats
{
    public string PlayerName { get; set; } = "";
    public int TotalKills { get; set; }
    public int TotalDeaths { get; set; }
    public int TotalScore { get; set; }
    public int TotalPlayTimeMinutes { get; set; }
    public DateTime LastUpdated { get; set; }
}

public class BadgeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string UIDescription { get; set; } = ""; // Concise, gamer-friendly description for UI
    public string Tier { get; set; } = "";
    public string Category { get; set; } = ""; // 'performance', 'milestone', 'social'
    public Dictionary<string, object> Requirements { get; set; } = new();
}

public class PlayerAchievementSummary
{
    public string PlayerName { get; set; } = "";
    public List<Achievement> RecentAchievements { get; set; } = new();
    public List<Achievement> AllBadges { get; set; } = new();
    public List<Achievement> Milestones { get; set; } = new();
    public List<Achievement> TeamVictories { get; set; } = new();
    public KillStreakStats BestStreaks { get; set; } = new();
    public DateTime LastCalculated { get; set; }
}

public class KillStreakStats
{
    public int BestSingleRoundStreak { get; set; }
    public string BestStreakMap { get; set; } = "";
    public string BestStreakServer { get; set; } = "";
    public DateTime BestStreakDate { get; set; }
    public List<KillStreak> RecentStreaks { get; set; } = new();
}

public class GamificationLeaderboard
{
    public string Category { get; set; } = "";
    public string Period { get; set; } = "";
    public List<LeaderboardEntry> Entries { get; set; } = new();
    public DateTime LastUpdated { get; set; }
}

public class LeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int Value { get; set; }
    public string DisplayValue { get; set; } = "";
    public int AchievementCount { get; set; }
    public List<string> TopBadges { get; set; } = new();
}

public class PlayerPlacementSummary
{
    public string PlayerName { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? MapName { get; set; }
    public int FirstPlaces { get; set; }
    public int SecondPlaces { get; set; }
    public int ThirdPlaces { get; set; }
    public int TotalPlacements => FirstPlaces + SecondPlaces + ThirdPlaces;
    public int PlacementPoints => (FirstPlaces * 3) + (SecondPlaces * 2) + (ThirdPlaces * 1);
    public string? BestTeamLabel { get; set; }
}

public class PlacementLeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int FirstPlaces { get; set; }
    public int SecondPlaces { get; set; }
    public int ThirdPlaces { get; set; }
    public int TotalPlacements => FirstPlaces + SecondPlaces + ThirdPlaces;
    public int PlacementPoints => (FirstPlaces * 3) + (SecondPlaces * 2) + (ThirdPlaces * 1);
}

// Badge tier definitions
public static class BadgeTiers
{
    public const string Bronze = "bronze";
    public const string Silver = "silver";
    public const string Gold = "gold";
    public const string Legend = "legend";
}

// Achievement types
public static class AchievementTypes
{
    public const string KillStreak = "kill_streak";
    public const string Badge = "badge";
    public const string Milestone = "milestone";
    public const string Ranking = "ranking";
    public const string Placement = "round_placement";
    public const string TeamVictory = "team_victory";
    public const string TeamVictorySwitched = "team_victory_switched";
}

// Badge categories
public static class BadgeCategories
{
    public const string Performance = "performance";
    public const string Milestone = "milestone";
    public const string Social = "social";
    public const string MapMastery = "map_mastery";
    public const string Consistency = "consistency";
    public const string TeamPlay = "team_play";
}

/// <summary>
/// Enhanced achievement response that includes player's achievement IDs for filtering
/// </summary>
public class AchievementResponse
{
    public List<Achievement> Items { get; set; } = new List<Achievement>();
    public int Page { get; set; }
    public int PageSize { get; set; }
    public int TotalItems { get; set; }
    public int TotalPages { get; set; }

    /// <summary>
    /// Player name if filtering by specific player
    /// </summary>
    public string? PlayerName { get; set; }

    /// <summary>
    /// Labeled achievement IDs with their type, tier, and category information
    /// </summary>
    public List<AchievementLabel> PlayerAchievementLabels { get; set; } = new List<AchievementLabel>();
}

/// <summary>
/// Achievement label with type, tier, and category information
/// </summary>
public class AchievementLabel
{
    public string AchievementId { get; set; } = "";
    public string AchievementType { get; set; } = "";
    public string Tier { get; set; } = "";
    public string Category { get; set; } = "";
    public string DisplayName { get; set; } = "";
}