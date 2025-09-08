# Game Trends API

This document describes the new game trends API endpoints designed to help players understand server activity patterns and find the best times to play.

## API Endpoints

### Landing Page Integration

#### `GET /stats/app/landingdata`
**Optimized endpoint for landing page display with trend data included**
- **Parameters**: 
  - `timeZoneOffsetHours` (optional): Player's timezone offset from UTC (e.g., +14 for Australia/Sydney)
- **Caching**: 10 minutes
- **Response**: Combined badge definitions + trend summary
- **Use case**: Single call to populate landing page with trends

#### `GET /stats/gametrends/landing-summary`
**Dedicated trend summary for landing page**
- **Parameters**: 
  - `gameId` (optional): Filter by game (bf1942, fh2, bfv)
  - `timeZoneOffsetHours` (optional): Player's timezone offset
- **Caching**: 10 minutes
- **Response**: Top 5 active servers + insights + 24h trend pattern

### Detailed Trend Analysis

#### `GET /stats/gametrends/hourly-activity`
**Hourly activity patterns over specified period**
- **Parameters**:
  - `gameId` (optional): Filter by game
  - `daysPeriod` (default: 30): Number of days to analyze
- **Caching**: 30 minutes
- **Use case**: Understanding peak gaming hours

#### `GET /stats/gametrends/server-activity`
**Server-specific activity trends**
- **Parameters**:
  - `gameId` (optional): Filter by game
  - `daysPeriod` (default: 7): Number of days to analyze
- **Caching**: 15 minutes
- **Use case**: Finding which servers are busiest at different times

#### `GET /stats/gametrends/current-activity`
**Real-time activity status**
- **Caching**: 5 minutes
- **Response**: Current player counts and active servers
- **Use case**: "Is it busy right now?"

#### `GET /stats/gametrends/weekly-patterns`
**Weekend vs weekday activity patterns**
- **Parameters**:
  - `gameId` (optional): Filter by game
  - `daysPeriod` (default: 30): Number of days to analyze
- **Caching**: 1 hour
- **Use case**: Understanding weekly activity cycles

#### `GET /stats/gametrends/gamemode-activity`
**Game mode/map popularity trends**
- **Parameters**:
  - `gameId` (optional): Filter by game
  - `daysPeriod` (default: 30): Number of days to analyze
- **Caching**: 30 minutes
- **Use case**: Identifying special events like CTF nights

#### `GET /stats/gametrends/insights`
**Personalized trend insights**
- **Parameters**:
  - `gameId` (optional): Filter by game
  - `timeZoneOffsetHours` (default: 0): Player's timezone offset
- **Caching**: 15 minutes
- **Response**: "Is it busy now?" and "Will it get busier?" insights
- **Use case**: Helping players decide when to play

## Data Models

### Key Data Structures

```typescript
interface TrendInsights {
  currentHourAvgPlayers: number;
  currentHourAvgRounds: number;
  nextHourAvgPlayers: number;
  nextHourAvgRounds: number;
  trendDirection: "increasing" | "decreasing";
  playerTimeZoneOffsetHours: number;
  generatedAt: string;
}

interface HourlyActivityTrend {
  hourOfDay: number;        // 0-23
  dayOfWeek: number;        // 1=Monday, 7=Sunday
  uniquePlayers: number;
  totalRounds: number;
  avgRoundDuration: number;
  activeServers: number;
  uniqueMaps: number;
}

interface CurrentActivityStatus {
  gameId: string;
  serverGuid: string;
  currentPlayers: number;
  activeRounds: number;
  latestActivity: string;
  mapsInRotation: number;
}
```

## Usage Examples

### For Landing Page
```javascript
// Single call to get everything needed for landing page
const response = await fetch('/stats/app/landingdata?timeZoneOffsetHours=14');
const data = await response.json();

// Access trend data
const insights = data.trendSummary.insights;
const currentActivity = data.trendSummary.currentActivity;
const hourlyTrends = data.trendSummary.hourlyTrends;
```

### For Detailed Analysis
```javascript
// Get hourly patterns for the last week
const hourlyTrends = await fetch('/stats/gametrends/hourly-activity?daysPeriod=7&gameId=bf1942');

// Get current activity across all games
const currentActivity = await fetch('/stats/gametrends/current-activity');

// Get personalized insights for Australian player
const insights = await fetch('/stats/gametrends/insights?timeZoneOffsetHours=14&gameId=bf1942');
```

## Performance Optimizations

### Caching Strategy
- **Landing page data**: 10 minutes (balance between freshness and performance)
- **Current activity**: 5 minutes (needs to be relatively fresh)
- **Hourly/weekly patterns**: 30-60 minutes (stable data)
- **Trend insights**: 15 minutes (personalized but cacheable)

### Query Optimization
- Uses optimized `player_rounds` table (50-100x faster than calculating from raw metrics)
- Pre-aggregated round data eliminates complex window functions
- Indexed by player_name, server_guid, and round_start_time
- Filtered queries to avoid unnecessary data processing

### Database Performance
- All queries use the high-performance ClickHouse `player_rounds` table
- Queries are designed for fast aggregation operations
- Proper indexing ensures sub-second response times
- Timezone handling done in database for efficiency

## Integration Points

### Landing Page Display
The trends are designed to answer key questions for players:

1. **"Is it busy right now?"** - `CurrentActivityStatus`
2. **"When should I play?"** - `HourlyActivityTrend`
3. **"Will it get busier?"** - `TrendInsights.trendDirection`
4. **"What's popular this weekend?"** - `GameModeActivityTrend`

### Timezone Handling
- All timestamps stored in UTC
- Frontend passes player's timezone offset
- Server-side calculations adjust for player's local time
- Examples: Australia (+14), US West (-8), EU Central (+1)

### Game Filtering
- Support for filtering by game: `bf1942`, `fh2`, `bfv`
- Defaults to all games if not specified
- Allows game-specific trend analysis

## Future Enhancements

### Potential Additions
- **Historical comparisons**: "This week vs last week"
- **Event detection**: Automatic identification of special gaming events
- **Player notifications**: Alert when favorite servers become active
- **Predictive analytics**: Machine learning models for activity prediction
- **Social features**: Share busy times with friends

### Performance Monitoring
- Monitor cache hit rates for optimization opportunities
- Track query performance and optimize slow queries
- Analyze user patterns to improve caching strategy
- Consider adding read replicas for high-traffic scenarios