# Neo4j Back-fill Testing Guide

## Overview
This document provides queries and methods to test and verify the Neo4j player relationship back-fill.

## Graph Structure

### Nodes
- **Player**: `(:Player {name, firstSeen, lastSeen, totalMinutes})`
- **Server**: `(:Server {guid, game, name})`

### Relationships
- **PLAYED_WITH**: `(Player)-[:PLAYED_WITH {sessionCount, totalMinutes, firstPlayedTogether, lastPlayedTogether, avgScoreDiff, serverGuids[]}]->(Player)`
- **PLAYS_ON**: `(Player)-[:PLAYS_ON {sessionCount, totalMinutes, lastPlayed}]->(Server)`

---

## Basic Verification Queries

### 1. Check Total Counts
```cypher
// Count all nodes and relationships
MATCH (p:Player) RETURN count(p) as totalPlayers;
MATCH (s:Server) RETURN count(s) as totalServers;
MATCH ()-[r:PLAYED_WITH]->() RETURN count(r) as totalCoPlayRelationships;
MATCH ()-[r:PLAYS_ON]->() RETURN count(r) as totalServerRelationships;
```

### 2. Sample Random Data
```cypher
// Get random sample of players
MATCH (p:Player) 
RETURN p.name, p.totalMinutes, p.firstSeen, p.lastSeen 
ORDER BY rand() 
LIMIT 10;

// Get random sample of servers
MATCH (s:Server) 
RETURN s.name, s.guid, s.game 
ORDER BY rand() 
LIMIT 5;
```

### 3. Check Data Completeness
```cypher
// Check for players with no relationships
MATCH (p:Player)
WHERE NOT (p)-[:PLAYED_WITH]-()
RETURN count(p) as playersWithNoCoPlayers;

// Check for players with no server relationships
MATCH (p:Player)
WHERE NOT (p)-[:PLAYS_ON]->()
RETURN count(p) as playersWithNoServers;

// Check for orphaned servers (no players)
MATCH (s:Server)
WHERE NOT ()-[:PLAYS_ON]->(s)
RETURN s.guid, s.name;
```

---

## Testing Player Relationships

### 4. Players with Most Overlapping Play Time
```cypher
// Top 20 player pairs by total minutes played together
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name  // Avoid duplicates (relationships are bidirectional)
RETURN 
  p1.name as player1, 
  p2.name as player2,
  r.totalMinutes as minutesTogether,
  r.sessionCount as sessions,
  r.firstPlayedTogether as firstMet,
  r.lastPlayedTogether as lastPlayed,
  r.avgScoreDiff as avgScoreDiff
ORDER BY r.totalMinutes DESC
LIMIT 20;
```

### 5. Most Connected Players (Highest Co-Player Count)
```cypher
// Players who have played with the most different people
MATCH (p:Player)-[:PLAYED_WITH]->()
RETURN 
  p.name, 
  count(*) as coPlayerCount,
  p.totalMinutes as totalPlayTime
ORDER BY coPlayerCount DESC
LIMIT 20;
```

### 6. Most Frequent Co-Players for Specific Player
```cypher
// Replace 'PlayerName' with actual player name
MATCH (p:Player {name: 'PlayerName'})-[r:PLAYED_WITH]->(other:Player)
RETURN 
  other.name as coPlayer,
  r.sessionCount as sessions,
  r.totalMinutes as minutesTogether,
  r.lastPlayedTogether as lastPlayed,
  size(r.serverGuids) as sharedServerCount
ORDER BY r.totalMinutes DESC
LIMIT 20;
```

### 7. Longest Running Friendships
```cypher
// Pairs who have been playing together the longest
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
WITH p1, p2, r, 
     duration.between(
       datetime(r.firstPlayedTogether), 
       datetime(r.lastPlayedTogether)
     ) as relationship_duration
RETURN 
  p1.name as player1,
  p2.name as player2,
  r.firstPlayedTogether as firstMet,
  r.lastPlayedTogether as lastPlayed,
  relationship_duration.days as daysKnown,
  r.sessionCount as sessions,
  r.totalMinutes as totalMinutes
ORDER BY relationship_duration.days DESC
LIMIT 20;
```

### 8. Recent New Connections (Met in Last 7 Days)
```cypher
// Find players who started playing together recently
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
  AND datetime(r.firstPlayedTogether) > datetime() - duration('P7D')
RETURN 
  p1.name as player1,
  p2.name as player2,
  r.firstPlayedTogether as metOn,
  r.sessionCount as sessionsSoFar,
  r.totalMinutes as minutesTogether
ORDER BY r.firstPlayedTogether DESC
LIMIT 20;
```

