# System Statistics Page - UI Implementation Prompt

## Overview
Implement a **System Statistics Dashboard** page that displays real-time data volume metrics from the BF1942 Stats backend. This page should showcase the scale of data being processed across our dual-database architecture (ClickHouse for analytics, SQLite for operational data).

## Backend API Endpoint

**URL:** `GET /stats/app/systemstats`

**Response Format:**
```json
{
  "clickHouseMetrics": {
    "roundsTracked": 1234567,
    "playerMetricsTracked": 9876543
  },
  "sqliteMetrics": {
    "serversTracked": 42,
    "playersTracked": 15823
  },
  "generatedAt": "2025-10-18T14:30:00Z"
}
```

**Performance Characteristics:**
- **Cache Duration:** 5 minutes (300 seconds)
- **Response Time:** < 100ms (heavily cached)
- **Backend Implementation:** Executes 4 parallel COUNT queries across ClickHouse and SQLite
- **Data Freshness:** Updates every 5 minutes

## Technical Architecture Details

### Database Layer Breakdown

#### ClickHouse (Analytical Database)
ClickHouse is a columnar OLAP database optimized for analytical queries on massive datasets:

**1. `roundsTracked` (from `player_rounds` table)**
- Represents completed game rounds where players participated
- Each row = one player's participation in one round
- **Schema includes:** round_id, player_name, server_guid, map, kills, deaths, score, team, etc.
- **Growth rate:** ~1000-5000 rows per day depending on server activity
- **Performance:** COUNT(*) executes in <50ms even with millions of rows due to columnar storage
- **Why it matters:** Shows the historical depth of gameplay data available for analytics

**2. `playerMetricsTracked` (from `player_metrics` table)**
- Time-series snapshots of player statistics taken periodically during gameplay
- Each row = one snapshot of a player's stats at a specific timestamp
- **Schema includes:** timestamp, player_name, server_guid, score, kills, deaths, ping, team
- **Growth rate:** ~5000-20000 rows per day (higher frequency than rounds)
- **Performance:** COUNT(*) is optimized via ClickHouse's metadata system
- **Why it matters:** Demonstrates the granularity of real-time tracking capabilities

**ClickHouse Design Philosophy:**
- Columnar storage allows COUNT(*) to use table metadata instead of scanning rows
- Partitioned by time (typically by day or month) for efficient querying
- Optimized for append-only workloads (perfect for time-series game data)

#### SQLite (Operational Database)
SQLite serves as the source of truth for entity tracking and relationships:

**1. `serversTracked` (from `Servers` table)**
- Unique game servers being monitored
- Each row = one BF1942 game server (identified by GUID)
- **Includes:** server_guid, name, IP address, port, last_seen timestamp, etc.
- **Typical range:** 20-100 servers (relatively stable count)
- **Performance:** COUNT(*) is instant (<10ms) using primary key metadata
- **Why it matters:** Shows the infrastructure footprint being monitored

**2. `playersTracked` (from `Players` table)**
- Unique players who have been observed across all servers
- Each row = one unique player (identified by player_name)
- **Includes:** player_name, first_seen, last_seen, total_kills, total_deaths, etc.
- **Growth rate:** +50-200 new players per month
- **Performance:** COUNT(*) uses primary key index for instant results
- **Why it matters:** Represents the size of the player community being tracked

### Why Two Databases?

**SQLite Strengths:**
- ACID compliance for critical entity data
- Simple embedded database (single file)
- Perfect for operational reads/writes with moderate volume
- Excellent for relational data with foreign keys

**ClickHouse Strengths:**
- Handles billions of rows with sub-second query times
- Columnar compression (10x-100x better than row-based)
- Optimized for aggregations, time-series analysis, and analytics
- Horizontally scalable for massive data volumes

**The Separation:**
- SQLite = "Who and where" (entities: players, servers)
- ClickHouse = "What and when" (events: rounds played, metrics snapshots)

## UI Design Requirements

### Visual Style
Create a **data-focused dashboard** with these characteristics:

