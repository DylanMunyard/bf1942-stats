# Player Alias Detection Engine - Implementation Summary

## Overview

A complete **player alias/similarity detection system** that identifies potential stat whores (players with multiple accounts) by analyzing:

1. **Statistical similarity** (30%) - K/D ratios, kill rates, map-specific performance
2. **Behavioral patterns** (20%) - Play times, server preferences, ping consistency
3. **Network analysis** (25%) - Teammate relationships, network shape via Neo4j
4. **Temporal consistency** (15%) - Session overlap, inverted activity patterns
5. **Ping consistency** (10%) - Geographic location evidence via ping patterns on same servers

## What Was Built

### Core Services

#### 1. **StatSimilarityCalculator** (`StatSimilarityCalculator.cs`)
Compares statistical performance between players:
- **K/D Ratio Similarity**: Normalized comparison with ratio-based scoring
- **Kill Rate Similarity**: Kills per session normalized
- **Map Performance**: Cosine similarity on K/D vectors across maps
- **Server Performance**: K/D patterns across commonly-played servers
- **Overall Score**: Weighted average (40% K/D, 25% kill rate, 15% score/round, 15% map, 5% server)

**Data Source**: `ISqlitePlayerStatsService` (SQLite PlayerStatsMonthly table)

#### 2. **BehavioralPatternAnalyzer** (`BehavioralPatternAnalyzer.cs`)
Analyzes player behavior patterns:
- **Play Time Overlap**: Hour-of-day distribution using Jensen-Shannon divergence
- **Server Affinity**: Jaccard similarity of server lists
- **Ping Consistency**: Average ping variance on common servers (geographic evidence)
- **Session Patterns**: Session frequency and duration similarity
- **Overall Score**: Weighted average (30% play time, 30% server, 20% ping, 20% session)

**Data Source**: `PlayerSession` and `PlayerObservation` (ping data)

#### 3. **Neo4jNetworkAnalyzer** (`Neo4jNetworkAnalyzer.cs`)
Analyzes player networks and temporal consistency:
- **Network Similarity**:
  - Teammate overlap (Jaccard similarity)
  - Mutual connections
  - Network shape comparison (degree centrality)
- **Temporal Consistency**:
  - Zero co-session check (should never play together)
  - Inverted activity patterns
  - Activity gap consistency
- **Overall Scores**: Separate scores for network (weighted 60/40 teammate/shape) and temporal

**Data Source**: Neo4j `PLAYED_WITH` relationships

#### 4. **PlayerAliasDetectionService** (`PlayerAliasDetectionService.cs`)
Orchestrates all analyzers:
- Calls all four similarity calculators
- Combines scores using weights: Stat(30%) + Behavior(20%) + Network(25%) + Temporal(15%)
- **Overall Score**: 0-1 scale, higher = more similar
- **Suspicion Levels**:
  - **Unrelated**: < 0.50
  - **Potential**: 0.50-0.70
  - **Likely**: 0.70-0.85
  - **VeryLikely**: > 0.85
- Identifies red flags and green flags
- Calculates confidence score based on data volume

#### 5. **AliasDetectionController** (`AliasDetectionController.cs`)
REST API endpoints:

```
GET /stats/alias-detection/compare
  ?player1=PlayerName1
  &player2=PlayerName2
  &lookBackDays=90

Returns: PlayerAliasSuspicionReport

POST /stats/alias-detection/explain
  Takes: PlayerAliasSuspicionReport
  Returns: Human-readable explanation with recommendations
```

### Data Models

**PlayerAliasSuspicionReport** - Comprehensive analysis report containing:
- `OverallSimilarityScore` (0-1)
- `SuspicionLevel` (enum)
- `StatAnalysis` - Individual stats scores and analysis
- `BehavioralAnalysis` - Play pattern scores
- `NetworkAnalysis` - Teammate overlap and network shape
- `TemporalAnalysis` - Co-session and activity pattern scores
- `RedFlags[]` - Suspicious patterns (increase suspicion)
- `GreenFlags[]` - Evidence of different players (decrease suspicion)
- `AnalysisConfidence` (0-1, based on data volume)

## Red Flags (Increase Suspicion)

The system identifies these patterns as suspicious:
- ✅ K/D ratios nearly identical (> 0.85 similarity)
- ✅ Identical map performance patterns
- ✅ Play at nearly identical times of day
- ✅ Strong server affinity match
- ✅ Nearly identical ping on same servers
- ✅ Very high teammate overlap
- ✅ **High teammate overlap + zero direct connection** (classic alias pattern!)
- ✅ Zero temporal overlap + high teammate overlap (same person, different accounts)
- ✅ Nearly identical kill rates

## Green Flags (Decrease Suspicion)

