# Neo4j Integration for BF1942 Stats

This module integrates Neo4j graph database capabilities to analyze player relationships and gaming patterns in BF1942 server data.

## Overview

The Neo4j integration creates a graph representation of gaming relationships that are difficult to analyze with traditional SQL queries:

- **Player Networks**: Who plays with whom, and how often
- **Server Communities**: Regular players on specific servers  
- **Geographic Patterns**: Cross-border gaming interactions
- **Skill-Based Matching**: Finding players with similar performance levels
- **Map Competitiveness**: Analyzing balanced vs. unbalanced matches

## Configuration

Add the following to your `appsettings.json`:

```json
{
  "Neo4j": {
    "Enabled": true,
    "Uri": "bolt://localhost:7687",
    "Username": "neo4j",
    "Password": "your-password",
    "Database": "neo4j",
    "MaxConnectionPoolSize": 100,
    "ConnectionTimeout": "00:00:30",
    "MaxIdleTime": "00:10:00"
  }
}
```

Or via environment variables:
```bash
export Neo4j__Enabled=true
export Neo4j__Uri=bolt://localhost:7687
export Neo4j__Username=neo4j
export Neo4j__Password=your-password
```

## Graph Data Model

### Node Types

- **Player**: Individual gamers with performance statistics
- **Server**: Game servers with geographic and capacity info
- **Map**: Game maps with type classification
- **Session**: Individual gaming sessions
- **GeographicRegion**: Countries/regions for location analysis
- **TimeSlot**: Time periods for temporal analysis

### Relationship Types

- **PLAYED_WITH**: Players who were in the same sessions
- **FREQUENTS**: Players who regularly use specific servers
- **PREFERS**: Maps that players perform well on
- **PERFORMS_BEST_IN**: Time periods when players excel
- **OCCURRED_ON**: Sessions that happened on servers
- **PLAYED_ON_MAP**: Sessions on specific maps

## API Endpoints

### Setup & Management
- `POST /api/neo4j/test-connection` - Test Neo4j connectivity
- `POST /api/neo4j/initialize` - Create database constraints
- `POST /api/neo4j/sync-data` - Sync last month's data from SQLite
- `DELETE /api/neo4j/clear-data` - Clear all graph data

### Analytics Queries
- `GET /api/neo4j/analytics/server-communities` - Find server player communities
- `GET /api/neo4j/analytics/similar-players` - Players with similar skill levels
- `GET /api/neo4j/analytics/cross-border-battles` - Geographic gaming patterns
- `GET /api/neo4j/analytics/map-competitiveness` - Balanced map analysis
- `GET /api/neo4j/analytics/player/{name}/network` - Player network statistics
- `GET /api/neo4j/analytics/player/{name}/recommendations` - Friend suggestions

## Setup Instructions

### Local Development with Docker

1. **Start Neo4j with Docker**:
```bash
docker run -d \\
  --name neo4j-bf1942 \\
  -p 7474:7474 -p 7687:7687 \\
  -e NEO4J_AUTH=neo4j/bf1942stats \\
  -e NEO4J_PLUGINS='["apoc"]' \\
  neo4j:5.28
```

2. **Configure application**:
```json
{
  "Neo4j": {
    "Enabled": true,
    "Uri": "bolt://localhost:7687", 
    "Username": "neo4j",
    "Password": "bf1942stats"
  }
}
```

3. **Initialize and sync data**:
```bash
# Test connection
curl -X POST http://localhost:5000/api/neo4j/test-connection

# Initialize constraints
curl -X POST http://localhost:5000/api/neo4j/initialize

# Sync last month's data (trial dataset)
curl -X POST http://localhost:5000/api/neo4j/sync-data
```

### Neo4j Browser

Access the Neo4j browser at http://localhost:7474 to explore your graph data with Cypher queries.

Example queries:
```cypher
// Show all node types and counts
MATCH (n) RETURN labels(n) as nodeType, count(*) as count

// Find most connected players
MATCH (p:Player)-[r:PLAYED_WITH]-()
RETURN p.name, count(r) as connections
ORDER BY connections DESC LIMIT 10

// Server communities
MATCH (p:Player)-[f:FREQUENTS]->(s:Server)
WHERE f.session_count >= 3
RETURN s.name, collect(p.name) as regulars
ORDER BY size(regulars) DESC
```

## Performance Considerations

- **Data Volume**: The trial syncs ~10,000 recent sessions to avoid overwhelming Neo4j
- **Relationship Density**: Player-to-player relationships are computed from concurrent sessions
- **Memory Usage**: Neo4j is memory-intensive; ensure adequate RAM allocation
- **Query Complexity**: Graph traversals can be expensive; queries include limits and indexes

## Gaming Use Cases

### 1. Player Community Detection
Identify tight-knit gaming groups and server regulars for community building and event organization.

### 2. Skill-Based Matchmaking
Find players with similar K/D ratios who haven't played together for balanced matches.

### 3. Geographic Gaming Analysis  
Understand how players from different regions interact and compete.

### 4. Friend Recommendations
Suggest new teammates based on mutual gaming connections.

### 5. Map Meta Analysis
Discover which maps create the most competitive and balanced gameplay.

### 6. Temporal Gaming Patterns
Analyze when players perform best for optimal match scheduling.

## Data Synchronization

The `SyncLastMonthDataAsync()` method:

1. Extracts recent PlayerSessions from SQLite
2. Creates unique Player, Server, Map, and Geographic nodes
3. Builds PLAYED_WITH relationships from concurrent sessions
4. Creates FREQUENTS relationships from player-server interactions
5. Calculates performance metrics and K/D ratios

This hybrid approach keeps SQLite for transactional operations while leveraging Neo4j for relationship analysis.

## Security Notes

- Neo4j credentials should be stored securely (environment variables, secrets)
- The API endpoints are currently unprotected - consider adding authentication
- Neo4j browser access should be restricted in production environments
- Regular backups of graph data are recommended for production use

## Troubleshooting

**Connection Issues**:
- Verify Neo4j is running and accessible
- Check URI format (bolt:// vs neo4j://)
- Confirm authentication credentials

**Performance Issues**:
- Monitor memory usage during large syncs
- Consider limiting data size for trials
- Add indexes on frequently queried properties

**Data Inconsistencies**:
- Clear and re-sync data if relationships seem incorrect
- Check that SQLite data is clean and consistent
- Verify constraint creation succeeded