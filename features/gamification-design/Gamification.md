---
### Gamification Implementation Plan ✅

I've analyzed the current BF1942 stats system and designed a comprehensive gamification strategy. Here's the complete implementation plan addressing your three requirements:

#### Todo List:
- [x] Read repository setup instructions (CLAUDE.md)
- [x] Analyze current codebase structure and architecture
- [x] Examine ClickHouse schema and data models
- [x] Design metrics storage strategy for gamification
- [x] Plan calculation methodology (event-driven vs scheduled)
- [x] Define interesting badges and statistics
- [x] Create detailed implementation roadmap

---

## 1. 📊 Metrics Storage Strategy

### New ClickHouse Tables

**`player_achievements`** - Core gamification table
```sql
CREATE TABLE player_achievements (
    player_name String,
    achievement_type String,        -- 'badge', 'milestone', 'streak', 'ranking'
    achievement_id String,          -- 'sharpshooter_gold', 'kill_streak_25', etc.
    achievement_name String,        -- 'Gold Sharpshooter', '25 Kill Streak'
    tier String,                    -- 'bronze', 'silver', 'gold', 'legend'
    value UInt32,                   -- Achievement threshold (kills, streak, etc.)
    achieved_at DateTime,
    server_guid String,
    map_name String,
    round_id String,
    metadata String                 -- JSON for additional context
) ENGINE = MergeTree()
ORDER BY (player_name, achievement_type, achieved_at)
PARTITION BY toYYYYMM(achieved_at);
```

**`player_streaks`** - Kill/Death streak tracking
```sql
CREATE TABLE player_streaks (
    player_name String,
    streak_type String,             -- 'kill', 'death', 'headshot'
    streak_count UInt16,
    streak_start DateTime,
    streak_end DateTime,
    server_guid String,
    map_name String,
    round_id String,
    is_active Bool DEFAULT 0        -- Current ongoing streak
) ENGINE = MergeTree()
ORDER BY (player_name, streak_type, streak_start)
PARTITION BY toYYYYMM(streak_start);
```

**`player_rankings`** - Leaderboard rankings
```sql
CREATE TABLE player_rankings (
    ranking_period String,          -- 'daily', 'weekly', 'monthly', 'all_time'
    ranking_type String,            -- 'kills', 'kd_ratio', 'score', 'playtime'
    ranking_scope String,           -- 'global', 'server_guid', 'map_name'
    scope_value String,             -- server GUID or map name
    player_name String,
    rank UInt32,
    value Float64,
    calculated_at DateTime
) ENGINE = ReplacingMergeTree(calculated_at)
ORDER BY (ranking_period, ranking_type, ranking_scope, scope_value, rank)
PARTITION BY toYYYYMM(calculated_at);
```

### Player Comparison Features
- **Ranking Comparisons**: Direct rank vs rank comparisons
- **Achievement Gaps**: Show what achievements other players have that you don't
- **Head-to-Head Stats**: Extend existing comparison with achievement counts
- **Server Leaderboards**: Per-server ranking comparisons

---

## 2. ⚡ Calculation Methodology

### Hybrid Approach: Event-Driven + Scheduled

**Event-Driven (Real-time):**
- **Kill Streaks**: Calculate immediately when new `player_metrics` data arrives (every 15s)
- **Milestone Achievements**: Trigger when thresholds crossed
- **Integration Point**: Extend `StatsCollectionBackgroundService:148` after ClickHouse sync

**Scheduled Calculations:**
- **Daily Rankings**: Calculate at 00:05 UTC
- **Weekly/Monthly Rankings**: Calculate on period boundaries  
- **Complex Achievements**: Badge calculations requiring historical analysis
- **Achievement Cleanup**: Remove expired/invalid achievements

### Implementation Integration Points

**1. Real-time Streak Detection (StatsCollectionBackgroundService.cs:148)**
```csharp
// Add after ClickHouse batch storage
var gamificationService = scope.ServiceProvider.GetRequiredService<GamificationService>();
await gamificationService.ProcessRealtimeAchievements(allServers, timestamp);
```

**2. New Background Service: `GamificationBackgroundService`**
- Runs every 5 minutes for achievement processing
- Daily ranking calculations
- Badge tier promotions
- Achievement notifications

---

## 3. 🏆 Badges & Statistics System

