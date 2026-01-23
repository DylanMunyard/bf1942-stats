# Enhanced Player Details API

## New API Response Structure

The player details API (`GET /stats/players/{playerName}`) now returns enhanced insights:

### Before (Old Structure):
```json
{
  "totalSessions": 150,
  "totalPlayTimeMinutes": 12000,
  "totalKills": 8500,
  "totalDeaths": 6200,
  "bestScores": [
    {
      "serverGuid": "abc-123",
      "serverName": "Desert Combat Server",
      "bestScore": 45678,
      "playTimeMinutes": 3000
    }
  ]
}
```

### After (New Structure):
```json
{
  "totalSessions": 150,
  "totalPlayTimeMinutes": 12000,
  "totalKills": 8500,
  "totalDeaths": 6200,
  "killMilestones": [
    {
      "milestone": 5000,
      "achievedDate": "2024-03-15T14:30:00Z",
      "totalKillsAtMilestone": 5000,
      "daysToAchieve": 45
    },
    {
      "milestone": 10000,
      "achievedDate": "2024-06-20T09:15:00Z",
      "totalKillsAtMilestone": 10000,
      "daysToAchieve": 142
    }
  ],
  "servers": [
    {
      "serverGuid": "abc-123",
      "serverName": "Desert Combat Server",
      "gameId": "bf1942",
      "totalMinutes": 1800.5,
      "totalKills": 1250,
      "totalDeaths": 890,
      "highestScore": 45678,
      "killsPerMinute": 0.694,
      "totalRounds": 89,
      "kdRatio": 1.40
    },
    {
      "serverGuid": "def-456", 
      "serverName": "Wake Island 24/7",
      "gameId": "bf1942",
      "totalMinutes": 1200.0,
      "totalKills": 780,
      "totalDeaths": 650,
      "highestScore": 32100,
      "killsPerMinute": 0.650,
      "totalRounds": 65,
      "kdRatio": 1.20
    }
  ]
}
```

## Key Changes:

1. **Breaking Change**: Removed `bestScores` array
2. **Added**: `killMilestones` array with milestone tracking (5k, 10k, 20k, 50k kills)
3. **Added**: `servers` array with enhanced server-specific insights
4. **Requirement**: Server insights only shown for servers with 10+ hours of playtime
5. **New Metrics**: 
   - `killsPerMinute` - Player's kill rate on that server
   - `kdRatio` - Kill/death ratio on that server
   - `totalRounds` - Number of rounds played on that server
   - `daysToAchieve` - Days taken to reach each kill milestone

## SQLite Integration:

The new insights use SQLite queries with precomputed aggregates:
- Milestone tracking uses window functions to calculate cumulative kills over time
- Server insights aggregate data from the analytics tables
- Minimum 10-hour threshold ensures meaningful statistics