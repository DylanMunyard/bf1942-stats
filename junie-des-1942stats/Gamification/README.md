# 🏆 BF1942 Gamification System

A comprehensive achievement and badge system for BF1942 stats tracking.

## 🚀 Quick Setup

### 1. Create ClickHouse Tables
Run the SQL commands in `../Migrations/clickhouse-gamification.sql` in your ClickHouse instance:

```bash
# Connect to ClickHouse and run:
clickhouse-client --query "$(cat Migrations/clickhouse-gamification.sql)"
```

### 2. Start the System
The gamification system will automatically start with the main application. It includes:
- **Background Service**: Processes new achievements every 5 minutes
- **API Endpoints**: Access achievements and leaderboards
- **Incremental Processing**: Only processes new data since last run

### 3. Initial Historical Processing (Optional)
To process existing historical data for achievements:

```bash
# Trigger historical processing via API (admin endpoint)
POST /api/gamification/admin/process-historical
```

## 📊 Achievement Types

### Kill Streak Achievements
- **First Blood** (5 kills) - Bronze
- **Double Digits** (10 kills) - Bronze  
- **Killing Spree** (15 kills) - Silver
- **Rampage** (20 kills) - Silver
- **Unstoppable** (25 kills) - Gold
- **Godlike** (30 kills) - Gold
- **Legendary** (50+ kills) - Legend

### Performance Badges
- **KPM Badges**: Based on kills per minute over sustained rounds
  - Bronze: 1.0+ KPM over 10 rounds
  - Silver: 1.5+ KPM over 25 rounds
  - Gold: 2.0+ KPM over 50 rounds
  - Legend: 2.5+ KPM over 100 rounds

- **KD Ratio Badges**: Based on kill/death ratio
  - Bronze Elite: 2.0+ KD over 25 rounds
  - Silver Elite: 3.0+ KD over 50 rounds
  - Gold Elite: 4.0+ KD over 100 rounds
  - Legendary Elite: 5.0+ KD over 200 rounds

### Milestone Achievements
- **Kill Milestones**: 100, 500, 1K, 2.5K, 5K, 10K, 25K, 50K total kills
- **Playtime Milestones**: 10h, 50h, 100h, 500h, 1000h total played
- **Score Milestones**: 10K, 50K, 100K, 500K, 1M total score

### Consistency Badges
- **Consistent Killer**: Positive KD in 80% of last 50 rounds
- **Rock Solid**: Low variance in performance over time

## 🔗 API Endpoints

### Player Achievements
```bash
# Get all achievements for a player
GET /api/gamification/player/{playerName}

# Get recent achievements (last 30 days)
GET /api/gamification/player/{playerName}/recent?days=30&limit=20

# Get specific achievement type
GET /api/gamification/player/{playerName}/kill_streak
GET /api/gamification/player/{playerName}/badge
GET /api/gamification/player/{playerName}/milestone

# Check if player has specific achievement
GET /api/gamification/player/{playerName}/has/{achievementId}
```

### Leaderboards
```bash
# Get kill streak leaderboard
GET /api/gamification/leaderboard/kill_streaks?limit=100

# Get achievements leaderboard
GET /api/gamification/leaderboard/achievements?period=monthly&limit=50
```

### Badge Information
```bash
# Get all available badges
GET /api/gamification/badges

# Get badges by category
GET /api/gamification/badges/performance
GET /api/gamification/badges/milestone
GET /api/gamification/badges/social
```

### Admin Endpoints
```bash
# Process historical data
POST /api/gamification/admin/process-historical?fromDate=2024-01-01

# Trigger incremental processing
POST /api/gamification/admin/process-incremental

# Get system stats
GET /api/gamification/stats
```

## ⚡ Performance Features

### Incremental Processing
- Only processes new `player_rounds` data since last run
- Uses `MAX(processed_at)` timestamp to track progress
- No reprocessing of historical records

### Optimized Calculations
- **Kill Streaks**: Single-round only (no cross-round complexity)
- **Milestones**: Threshold detection on new rounds
- **Performance Badges**: Calculated using recent round windows
- **Batch Processing**: Multiple achievements stored in single ClickHouse operation

### Caching Strategy
- Badge definitions cached in memory
- Recent player data cached for performance calculations
- Leaderboards can be cached with TTL

## 🛠️ Architecture

```
GamificationService (main orchestrator)
├── KillStreakDetector (real-time single-round streaks)
├── MilestoneCalculator (threshold crossing detection)
├── PerformanceBadgeCalculator (KPM, KD badges)
├── ClickHouseGamificationService (data operations)
└── BadgeDefinitionsService (badge metadata)
```

## 📈 Monitoring

The system provides comprehensive logging:
- Achievement processing statistics
- Performance metrics
- Error handling and recovery
- Historical processing progress

Check application logs for gamification processing information.

## 🔧 Configuration

Key environment variables:
- `CLICKHOUSE_URL`: ClickHouse connection URL for gamification data

The system uses the same ClickHouse instance as the main stats system and automatically configures itself based on existing settings. 