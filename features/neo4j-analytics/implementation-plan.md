# Neo4j Analytics Implementation Plan

## Overview
This document outlines new UI components and screens to leverage the Neo4j graph database for player relationship analytics in the BF1942 stats system.

## Data Available

### Nodes
- **Player**: name, firstSeen, lastSeen, totalSessions
- **Server**: guid, name, game

### Relationships
- **PLAYED_WITH**: sessionCount, firstPlayedTogether, lastPlayedTogether, servers[]
- **PLAYS_ON**: sessionCount, lastPlayed

## New UI Components & Screens

### 1. Player Network Graph Visualization
**Location**: `/players/{playerName}/network`

**Features**:
- Interactive force-directed graph showing player's network
- Node size based on play frequency
- Edge thickness based on session count
- Color coding for:
  - Recent connections (last 7 days) - green
  - Active connections (last 30 days) - blue
  - Dormant connections (>30 days) - gray
- Click nodes to navigate to player profiles
- Filters:
  - Time range
  - Minimum session count
  - Server filter
- Stats overlay:
  - Total connections
  - Average sessions per connection
  - Most frequent teammates

**Implementation**:
- Use D3.js or vis.js for graph rendering
- Neo4j query: Get all PLAYED_WITH relationships for a player

### 2. Community Detection Dashboard
**Location**: `/analytics/communities`

**Features**:
- Grid view of detected player communities
- For each community:
  - Top 5 most connected players
  - Primary servers
  - Activity heatmap
  - Member count
  - Formation date
- Community comparison tool
- Export community lists

**Implementation**:
- Use Neo4j's community detection algorithms (Louvain, Label Propagation)
- Background job to compute communities daily
- Cache results in PostgreSQL

### 3. Server Social Health Dashboard
**Location**: `/servers/{serverId}/social`

**Features**:
- Social metrics for each server:
  - Community cohesion score
  - New player integration rate
  - Player retention metrics
  - Cross-pollination with other servers
- Visualizations:
  - Player flow diagram (joins/leaves over time)
  - Relationship density heatmap
  - Top squads/groups
- Alerts:
  - Declining community health
  - Mass player exodus

**Implementation**:
- Aggregate Neo4j data into time-series metrics
- Store in PostgreSQL for fast querying

### 4. Squad Finder
**Location**: `/squad-finder`

**Features**:
- Match players based on:
  - Play time overlap
  - Server preferences
  - Skill level similarity
  - No existing connection (find new teammates)
- Recommendation list with:
  - Compatibility score
  - Common servers
  - Typical play times
  - One-click Discord invite

**Implementation**:
- Neo4j query: Find players who play on same servers but haven't played together
- Factor in player stats from PostgreSQL

### 5. Player Relationship Timeline
**Location**: `/players/{player1}/vs/{player2}`

**Features**:
- Detailed relationship view between two players
- Timeline showing:
  - First encounter
  - Session frequency over time
  - Servers played on together
  - Head-to-head stats (if available)
- Shared connections (mutual friends)
- Predict next play session (ML opportunity)

**Implementation**:
- Simple Neo4j path queries
- Combine with match data for detailed stats

### 6. Global Network Statistics
**Location**: `/analytics/network`

**Features**:
- Overall community health metrics
- Network statistics:
  - Average degree (connections per player)
  - Clustering coefficient
  - Network diameter
  - Component sizes
- Trending relationships (new popular duos/squads)
- Historical network growth

**Implementation**:
- Periodic Neo4j analytics jobs
- Store results in time-series format

### 7. Player Migration Flows
**Location**: `/analytics/migrations`

**Features**:
- Sankey diagram showing player movement between servers
- Time-based animation
- Filters by:
  - Game type
  - Time period
  - Player cohort
- Identify server lifecycle patterns

**Implementation**:
- Aggregate PLAYS_ON relationships over time
- Use D3.js Sankey plugin

### 8. Social Leaderboards
**Location**: `/leaderboards/social`

**Features**:
- Most connected players
- Strongest relationships (by session count)
- Community builders (high betweenness centrality)
- Server ambassadors (play on most servers)
- Rising stars (fastest growing networks)

**Implementation**:
- Daily Neo4j analytics queries
- Cache in Redis for performance

## API Endpoints Needed

### Player Relationships
- `GET /api/players/{name}/network` - Get player's network
- `GET /api/players/{name}/network/stats` - Network statistics
- `GET /api/players/{player1}/relationship/{player2}` - Specific relationship

### Community Analytics
- `GET /api/analytics/communities` - List detected communities
- `GET /api/analytics/communities/{id}` - Community details
- `GET /api/analytics/network/stats` - Global network stats

### Server Social Analytics
- `GET /api/servers/{guid}/social` - Server social metrics
- `GET /api/servers/{guid}/communities` - Server's communities

### Recommendations
- `GET /api/squad-finder/recommendations` - Squad recommendations
- `POST /api/squad-finder/feedback` - Improve recommendations

## Technical Implementation Notes

