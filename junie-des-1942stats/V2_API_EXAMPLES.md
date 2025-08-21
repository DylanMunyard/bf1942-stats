# V2 Player Details API - Usage Examples

## Overview

The V2 Player Details API focuses on **progression metrics and delta analysis** to highlight player improvement and engagement. Unlike the original API that displays static data, V2 emphasizes **what's changing** and **how players are improving**.

## Base URL Structure

All V2 endpoints use the following base pattern:
```
/stats/v2/players/{playerName}/...
```

## Core Endpoints

### 1. Full Progression Details
```http
GET /stats/v2/players/SomePlayer/progression
```

**Response Example:**
```json
{
  "playerName": "SomePlayer",
  "analysisPeriodStart": "2024-11-21T22:31:00Z",
  "analysisPeriodEnd": "2025-08-21T22:31:00Z",
  "overallProgression": {
    "currentKillRate": 0.85,
    "currentKDRatio": 1.32,
    "currentScorePerMinute": 15.2,
    "killRateDelta": {
      "currentValue": 0.85,
      "previousValue": 0.76,
      "absoluteChange": 0.09,
      "percentageChange": 11.8,
      "direction": "Improving",
      "changeDescription": "+11.8% improvement"
    },
    "kdRatioDelta": {
      "currentValue": 1.32,
      "previousValue": 1.18,
      "absoluteChange": 0.14,
      "percentageChange": 11.9,
      "direction": "Improving", 
      "changeDescription": "+11.9% improvement"
    },
    "activeMilestones": [
      {
        "milestoneType": "kills",
        "milestoneName": "5,000 Kills",
        "targetValue": 5000,
        "currentValue": 4650,
        "remainingValue": 350,
        "progressPercentage": 93.0,
        "progressDescription": "4,650 / 5,000 kills"
      }
    ]
  },
  "mapProgressions": [
    {
      "mapName": "Wake Island",
      "totalRoundsPlayed": 45,
      "totalPlayTimeHours": 12.3,
      "currentKillRate": 0.92,
      "currentKDRatio": 1.45,
      "killRateDelta": {
        "currentValue": 0.92,
        "previousValue": 0.81,
        "percentageChange": 13.6,
        "direction": "Improving",
        "changeDescription": "+13.6% improvement"
      },
      "mapAverageKillRate": 0.73,
      "performanceVsAverage": "AboveAverage"
    }
  ],
  "performanceTrajectory": {
    "overallTrajectory": "Improving",
    "trajectoryConfidence": 0.78,
    "trajectoryDescription": "Player demonstrates strong upward trend in performance",
    "killRateTrend": {
      "slope": 0.035,
      "rSquared": 0.72,
      "trend": "Improving",
      "trendDescription": "Improving trend (strong confidence)"
    }
  }
}
```

### 2. Progression Summary (Key Metrics Only)
```http
GET /stats/v2/players/SomePlayer/progression/summary
```

**Perfect for dashboard widgets** - returns just the `overallProgression` object.

### 3. Map-Specific Analysis
```http
GET /stats/v2/players/SomePlayer/progression/maps?minRounds=5
```

Shows how the player is improving on each map they play regularly.

### 4. Performance Trajectory (Trends & Statistics)
```http
GET /stats/v2/players/SomePlayer/progression/trajectory
```

Returns statistical trend analysis with confidence scores:
```json
{
  "overallTrajectory": "StronglyImproving",
  "trajectoryConfidence": 0.85,
  "killRateTrajectory": [
    {"date": "2025-08-01", "value": 0.78, "sampleSize": 8},
    {"date": "2025-08-02", "value": 0.82, "sampleSize": 12},
    {"date": "2025-08-03", "value": 0.87, "sampleSize": 6}
  ],
  "killRateTrend": {
    "slope": 0.042,
    "rSquared": 0.89,
    "trend": "StronglyImproving",
    "trendDescription": "Strong improvement trend (strong confidence)"
  }
}
```

### 5. Recent Activity Analysis
```http
GET /stats/v2/players/SomePlayer/progression/activity
```

Shows playing patterns and engagement:
```json
{
  "lastPlayedDate": "2025-08-21T20:15:00Z",
  "daysSinceLastPlayed": 0,
  "recentActivityLevel": "Active",
  "roundsLast7Days": 28,
  "playTimeLast7Days": 420.5,
  "preferredPlayingHours": [19, 20, 21, 22]
}
```

### 6. Comparative Analysis
```http
GET /stats/v2/players/SomePlayer/progression/comparative
```

Shows how the player compares to server/global averages:
```json
{
  "globalComparison": {
    "globalAverageKillRate": 0.65,
    "globalAverageKDRatio": 1.02,
    "killRateRating": "AboveAverage",
    "kdRatioRating": "AboveAverage",
    "globalRank": 1247,
    "totalPlayers": 15832,
    "globalPercentile": 92.1
  }
}
```

### 7. Multi-Player Comparison
```http
GET /stats/v2/players/compare/progression?playerNames=Player1,Player2,Player3&metric=killrate
```

Compare progression across multiple players - perfect for leaderboards focused on improvement rather than just totals.

## Key Differentiators from V1 API

### V1 (Original): Static Data Display
```json
{
  "totalKills": 4650,
  "totalDeaths": 3521,
  "totalPlayTime": 28450
}
```

### V2 (Progression): Delta & Engagement Focus  
```json
{
  "currentKillRate": 0.85,
  "killRateDelta": {
    "percentageChange": +11.8,
    "direction": "Improving",
    "changeDescription": "+11.8% improvement"
  },
  "performanceTrajectory": "StronglyImproving",
  "milestoneProgress": "93% to 5,000 kills"
}
```

## Use Cases

**1. Player Dashboards:**
- Show "You've improved your K/D by 15% this month!"
- Display milestone progress: "347 kills to next milestone"
- Trajectory indicators: "📈 Trending upward"

**2. Engagement Features:**
- Activity level badges: "Very Active Player"
- Performance comparisons: "Better than 85% of players on this map"
- Improvement recognition: "Your Wake Island performance improved 23%!"

**3. Analytics & Insights:**
- Map-specific coaching: "You excel on urban maps but struggle on open terrain"
- Playing pattern analysis: "Most active between 7-10 PM"
- Progress tracking: "On track to reach 10,000 kills by December"

**4. Community Features:**
- Progress-based leaderboards: "Most improved players this month"
- Peer comparisons: "Similar skill players in your region"
- Achievement celebrations: "Just reached 5,000 kills!"

## Performance Considerations

- All endpoints use **ClickHouse analytics** for fast aggregations
- **90-day analysis window** provides meaningful trends while maintaining performance
- **Parallel query execution** for comprehensive analysis
- **Smart caching** for frequently accessed progression data
- **Configurable minimum sample sizes** to ensure statistical relevance

## Integration with Existing System

- **Backward compatible**: V1 API remains unchanged
- **Shared data sources**: Uses existing `player_rounds` ClickHouse table
- **Consistent authentication**: Same JWT auth as V1
- **Same infrastructure**: Redis caching, logging, metrics

## Future Enhancements

- **Server ranking progression**: Integration with existing ranking system
- **Achievement system**: Connection to gamification features  
- **Predictive analytics**: Estimated completion dates for milestones
- **Seasonal analysis**: Performance comparisons across time periods
- **Social features**: Friends comparison and progression sharing