Evidence that they're different players:
- ✅ Played together in multiple sessions
- ✅ Play at significantly different times
- ✅ Very different pings on same servers (different geographic locations)
- ✅ Different map-specific performance
- ✅ Played together (suggests different accounts)
- ✅ K/D ratios significantly different
- ✅ Different server preferences

## How It Works - Flow Diagram

```
ComparePlayers(player1, player2, lookBackDays)
    ↓
    ├─→ StatSimilarityCalculator.CalculateSimilarityAsync()
    │       ├─→ Get lifetime stats (K/D, kill rate, score)
    │       ├─→ Get map-specific stats
    │       ├─→ Get server insights
    │       └─→ Return StatSimilarityAnalysis
    │
    ├─→ BehavioralPatternAnalyzer.AnalyzeBehaviorAsync()
    │       ├─→ Get session data (play times)
    │       ├─→ Calculate hour-of-day distribution
    │       ├─→ Calculate server affinity
    │       ├─→ Calculate ping consistency (PlayerObservations)
    │       └─→ Return BehavioralAnalysis
    │
    ├─→ Neo4jNetworkAnalyzer.AnalyzeNetworkAndTemporalAsync()
    │       ├─→ Get teammate lists (Neo4j)
    │       ├─→ Calculate Jaccard similarity
    │       ├─→ Check for direct co-sessions
    │       ├─→ Calculate mutual connections
    │       ├─→ Check temporal overlap
    │       └─→ Return NetworkAnalysis + TemporalAnalysis
    │
    └─→ Calculate Overall Score
            ├─→ Stat(30%) + Behavior(20%) + Network(25%) + Temporal(15%)
            ├─→ Identify red/green flags
            ├─→ Determine suspicion level
            └─→ Return PlayerAliasSuspicionReport
```

## API Usage Examples

### Example 1: Basic Comparison
```bash
curl "http://localhost:5000/stats/alias-detection/compare?player1=StatWhore&player2=AltAccount&lookBackDays=90"
```

**Response**:
```json
{
  "player1": "StatWhore",
  "player2": "AltAccount",
  "overallSimilarityScore": 0.87,
  "suspicionLevel": "VeryLikely",
  "statAnalysis": {
    "score": 0.92,
    "kdRatioDifference": 0.89,
    "analysis": "K/D ratios nearly identical"
  },
  "behavioralAnalysis": {
    "score": 0.78,
    "playTimeOverlapScore": 0.81,
    "serverAffinityScore": 0.75,
    "analysis": "Play at very similar times of day; Play on 12 common servers"
  },
  "networkAnalysis": {
    "score": 0.82,
    "sharedTeammateCount": 127,
    "teammateOverlapPercentage": 0.71,
    "hasDirectConnection": false,
    "analysis": "Shared teammates: 127; Direct connection: NO"
  },
  "temporalAnalysis": {
    "score": 0.85,
    "temporalOverlapMinutes": 0,
    "significantTemporalOverlap": false,
    "analysis": "Zero temporal overlap with high teammate overlap (likely same person)"
  },
  "redFlags": [
    "K/D ratios nearly identical",
    "Play at nearly identical times of day",
    "Strong server affinity match",
    "Nearly identical ping on same servers (likely same location)",
    "Very high teammate overlap",
    "High teammate overlap but no direct co-session (classic alias pattern)",
    "Zero temporal overlap with high teammate overlap (likely same person)"
  ],
  "greenFlags": [],
  "suspicionLevel": "VeryLikely",
  "analysisConfidence": 0.95
}
```

### Example 2: Get Explanation
```bash
curl -X POST http://localhost:5000/stats/alias-detection/explain \
  -H "Content-Type: application/json" \
  -d @report.json
```

**Response**:
```
Alias Detection Report: StatWhore vs AltAccount
Overall Suspicion Score: 87% (VeryLikely)
Analysis Confidence: 95%

BREAKDOWN BY DIMENSION:
• Statistics Similarity: 92% - K/D ratios nearly identical; Very similar map performance patterns
• Behavioral Match: 78% - Play at very similar times of day; Strong server affinity match; Nearly identical ping
• Network Overlap: 82% - Shared teammates: 127; Direct connection: NO; High network shape similarity
• Temporal Consistency: 85% - Zero temporal overlap with high teammate overlap (likely same person)

🚩 RED FLAGS (Suggest same player):
  ⚠️  K/D ratios nearly identical
  ⚠️  Play at nearly identical times of day
  ⚠️  Strong server affinity match
  ⚠️  Nearly identical ping on same servers (likely same location)
  ⚠️  Very high teammate overlap
  ⚠️  High teammate overlap but no direct co-session (classic alias pattern)
  ⚠️  Zero temporal overlap with high teammate overlap (likely same person)

RECOMMENDATION:
⚠️  VERY LIKELY ALIASES - Strong evidence suggests same player. Consider investigation.
```

## Confidence Scoring