### Kill Streak Achievements
```csharp
// Extend your kill streak definition
public class KillStreakDetection
{
    // Current kills increase, deaths stay same = active streak
    // Achievement tiers: 5, 10, 15, 20, 25, 30, 50+ kills
    
    "kill_streak_5": "First Blood (5 kills)",
    "kill_streak_15": "Killing Spree (15 kills)", 
    "kill_streak_25": "Unstoppable (25 kills)",
    "kill_streak_50": "Legendary (50+ kills)"
}
```

### Performance Badges
```csharp
public static class BadgeDefinitions 
{
    // KPM-based badges (per server/map/global)
    "sharpshooter_bronze": "1.0+ KPM sustained over 10 rounds",
    "sharpshooter_silver": "1.5+ KPM sustained over 25 rounds", 
    "sharpshooter_gold": "2.0+ KPM sustained over 50 rounds",
    
    // Map mastery badges  
    "map_specialist": "Top 10% KD ratio on specific map (min 50 rounds)",
    "map_dominator": "Top 3% KD ratio on specific map (min 100 rounds)",
    
    // Consistency badges
    "consistent_killer": "Positive KD in 80% of last 50 rounds",
    "comeback_king": "Most improved player (30-day KD trend)",
    
    // Social badges
    "server_regular": "Top 10 playtime on specific server",
    "night_owl": "Most active 10pm-6am player",
    "early_bird": "Most active 6am-10am player"
}
```

### Ranking Categories
```csharp
public enum RankingType 
{
    TotalKills,           // Raw kill count
    KillsPerMinute,       // Your suggested KPM metric  
    KDRatio,              // Classic metric
    TotalScore,           // Score-based ranking
    PlayTime,             // Dedication ranking
    RecentForm,           // Last 30 days performance
    Consistency,          // Least variance in performance
    MapSpecialist,        // Best map-specific performance
    ServerDomination      // Best server-specific performance  
}
```

### Advanced Statistics
```csharp
public class GamificationStats
{
    // Your suggested metrics
    public KillStreakStats LongestKillStreak { get; set; }
    public Dictionary<string, double> KillsPerMinuteByServer { get; set; }
    public Dictionary<string, double> KillsPerMinuteByMap { get; set; }
    public double OverallKillsPerMinute { get; set; }
    
    // Additional engaging metrics
    public List<Achievement> RecentAchievements { get; set; }
    public Dictionary<string, int> PlayerRankings { get; set; }  // Per category
    public List<Badge> EarnedBadges { get; set; }
    public ProgressToNextBadge NextGoals { get; set; }
    public PlayerComparison VsTopPlayers { get; set; }
}
```

---

## 🚀 Implementation Roadmap

### Phase 1: Foundation (Week 1)
1. **Create ClickHouse tables** (`player_achievements`, `player_streaks`, `player_rankings`)
2. **Build core services**: `GamificationService`, `AchievementCalculator`, `StreakDetector`
3. **Integrate real-time streak detection** into existing background service
4. **API endpoints**: `/gamification/player/{name}`, `/gamification/leaderboards`

### Phase 2: Badge System (Week 2) 
1. **Badge definitions and tiers** (Bronze/Silver/Gold/Legend)
2. **Achievement calculation logic** for all badge types
3. **Historical data migration** - calculate existing achievements  
4. **Player achievement API** with comparison features

### Phase 3: Rankings & Competition (Week 3)
1. **Daily/weekly/monthly ranking calculations**
2. **Leaderboard APIs** with filtering (global, server-specific, map-specific)
3. **Player comparison enhancements** with achievement gaps
4. **Achievement notifications** and progress tracking

### Phase 4: Advanced Features (Week 4)
1. **Trend analysis** (improving/declining players)
2. **Social features** (server regulars, rivalry detection)  
3. **Achievement sharing** and highlights
4. **Performance analytics dashboard**

---

## 🔧 Technical Implementation Details

### Service Architecture
```
GamificationService (main orchestrator)
├── StreakDetector (real-time kill streaks)
├── AchievementCalculator (badge logic)  
├── RankingService (leaderboard calculations)
├── PlayerComparisonEnhancer (extends existing)
└── GamificationBackgroundService (scheduled tasks)
```

### Performance Considerations
- **Leverage existing `player_rounds` optimization** for historical calculations
- **Use `player_metrics` for real-time streak detection** (unavoidable)
- **Partition tables by month** for efficient queries
- **Cache rankings** in Redis with TTL
- **Batch achievement calculations** to avoid performance impact

This gamification system will transform BF1942 stats into an engaging, competitive experience that encourages continued play and provides meaningful progression goals for all skill levels.

---