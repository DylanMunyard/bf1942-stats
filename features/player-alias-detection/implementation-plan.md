# Player Alias Detection Engine - Implementation Plan

## Overview
Build a comprehensive player similarity/alias detection system that identifies potential stat whores (players with multiple accounts). The system combines:
- Statistical analysis (K/D, kills, map performance)
- Behavioral patterns (play times, server affinity, ping)
- Network analysis (Neo4j - teammate relationships)
- Temporal consistency (no co-observation, similar session timing)

## Data Sources Available

### From SQLite (PlayerStatsMonthly, PlayerSessions)
- K/D ratio, kill rate, total kills/deaths
- Overall scores, score per round
- Total play time, first/last seen
- Map-specific stats (K/D per map)
- Server insights (play time per server, server-specific K/D)
- Average ping per server
- Session data (timestamps, servers)

### From Neo4j
- PLAYED_WITH relationships (sessionCount, servers, timestamps)
- Player network shape (teammates, mutual connections)
- Community membership
- Network centrality metrics

## Similarity Dimensions

### 1. Stat Similarity (30%)
- Overall K/D ratio (normalized)
- Kill rate (kills/session) (normalized)
- Score per round (normalized)
- Map-specific K/D patterns (cosine similarity across map vectors)
- Server-specific performance patterns

### 2. Network Similarity (25%)
- Teammate overlap (Jaccard similarity)
- Shared mutual connections
- Network centrality comparison
- Similar teammate stat profiles

### 3. Behavioral Similarity (20%)
- Play time distribution (hour-of-day patterns)
- Server affinity (which servers, how much time)
- Session frequency and duration patterns
- Weapon/loadout preferences (if tracked)

### 4. Temporal Consistency (15%)
- **Zero temporal overlap**: They should never be in same session
- **Inverted activity**: When one stops, the other starts
- **Session timing similarity**: Play at similar times
- **Duration patterns**: Similar session lengths

### 5. Ping Consistency (10%)
- Average ping on common servers should be nearly identical
- Ping variance patterns

## Implementation Architecture

```
AliasSuspicionController (API endpoint)
    ↓
PlayerAliasDetectionService (composite scoring)
    ├── StatSimilarityCalculator
    ├── BehavioralPatternAnalyzer
    ├── Neo4jNetworkAnalyzer
    └── TemporalConsistencyValidator
    ↓
Supporting Services (existing)
    ├── ISqlitePlayerStatsService (stats)
    ├── IPlayerRelationshipService (Neo4j)
    ├── IPlayerRelationshipService (teammates, network)
```

## API Design

### Endpoint
```
GET /stats/alias-detection/compare
  ?player1=PlayerName1
  &player2=PlayerName2
  &lookBackDays=90
```

### Response Model
```csharp
public class PlayerAliasSuspicionReport
{
    // Overall scores
    public double OverallSimilarityScore { get; set; }      // 0-1 (higher = more similar)
    public AliasSuspicionLevel SuspicionLevel { get; set; } // Unrelated, Potential, Likely, VeryLikely

    // Individual dimensions
    public StatSimilarityAnalysis StatAnalysis { get; set; }
    public BehavioralAnalysis BehavioralAnalysis { get; set; }
    public NetworkAnalysis NetworkAnalysis { get; set; }
    public TemporalAnalysis TemporalAnalysis { get; set; }

    // Red flags
    public List<string> RedFlags { get; set; }
    public List<string> GreenFlags { get; set; } // Indicators they're different people

    // Supporting data
    public DateTime AnalysisTimestamp { get; set; }
    public int DaysAnalyzed { get; set; }
}

public enum AliasSuspicionLevel
{
    Unrelated,       // Score < 0.50 - Different players
    Potential,       // Score 0.50-0.70 - Could be related
    Likely,          // Score 0.70-0.85 - Probably same person
    VeryLikely       // Score > 0.85 - Almost certainly same person
}
```

## Calculation Strategy

1. **Normalize all stats** to 0-1 scale within context
2. **Calculate each dimension's score** independently
3. **Apply weighted formula** for overall score
4. **Identify red flags** from patterns
5. **Confidence adjustment** based on data quality (lookback period, games played)

## Red Flags (Increase suspicion)
- No temporal overlap (never in same session)
- Identical server affinity patterns
- Identical map-specific performance
- Similar play time distribution
- High network similarity + no relationship
- Similar teammate interaction patterns
- Ping variance on same servers < threshold

## Green Flags (Decrease suspicion)
- Significant temporal overlap
- Different server preferences
- Very different K/D patterns
- Different play time schedules
- Unique teammates per player
- Geographic evidence (ping differences on same server)

## Performance Considerations

- **Cache friendly**: Only hit Neo4j for network analysis if stat score > 0.6
- **Data aggregation**: Pre-compute monthly patterns where possible
- **Query limits**: 7-day lookback for session-level analysis, 90-day for trends
- **Batch comparison**: Support comparing one player against list of suspects

## Phase Implementation

### Phase 1: Core Similarity Engine ✅ COMPLETED
- [x] Stat similarity calculator (K/D, kill rate, map patterns)
- [x] Behavioral pattern analyzer (play times, servers, ping)
- [x] Basic report generation
- [x] StatSimilarityCalculator.cs
- [x] BehavioralPatternAnalyzer.cs

### Phase 2: Neo4j Integration ✅ COMPLETED
- [x] Network similarity calculator (teammates, mutual connections)
- [x] Temporal consistency validator
- [x] Red flag detection
- [x] Neo4jNetworkAnalyzer.cs

### Phase 3: API & Frontend ✅ COMPLETED (API only)
- [x] Compare endpoint (`GET /stats/alias-detection/compare`)
- [x] Explain endpoint (`POST /stats/alias-detection/explain`)
- [ ] Batch comparison endpoint (placeholder for future)
- [ ] Simple report UI (for mods/admins) - *Optional, frontend work*

### Phase 4: Advanced Features
- [ ] Suspicion pool (find potential aliases for a player)
- [ ] Time-series similarity tracking
- [ ] ML-based confidence scoring (optional, future)

## Success Metrics
- Ability to correctly identify known aliases
- Low false positive rate for legitimate different players
- Performance < 2s for single comparison
- Useful red flags for moderator investigation