**Theme:**
- Dark mode optimized (or follow your existing theme)
- Monospaced fonts for numbers (e.g., `JetBrains Mono`, `Fira Code`, `Consolas`)
- Color-coded by database type:
  - **ClickHouse metrics:** Blue/Cyan tones (#00B4D8, #0077B6)
  - **SQLite metrics:** Green/Emerald tones (#06D6A0, #048067)

**Layout Options:**

**Option 1: Card Grid (Recommended for Visual Impact)**
```
┌─────────────────────────────────────────────────────┐
│               SYSTEM STATISTICS                      │
│           Data Volume & Scale Overview              │
└─────────────────────────────────────────────────────┘

┌──────────────────────┐  ┌──────────────────────┐
│  CLICKHOUSE          │  │  SQLITE              │
│  Analytics Engine    │  │  Operational DB      │
├──────────────────────┤  ├──────────────────────┤
│                      │  │                      │
│  1,234,567           │  │      42              │
│  Rounds Tracked      │  │  Servers Tracked     │
│                      │  │                      │
│  9,876,543           │  │   15,823             │
│  Metrics Tracked     │  │  Players Tracked     │
│                      │  │                      │
└──────────────────────┘  └──────────────────────┘

Last Updated: Oct 18, 2025 2:30 PM UTC
Auto-refresh in 4:23
```

**Option 2: Unified Table (Better for Dense Information)**
```
┌──────────────┬─────────────────────────────┬────────────┐
│ DATABASE     │ METRIC                      │   COUNT    │
├──────────────┼─────────────────────────────┼────────────┤
│ ClickHouse   │ Rounds Tracked              │  1,234,567 │
│ ClickHouse   │ Player Metrics Tracked      │  9,876,543 │
│ SQLite       │ Servers Tracked             │         42 │
│ SQLite       │ Players Tracked             │     15,823 │
└──────────────┴─────────────────────────────┴────────────┘
```

### Number Formatting
Apply locale-aware formatting for readability:

```javascript
// Use Intl.NumberFormat for proper locale formatting
const formatCount = (num) => {
  return new Intl.NumberFormat('en-US').format(num);
};

// Examples:
// 1234567 → "1,234,567"
// 9876543 → "9,876,543"
// 42 → "42"
```

### Interactive Features

**1. Auto-refresh Mechanism**
```javascript
// Implement automatic refresh every 5 minutes (matching cache duration)
useEffect(() => {
  const fetchStats = async () => {
    const response = await fetch('/stats/app/systemstats');
    const data = await response.json();
    setStats(data);
  };

  fetchStats(); // Initial fetch
  const interval = setInterval(fetchStats, 5 * 60 * 1000); // 5 minutes

  return () => clearInterval(interval);
}, []);
```

**2. Loading States**
- **Initial load:** Skeleton loaders for each metric card
- **Auto-refresh:** Subtle pulse/shimmer effect on numbers during update
- **Error state:** Display last successful data with error banner

**3. Countdown Timer**
Display time until next refresh (similar to financial dashboards):
```
Last updated: 2 minutes ago
Next refresh in: 3m 14s
```

**4. Hover Tooltips** (Optional Enhancement)
Show additional context on hover:

- **Rounds Tracked:** "Total completed game rounds across all servers since inception"
- **Player Metrics Tracked:** "Time-series snapshots of player stats (captured every 30s during gameplay)"
- **Servers Tracked:** "Unique BF1942 game servers currently being monitored"
- **Players Tracked:** "Unique players observed across all servers"

### Rate of Change Indicators (Advanced Feature)

If you want to show growth, fetch the endpoint twice (initial + 5min later) and calculate:

```javascript
const calculateGrowthRate = (current, previous, timeWindowMinutes) => {
  const diff = current - previous;
  const rate = diff / timeWindowMinutes;
  return {
    absolute: diff,
    perMinute: rate,
    perHour: rate * 60,
    perDay: rate * 60 * 24
  };
};

// Display example:
// "Rounds Tracked: 1,234,567 (+128 in last 5m, ~1,536/hr)"
```

### Responsive Design Breakpoints

**Desktop (>1024px):**
- 2-column grid for ClickHouse/SQLite sections
- Large numbers (48-72px font size)
- Full descriptive labels

**Tablet (768px - 1024px):**
- 2-column grid maintained
- Medium numbers (36-48px)
- Abbreviated labels if needed

**Mobile (<768px):**
- Single column stack
- Cards full-width
- Slightly smaller numbers (32-40px)
- Consider tabbed interface for ClickHouse/SQLite

## Sample React/Next.js Implementation

```typescript
// app/stats/system/page.tsx
'use client';

import { useEffect, useState } from 'react';

interface SystemStats {
  clickHouseMetrics: {
    roundsTracked: number;
    playerMetricsTracked: number;
  };
  sqliteMetrics: {
    serversTracked: number;
    playersTracked: number;
  };
  generatedAt: string;
}

export default function SystemStatsPage() {
  const [stats, setStats] = useState<SystemStats | null>(null);
  const [loading, setLoading] = useState(true);
  const [lastUpdate, setLastUpdate] = useState<Date | null>(null);

  const fetchStats = async () => {
    try {
      const response = await fetch('/stats/app/systemstats');
      const data = await response.json();
      setStats(data);
      setLastUpdate(new Date());
      setLoading(false);
    } catch (error) {
      console.error('Failed to fetch system stats:', error);
    }
  };

  useEffect(() => {
    fetchStats();
    const interval = setInterval(fetchStats, 5 * 60 * 1000); // 5 minutes
    return () => clearInterval(interval);
  }, []);

  const formatNumber = (num: number) => {
    return new Intl.NumberFormat('en-US').format(num);
  };

  if (loading || !stats) {
    return <div>Loading system statistics...</div>;
  }

  return (
    <div className="container mx-auto p-6">
      <header className="mb-8">
        <h1 className="text-4xl font-bold mb-2">System Statistics</h1>
        <p className="text-gray-400">
          Real-time data volume metrics across analytical and operational databases
        </p>
      </header>

      <div className="grid grid-cols-1 md:grid-cols-2 gap-6">
        {/* ClickHouse Card */}
        <div className="bg-slate-800 rounded-lg p-6 border border-blue-500/30">
          <div className="flex items-center gap-3 mb-4">
            <div className="h-3 w-3 rounded-full bg-blue-500"></div>
            <h2 className="text-xl font-semibold text-blue-400">ClickHouse</h2>
            <span className="text-sm text-gray-500">Analytics Engine</span>
          </div>

          <div className="space-y-6">
            <div>
              <div className="text-5xl font-mono font-bold text-white">
                {formatNumber(stats.clickHouseMetrics.roundsTracked)}
              </div>
              <div className="text-sm text-gray-400 mt-1">Rounds Tracked</div>
            </div>

            <div>
              <div className="text-5xl font-mono font-bold text-white">
                {formatNumber(stats.clickHouseMetrics.playerMetricsTracked)}
              </div>
              <div className="text-sm text-gray-400 mt-1">Player Metrics Tracked</div>
            </div>
          </div>
        </div>

        {/* SQLite Card */}
        <div className="bg-slate-800 rounded-lg p-6 border border-green-500/30">
          <div className="flex items-center gap-3 mb-4">
            <div className="h-3 w-3 rounded-full bg-green-500"></div>
            <h2 className="text-xl font-semibold text-green-400">SQLite</h2>
            <span className="text-sm text-gray-500">Operational Database</span>
          </div>

          <div className="space-y-6">
            <div>
              <div className="text-5xl font-mono font-bold text-white">
                {formatNumber(stats.sqliteMetrics.serversTracked)}
              </div>
              <div className="text-sm text-gray-400 mt-1">Servers Tracked</div>
            </div>

            <div>
              <div className="text-5xl font-mono font-bold text-white">
                {formatNumber(stats.sqliteMetrics.playersTracked)}
              </div>
              <div className="text-sm text-gray-400 mt-1">Players Tracked</div>
            </div>
          </div>
        </div>
      </div>

      <footer className="mt-6 text-center text-sm text-gray-500">
        Last updated: {lastUpdate?.toLocaleString('en-US', {
          dateStyle: 'medium',
          timeStyle: 'short'
        })}
        {' · '}
        Auto-refresh every 5 minutes
      </footer>
    </div>
  );
}
```

## Additional Enhancement Ideas

### 1. Historical Trend Visualization
Add a small sparkline chart showing growth over time:
- Store last 24 hours of counts in local storage
- Display mini line chart below each metric
- Useful for seeing growth patterns at a glance

### 2. Database Health Indicators
Add visual health badges:
- **Green:** Last update < 5 minutes ago
- **Yellow:** 5-15 minutes ago
- **Red:** > 15 minutes (indicates potential backend issue)

### 3. Comparative Metrics
Show derived statistics:
- **Average rounds per player:** `roundsTracked / playersTracked`
- **Metrics per round:** `playerMetricsTracked / roundsTracked` (shows sampling density)
- **Players per server:** `playersTracked / serversTracked`

### 4. Export Functionality
Add a "Download Report" button:
- Export current stats as JSON
- Generate markdown summary
- Useful for sharing system scale with stakeholders

### 5. Milestone Celebrations
Add visual flair when crossing thresholds:
- "🎉 1 Million Rounds Tracked!"
- "🚀 100,000 Players Tracked!"
- Could use confetti animation on first load after milestone

## Performance Optimization Notes

### Caching Strategy
The backend endpoint uses Redis caching with a 5-minute TTL:
```
Cache Key: "app:system:stats:v1"
TTL: 300 seconds (5 minutes)
```

**Frontend Implications:**
- Don't cache on the frontend (rely on backend cache)
- Use `cache: 'no-store'` in fetch if using Next.js App Router
- Auto-refresh interval should align with backend cache TTL

### Error Handling
Implement graceful degradation:

```javascript
try {
  const response = await fetch('/stats/app/systemstats');
  if (!response.ok) {
    throw new Error(`HTTP ${response.status}`);
  }
  const data = await response.json();
  setStats(data);
} catch (error) {
  // Keep displaying last successful data
  console.error('Failed to fetch stats:', error);
  // Show subtle error indicator without disrupting UX
}
```

## Accessibility Considerations

1. **Semantic HTML:** Use proper heading hierarchy (h1 → h2 → h3)
2. **ARIA Labels:** Add descriptive labels for screen readers
   ```html
   <div aria-label="ClickHouse rounds tracked: 1,234,567">
   ```
3. **Keyboard Navigation:** Ensure all interactive elements are keyboard accessible
4. **Color Contrast:** Verify text meets WCAG AA standards (4.5:1 for body text)
5. **Motion Preferences:** Respect `prefers-reduced-motion` for auto-refresh animations

## Testing Checklist

- [ ] Verify numbers format correctly with commas
- [ ] Test auto-refresh works after 5 minutes
- [ ] Confirm loading state displays properly
- [ ] Test error state when API is unreachable
- [ ] Verify responsive layout on mobile/tablet/desktop
- [ ] Check accessibility with screen reader
- [ ] Test with extremely large numbers (billions)
- [ ] Verify timestamp displays in user's local timezone

## API Integration Notes

**Base URL:** Use your existing API base URL (likely matches your other API calls)

**Authentication:** If your app requires auth tokens, ensure they're included:
```javascript
fetch('/stats/app/systemstats', {
  headers: {
    'Authorization': `Bearer ${token}`,
  },
});
```

**TypeScript Types:** The response types are already defined in the sample code above.

---

## Summary

This System Statistics page provides transparency into your data infrastructure's scale and serves as a "pulse check" for the health of your tracking system. The dual-database architecture showcases a production-grade design that separates operational concerns (SQLite) from analytical workloads (ClickHouse).

The implementation should be straightforward (< 200 lines of code) while providing immediate value to users interested in understanding the scope of data being processed. The auto-refresh mechanism ensures data stays current without manual intervention, and the clear visual distinction between database types helps users understand your technical architecture at a glance.

**Key Takeaway:** This is a simple but powerful dashboard that demonstrates technical sophistication through clean data presentation, not through complex visualizations. Let the numbers speak for themselves.
