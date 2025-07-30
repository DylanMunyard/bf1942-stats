-- Gamification tables for BF1942 Stats System
-- Run these commands in ClickHouse to create the gamification schema

-- Core achievements table with ReplacingMergeTree for overwrite capability
CREATE TABLE player_achievements (
    player_name String,
    achievement_type String,        -- 'kill_streak', 'badge', 'milestone', 'ranking'
    achievement_id String,          -- 'kill_streak_15', 'sharpshooter_gold', etc.
    achievement_name String,        -- 'Killing Spree', 'Gold Sharpshooter'
    tier String,                    -- 'bronze', 'silver', 'gold', 'legend'
    value UInt32,                   -- Achievement threshold (kills, streak, etc.)
    achieved_at DateTime,
    processed_at DateTime,          -- For incremental processing tracking
    server_guid String,
    map_name String,
    round_id String,
    metadata String                 -- JSON for additional context
) ENGINE = MergeTree()
ORDER BY (player_name, achievement_type, processed_at)
PARTITION BY toYYYYMM(achieved_at)
COMMENT 'Core gamification achievements including badges, milestones, and kill streaks';

-- Kill streaks table for detailed streak tracking
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
PARTITION BY toYYYYMM(streak_start)
COMMENT 'Detailed kill streak tracking per round';

-- Rankings table for leaderboards with ReplacingMergeTree for overwriting
CREATE TABLE player_rankings (
    ranking_period String,          -- 'daily', 'weekly', 'monthly', 'all_time'
    ranking_type String,            -- 'kills', 'kd_ratio', 'score', 'playtime', 'achievements'
    ranking_scope String,           -- 'global', 'server_guid', 'map_name'
    scope_value String,             -- server GUID or map name
    player_name String,
    rank UInt32,
    value Float64,
    achievement_count UInt32,       -- Number of achievements for this player
    calculated_at DateTime,
    version DateTime DEFAULT now()  -- For ReplacingMergeTree
) ENGINE = ReplacingMergeTree(version)
ORDER BY (ranking_period, ranking_type, ranking_scope, scope_value, rank)
PARTITION BY toYYYYMM(calculated_at)
COMMENT 'Leaderboard rankings for various metrics and scopes';

-- Player aggregated stats for quick lookups
CREATE TABLE player_game_stats (
    player_name String,
    total_kills UInt32,
    total_deaths UInt32,
    total_score UInt32,
    total_playtime_minutes UInt32,
    rounds_played UInt32,
    last_updated DateTime,
    version DateTime DEFAULT now()
) ENGINE = ReplacingMergeTree(version)
ORDER BY player_name
COMMENT 'Aggregated player statistics for quick gamification calculations';

-- Indexes for performance
-- Achievement lookups by player and type
CREATE INDEX idx_player_achievements_player_type ON player_achievements (player_name, achievement_type) TYPE bloom_filter GRANULARITY 1;

-- Ranking lookups by type and scope
CREATE INDEX idx_player_rankings_type_scope ON player_rankings (ranking_type, ranking_scope) TYPE bloom_filter GRANULARITY 1;

-- Recent achievements index
CREATE INDEX idx_player_achievements_recent ON player_achievements (achieved_at) TYPE minmax GRANULARITY 8192;

-- Sample queries for testing tables:
-- SELECT COUNT(*) FROM player_achievements;
-- SELECT player_name, achievement_name, tier FROM player_achievements WHERE achievement_type = 'kill_streak' ORDER BY value DESC LIMIT 10;
-- SELECT * FROM player_rankings WHERE ranking_type = 'kills' AND ranking_scope = 'global' ORDER BY rank LIMIT 100; 