---

## Testing Server Relationships

### 9. Most Popular Servers by Player Count
```cypher
// Servers with most unique players
MATCH (s:Server)<-[:PLAYS_ON]-(p:Player)
RETURN 
  s.name as serverName,
  s.guid as serverGuid,
  s.game as game,
  count(p) as uniquePlayers
ORDER BY uniquePlayers DESC
LIMIT 20;
```

### 10. Most Active Servers by Total Play Time
```cypher
// Servers with most total player minutes
MATCH (s:Server)<-[r:PLAYS_ON]-(p:Player)
RETURN 
  s.name as serverName,
  s.guid as serverGuid,
  sum(r.totalMinutes) as totalMinutes,
  count(p) as playerCount,
  avg(r.totalMinutes) as avgMinutesPerPlayer
ORDER BY totalMinutes DESC
LIMIT 20;
```

### 11. Shared Servers for Two Players
```cypher
// Find servers where two specific players have both played
MATCH (p1:Player {name: 'Player1'})-[:PLAYS_ON]->(s:Server)<-[:PLAYS_ON]-(p2:Player {name: 'Player2'})
RETURN 
  s.name as serverName,
  s.guid as serverGuid
ORDER BY s.name;
```

### 12. Player's Server History
```cypher
// All servers a player has visited
MATCH (p:Player {name: 'PlayerName'})-[r:PLAYS_ON]->(s:Server)
RETURN 
  s.name as serverName,
  s.game as game,
  r.sessionCount as sessions,
  r.totalMinutes as minutesPlayed,
  r.lastPlayed as lastVisit
ORDER BY r.totalMinutes DESC;
```

---

## Advanced Relationship Queries

### 13. Player Communities (Friends of Friends)
```cypher
// Find players connected through mutual friends (2-hop relationships)
MATCH (p:Player {name: 'PlayerName'})-[:PLAYED_WITH]-(friend:Player)-[:PLAYED_WITH]-(friend_of_friend:Player)
WHERE p <> friend_of_friend
  AND NOT (p)-[:PLAYED_WITH]-(friend_of_friend)  // Not direct friends yet
RETURN 
  friend_of_friend.name as potentialConnection,
  count(DISTINCT friend) as mutualFriends,
  collect(DISTINCT friend.name) as throughPlayers
ORDER BY mutualFriends DESC
LIMIT 20;
```

### 14. Most Balanced Rivalries (Similar Scores)
```cypher
// Player pairs with most similar performance (low avgScoreDiff)
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
  AND r.sessionCount >= 5  // Minimum sample size
RETURN 
  p1.name as player1,
  p2.name as player2,
  r.avgScoreDiff as scoreDifference,
  r.sessionCount as sessions,
  r.totalMinutes as minutesTogether
ORDER BY r.avgScoreDiff ASC
LIMIT 20;
```

### 15. One-Sided Dominance (High Score Difference)
```cypher
// Pairs where one player consistently outscores the other
MATCH (p1:Player)-[r:PLAYED_WITH]->(p2:Player)
WHERE p1.name < p2.name
  AND r.sessionCount >= 5  // Minimum sample size
RETURN 
  p1.name as player1,
  p2.name as player2,
  r.avgScoreDiff as avgScoreDiff,
  r.sessionCount as sessions,
  r.totalMinutes as minutesTogether
ORDER BY r.avgScoreDiff DESC
LIMIT 20;
```

### 16. Most Active Time Periods
```cypher
// When were the most relationships formed?
MATCH ()-[r:PLAYED_WITH]->()
WITH r.firstPlayedTogether as meetDate
WITH date(datetime(meetDate)) as day, count(*) as relationshipsFormed
RETURN day, relationshipsFormed
ORDER BY relationshipsFormed DESC
LIMIT 20;
```

---

## Data Quality Checks

### 17. Relationship Symmetry Check
```cypher
// Verify PLAYED_WITH relationships are bidirectional with same data
MATCH (p1:Player)-[r1:PLAYED_WITH]->(p2:Player)
OPTIONAL MATCH (p2)-[r2:PLAYED_WITH]->(p1)
WHERE r2 IS NULL 
   OR r1.sessionCount <> r2.sessionCount
   OR r1.totalMinutes <> r2.totalMinutes
RETURN 
  p1.name as player1,
  p2.name as player2,
  r1.sessionCount as p1_to_p2_sessions,
  r2.sessionCount as p2_to_p1_sessions,
  r1.totalMinutes as p1_to_p2_minutes,
  r2.totalMinutes as p2_to_p1_minutes
LIMIT 10;
```