### Performance Considerations
1. **Caching Strategy**:
   - Cache Neo4j query results in Redis
   - Daily computation of expensive metrics
   - Real-time queries only for specific player lookups

2. **Query Optimization**:
   - Use Neo4j indexes effectively
   - Limit graph traversal depth
   - Paginate large result sets

3. **Data Freshness**:
   - ETL runs hourly for recent data
   - Full recomputation daily
   - Real-time updates for active sessions

### Frontend Technologies
- **Graph Visualization**: D3.js or vis.js
- **Time Series**: Chart.js or ECharts
- **Flow Diagrams**: D3.js Sankey
- **Heatmaps**: Custom canvas rendering
- **State Management**: Pinia stores for caching

### Backend Architecture
```
API Controller
    ↓
Relationship Service (interface)
    ↓
Neo4j Service (queries)
    ↓
Cache Layer (Redis)
    ↓
Background Jobs (analytics)
```

## Priority Order

1. **Phase 1** (Core Features): ✅ COMPLETED
   - Player Network Graph ✅
   - Basic relationship queries ✅
   - API endpoints ✅

2. **Phase 2** (Analytics): ✅ COMPLETED
   - Community Detection ✅
   - Server Social Health ✅
   - Caching layer ✅

3. **Phase 3** (Advanced): ✅ COMPLETED
   - Squad Finder ✅
   - Migration Flows ✅
   - ML predictions (deferred)

4. **Phase 4** (Polish):
   - Real-time updates
   - Advanced visualizations
   - Mobile optimization

## Success Metrics

- User engagement with network features
- Squad finder match success rate
- Community retention improvement
- Page load performance
- Query response times

## Completed Features

### Phase 1: Core Features
- ✅ IPlayerRelationshipService interface
- ✅ PlayerRelationshipService with Neo4j queries
- ✅ API endpoints:
  - GET /stats/relationships/players/{playerName}/teammates
  - GET /stats/relationships/players/{playerName}/potential-connections
  - GET /stats/relationships/players/{player1}/relationship/{player2}
  - GET /stats/relationships/players/{player1}/shared-servers/{player2}
  - GET /stats/relationships/players/{playerName}/recent-connections
  - GET /stats/relationships/players/{playerName}/network-stats
  - GET /stats/relationships/players/{playerName}/network-graph
  - GET /stats/relationships/servers/{serverGuid}/social-stats

### Phase 2: Analytics
- ✅ Community detection with Louvain algorithm
- ✅ CommunityDetectionService background job (runs daily)
- ✅ Community API endpoints:
  - GET /stats/communities
  - GET /stats/communities/{communityId}
  - GET /stats/communities/players/{playerName}
  - POST /stats/communities/detect
- ✅ Server social health metrics
- ✅ Redis caching layer with:
  - IRelationshipCacheService
  - RelationshipCacheService
  - CachedPlayerRelationshipService
- ✅ Cache invalidation strategies

### Phase 3: Advanced Features
- ✅ Squad Finder with:
  - GET /stats/squad-finder/players/{playerName}/recommendations
  - POST /stats/squad-finder/feedback
  - Compatibility scoring based on:
    - Common servers
    - Play time overlap
    - Mutual connections
  - Recommendation feedback tracking
- ✅ Player Migration Flows:
  - GET /stats/analytics/migrations/flow
  - GET /stats/analytics/migrations/server-lifecycle
  - Server lifecycle analysis (Growing/Stable/Declining/Dead)
  - Migration patterns between servers
  - Time-based flow data for Sankey diagrams

## API Summary

### Relationship Endpoints
- GET /stats/relationships/players/{playerName}/teammates
- GET /stats/relationships/players/{playerName}/potential-connections
- GET /stats/relationships/players/{player1}/relationship/{player2}
- GET /stats/relationships/players/{player1}/shared-servers/{player2}
- GET /stats/relationships/players/{playerName}/recent-connections
- GET /stats/relationships/players/{playerName}/network-stats
- GET /stats/relationships/players/{playerName}/network-graph
- GET /stats/relationships/servers/{serverGuid}/social-stats

### Community Endpoints
- GET /stats/communities
- GET /stats/communities/{communityId}
- GET /stats/communities/players/{playerName}
- POST /stats/communities/detect

### Squad Finder Endpoints
- GET /stats/squad-finder/players/{playerName}/recommendations
- POST /stats/squad-finder/feedback

### Migration Analytics Endpoints
- GET /stats/analytics/migrations/flow
- GET /stats/analytics/migrations/server-lifecycle

## Next Steps - Phase 4 (Frontend)

1. **Vue.js Components**:
   - Player network graph visualization (D3.js/vis.js)
   - Community grid dashboard
   - Squad finder recommendations UI
   - Server social health metrics
   - Migration flow Sankey diagram

2. **Integration**:
   - Add relationship data to existing player profiles
   - Add community badges to player cards
   - Integrate squad finder with Discord invites
   - Add network metrics to server pages

3. **Performance**:
   - Implement progressive loading for large graphs
   - Add WebSocket support for real-time updates
   - Optimize graph rendering for mobile