The analysis confidence (0-1) is based on data volume:
- **Base**: 0.50
- **+0.25** if sufficient stat data available
- **+0.15** if sufficient behavioral data available
- **+0.10** if network has 5+ shared teammates
- **Max**: 1.0

High confidence = trust the score more.
Low confidence = results may be inconclusive (need more data).

## Performance Considerations

### Query Optimization
- **Stat queries**: Use pre-computed `PlayerStatsMonthly` table (indexed by PlayerName)
- **Behavioral queries**: Use indexed PlayerSessions table
- **Ping queries**: Use PlayerObservation indexed by PlayerName + ServerGuid
- **Network queries**: Neo4j graph traversal (well-indexed relationships)

### Caching Strategy
- Results can be cached in Redis if comparing same pairs repeatedly
- Individual dimension scores are computed independently (parallelizable)
- Expensive operations (Neo4j queries) only run if stat score > 0.6

### Latency Expectations
- **Stat Analysis**: ~100ms (SQLite queries)
- **Behavioral Analysis**: ~150ms (session data)
- **Ping Analysis**: ~100ms (PlayerObservation queries)
- **Network Analysis**: ~200-500ms (Neo4j traversal)
- **Total**: **~600ms - 1.5s** per comparison

## Dependencies & Requirements

### Required
- ✅ SQLite database (PlayerStatsMonthly, PlayerSessions, PlayerObservations)
- ✅ Neo4j configured and populated
- ✅ IPlayerRelationshipService registered (for network analysis)
- ✅ ISqlitePlayerStatsService registered

### Configuration
All services auto-register in `Program.cs` when Neo4j is configured:
```csharp
if (neo4jConfig != null && !string.IsNullOrEmpty(neo4jConfig.Uri))
{
    builder.Services.AddScoped<PlayerAliasDetectionService>();
    builder.Services.AddScoped<StatSimilarityCalculator>();
    builder.Services.AddScoped<BehavioralPatternAnalyzer>();
    builder.Services.AddScoped<Neo4jNetworkAnalyzer>();
}
```

## Testing Scenarios

### True Positives (Known Aliases)
Compare players you know are the same person:
- Same K/D patterns ✅
- Same play times ✅
- Same server affinity ✅
- Same ping on servers ✅
- Large teammate overlap ✅
- **Zero co-sessions** ✅

**Expected**: Score > 0.80, `VeryLikely`

### True Negatives (Different Players)
Compare random players:
- Different K/D ratios ✅
- Different play times ✅
- Different servers ✅
- Different pings ✅
- Little teammate overlap ✅

**Expected**: Score < 0.50, `Unrelated`

### False Positives (Different but Similar)
Two legitimate good players with similar stats:
- Similar K/D (both skilled) ✅
- But: Different servers, different teammates, played together ✅

**Expected**: Score 0.60-0.75, `Potential` (inconclusive)

## Integration Points

### For Moderators
- Add UI panel to compare suspected aliases
- Display suspicion report with red/green flags
- Provide one-click investigation links

### For Anti-Cheat
- Could integrate with cheat detection
- Flag suspicious accounts for review
- Historical alias tracking

### For Tournament Enforcement
- Identify multi-account violations
- Prevent same-person teams in tournaments
- Validate single-account requirement

## Known Limitations

1. **Map data required**: Needs map-specific stats to be populated
2. **Network data**: Needs Neo4j fully synced with recent matches
3. **False positives possible**: Two similar legitimate players could score high
4. **Geographic limitations**: VPN/proxy usage invalidates ping consistency checks
5. **New players**: Limited history = lower confidence

## Future Enhancements

1. **ML scoring**: Train classifier on known aliases
2. **Batch comparison**: Find all potential aliases for a player
3. **Time-series tracking**: Track when same person stops using old alias
4. **Hardware fingerprinting**: If available (MAC address, etc.)
5. **Weapon preference analysis**: Add to stat similarity
6. **Squad/clan analysis**: Cross-reference with team memberships
7. **Account age correlation**: Alias accounts often created around same time

## Files Created

```
api/PlayerRelationships/
├── Models/
│   └── PlayerAliasSuspicionReport.cs      (Data models)
├── StatSimilarityCalculator.cs            (Stat analysis)
├── BehavioralPatternAnalyzer.cs           (Behavior analysis)
├── Neo4jNetworkAnalyzer.cs                (Network + temporal)
├── PlayerAliasDetectionService.cs         (Orchestration)
└── AliasDetectionController.cs            (API endpoints)

features/player-alias-detection/
├── implementation-plan.md                 (Design document)
└── IMPLEMENTATION_SUMMARY.md              (This file)
```

## Next Steps

1. **Deploy and test** on known alias pairs
2. **Tune weights** based on false positive rate
3. **Add moderator UI** for viewing reports
4. **Create batch comparison tool** for finding aliases for one player
5. **Monitor performance** and optimize queries if needed
6. **Integrate with anti-cheat systems** if available