### 18. Timeline Consistency Check
```cypher
// Check for logical timeline issues (lastPlayed before firstPlayed)
MATCH ()-[r:PLAYED_WITH]->()
WHERE datetime(r.lastPlayedTogether) < datetime(r.firstPlayedTogether)
RETURN count(r) as timelineErrors;

MATCH ()-[r:PLAYS_ON]->()
WHERE datetime(r.lastPlayed) < datetime() - duration('P365D')  // Old data
RETURN count(r) as oldRelationships;
```

### 19. Data Range Check
```cypher
// Min/Max dates in the dataset
MATCH ()-[r:PLAYED_WITH]->()
RETURN 
  min(r.firstPlayedTogether) as earliestConnection,
  max(r.lastPlayedTogether) as latestConnection,
  min(r.totalMinutes) as minMinutes,
  max(r.totalMinutes) as maxMinutes,
  avg(r.totalMinutes) as avgMinutes,
  min(r.sessionCount) as minSessions,
  max(r.sessionCount) as maxSessions,
  avg(r.sessionCount) as avgSessions;
```

### 20. Orphaned Player Check
```cypher
// Players with totalMinutes but no relationships (potential data issue)
MATCH (p:Player)
WHERE p.totalMinutes > 0
  AND NOT (p)-[:PLAYED_WITH]-()
RETURN 
  p.name,
  p.totalMinutes,
  p.firstSeen,
  p.lastSeen
LIMIT 20;
```

---

## Performance Testing Queries

### 21. Degree Distribution
```cypher
// How many connections does the average player have?
MATCH (p:Player)
OPTIONAL MATCH (p)-[:PLAYED_WITH]->()
WITH p, count(*) as connections
RETURN 
  connections,
  count(p) as playerCount
ORDER BY connections DESC;
```

### 22. Heavy Hitters
```cypher
// Players with unusually high play time (potential data quality issues)
MATCH (p:Player)
WHERE p.totalMinutes > 10000  // More than ~167 hours
RETURN 
  p.name,
  p.totalMinutes,
  p.totalMinutes / 60.0 as totalHours,
  p.firstSeen,
  p.lastSeen
ORDER BY p.totalMinutes DESC
LIMIT 20;
```

---

## Running Queries

### Via Neo4j Browser
1. Open Neo4j Browser: `http://localhost:7474`
2. Connect using credentials from appsettings
3. Paste and run any query above
4. Use the graph visualization or table view

### Via API (Future)
These queries could be exposed as API endpoints in `PlayerRelationshipService.cs` for programmatic testing.

### Via Cypher Shell
```bash
# Connect to Neo4j
cypher-shell -u neo4j -p your_password -d bf1942stats

# Run a query
MATCH (p:Player) RETURN count(p);
```

---

## Expected Results (After Full Back-fill)

Based on your production data (~100M observations):

- **Players**: Thousands of unique player nodes
- **Servers**: Hundreds of server nodes  
- **PLAYED_WITH relationships**: Tens of thousands to hundreds of thousands (depends on player overlap)
- **PLAYS_ON relationships**: One per player-server combination

### Sanity Checks
- Every `PLAYED_WITH` relationship should be bidirectional
- `totalMinutes` should never be negative
- `lastPlayedTogether` >= `firstPlayedTogether`
- `sessionCount` >= 1
- Players with high `totalMinutes` should have many `PLAYED_WITH` relationships

---

## Troubleshooting

### Low Relationship Count
- Check if rounds were filtered out (deleted sessions)
- Verify date range in back-fill
- Check if players were solo (no other players in round)

### Missing Players
- Verify player names are normalized consistently
- Check for case sensitivity issues
- Look at raw PlayerObservations data

### Performance Issues
- Ensure indexes were created (check migration status)
- Use `PROFILE` or `EXPLAIN` before queries
- Limit result sets for exploratory queries

### Memory Issues
- Monitor Neo4j heap usage during queries
- Use pagination for large result sets
- Consider query optimization

---

## Next Steps

1. **Run Basic Verification** (queries 1-3)
2. **Spot Check Interesting Players** (queries 4-8)
3. **Verify Data Quality** (queries 17-20)
4. **Explore Patterns** (queries 13-16)
5. **Build API Endpoints** for the most useful queries
6. **Create Visualizations** using Neo4j Bloom or custom UI

