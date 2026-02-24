# Player Alias Detection - Quick Start Guide

## What It Does

Detects potential stat whores (players with multiple accounts) by comparing:
- Statistical similarity (K/D, kills, map dominance)
- Behavioral patterns (play times, server preferences, ping)
- Network relationships (teammates, mutual connections)
- Temporal consistency (do they ever play together? should be NO for aliases)

## Using the API

### 1. Compare Two Players

```bash
# Basic comparison (90-day lookback)
curl "http://localhost:5000/stats/alias-detection/compare?player1=PlayerOne&player2=PlayerTwo"

# With custom lookback period
curl "http://localhost:5000/stats/alias-detection/compare?player1=PlayerOne&player2=PlayerTwo&lookBackDays=180"

# All-time comparison
curl "http://localhost:5000/stats/alias-detection/compare?player1=PlayerOne&player2=PlayerTwo&lookBackDays=0"
```

### 2. Read the Report

Key fields to look at:

| Field | Meaning | What to Look For |
|-------|---------|-----------------|
| `overallSimilarityScore` | 0-1 scale | > 0.85 = Very suspicious |
| `suspicionLevel` | Enum: Unrelated / Potential / Likely / VeryLikely | VeryLikely = investigate |
| `redFlags` | Array of suspicious patterns | Multiple flags = strong evidence |
| `greenFlags` | Array of "they're different" evidence | Any green flags = less suspicious |
| `analysisConfidence` | 0-1, based on data volume | > 0.70 = trust the score |

### 3. Understanding the Report

**Example Report (High Suspicion)**:
```json
{
  "overallSimilarityScore": 0.87,
  "suspicionLevel": "VeryLikely",
  "analysisConfidence": 0.92,
  "redFlags": [
    "K/D ratios nearly identical",
    "Play at nearly identical times of day",
    "Nearly identical ping on same servers (likely same location)",
    "High teammate overlap but no direct co-session (classic alias pattern)"
  ],
  "greenFlags": []
}
```

**What this means**:
- Both accounts are almost certainly the same person
- They play at the same times from the same location
- They have many shared teammates but never actually play together (different accounts)
- This is the **classic signature of an alias**

---

**Example Report (Low Suspicion)**:
```json
{
  "overallSimilarityScore": 0.35,
  "suspicionLevel": "Unrelated",
  "redFlags": [],
  "greenFlags": [
    "Play at significantly different times",
    "Different server preferences",
    "K/D ratios significantly different"
  ]
}
```

**What this means**:
- These are almost certainly two different people
- They play at different times, on different servers, with different skill levels
- Not suspicious

---

**Example Report (Inconclusive)**:
```json
{
  "overallSimilarityScore": 0.62,
  "suspicionLevel": "Potential",
  "redFlags": [
    "Similar K/D ratios"
  ],
  "greenFlags": [
    "Different server preferences",
    "Played together in sessions"
  ],
  "analysisConfidence": 0.65
}
```

**What this means**:
- Similar stats, but they play different servers and have played together
- Probably two different skilled players with similar abilities
- Not necessarily aliases
- Confidence is only 65%, so don't rely too much on this score

## Quick Decision Tree

```
Score > 0.85?
├─ YES + confidence > 0.80?
│  └─ ALMOST CERTAINLY ALIASES
│     Action: Flag for investigation/ban
│
├─ YES + confidence < 0.70?
│  └─ LIKELY but low confidence
│     Action: Manually review (may need more data)
│
├─ 0.70-0.85?
│  └─ PROBABLY ALIASES
│     Action: Investigate further
│
├─ 0.50-0.70?
│  └─ POSSIBLY RELATED
│     Action: Check red flags manually
│
└─ < 0.50?
   └─ PROBABLY DIFFERENT
      Action: Not suspicious
```

## Key Red Flags to Watch

When ANY of these appear, be suspicious:
- ✅ **K/D ratios nearly identical** - Different players rarely have same skill level
- ✅ **Same play times** - Same person plays at same times
- ✅ **Same ping on same servers** - Same geographic location
- ✅ **High teammate overlap + ZERO co-sessions** - **This is the smoking gun for aliases**
- ✅ **Same map dominance patterns** - Same person dominates same maps

## Combining Signals

| Stat K/D | Play Time | Ping | Teammates | Co-sessions | Likely? |
|----------|-----------|------|-----------|-------------|---------|
| Identical | Same | Same | High overlap | ZERO | **YES (99%)** |
| Identical | Different | Same | Low | Many | Unlikely (different times?) |
| Similar | Same | Different | High | ZERO | **YES (90%)** |
| Different | Same | Same | High | Some | Probably not (played together) |
| Different | Different | Different | Low | Many | No (completely different) |

**The strongest signal**: High teammate overlap + ZERO co-sessions = ALIASES

## Testing It Out

### Known Aliases
If you have two accounts you know are the same person:
```bash
curl "http://localhost:5000/stats/alias-detection/compare?player1=MainAccount&player2=AltAccount&lookBackDays=90"
```
Expected: Score > 0.80, VeryLikely

### Random Players (should be different)
Compare two random player names:
```bash
curl "http://localhost:5000/stats/alias-detection/compare?player1=RandomPlayer1&player2=RandomPlayer2"
```
Expected: Score < 0.50, Unrelated

## Limitations

1. **New players** = lower confidence (not enough history)
2. **VPN/Proxy** = ping consistency check won't work
3. **Two skilled legitimate players** = might score 0.60-0.70 (inconclusive)
4. **Limited recent data** = might need `lookBackDays=0` to see all-time patterns
5. **Map stats not available** = one dimension won't work

## Advanced Usage

### Get Explanation
```bash
curl -X POST http://localhost:5000/stats/alias-detection/explain \
  -H "Content-Type: application/json" \
  -d @report.json
```

Returns a human-readable markdown explanation with recommendations.

### Batch Comparison (Future)
```bash
curl "http://localhost:5000/stats/alias-detection/PlayerOne/potential-aliases?limit=10"
```
*(Not implemented yet - returns 501 Not Implemented)*

## Interpreting Each Dimension

### Statistics Similarity (30% weight)
- **Score**: 0-1
- **What it measures**: K/D ratio, kill rate, map-specific performance
- **High score** (>0.85): Nearly identical performance
- **Low score** (<0.30): Very different skill levels
- **Why it matters**: Aliases have same skill level

### Behavioral Analysis (20% weight)
- **Score**: 0-1
- **What it measures**: Play times, server preferences, ping
- **High score** (>0.75): Same schedule, same servers, same location
- **Low score** (<0.30): Different times, different servers
- **Why it matters**: Same person has consistent habits

### Network Analysis (25% weight)
- **Score**: 0-1
- **Teammate Overlap**: % of teammates in common (Jaccard similarity)
- **Direct Connection**: Did they ever play together? (should be NO for aliases)
- **High score** (>0.80): Many shared teammates, no co-sessions
- **Why it matters**: Aliases meet same people, but never simultaneously

### Temporal Analysis (15% weight)
- **Score**: 0-1
- **Key metric**: Temporal overlap in minutes (should be 0)
- **High score** (>0.85): Never played together, inverted activity
- **Low score** (<0.30): Played together frequently
- **Why it matters**: Same person can't play 2 accounts at once

### Ping Consistency (Included in Behavioral)
- **Score**: 0-1
- **What it measures**: Average ping variance on common servers
- **High score** (>0.85): < 5% difference (same location)
- **Low score** (<0.30): > 30% difference (different location)
- **Why it matters**: Same person = same ISP/location

## Getting Help

If a comparison seems wrong:
1. Check `analysisConfidence` - if < 0.70, might not have enough data
2. Look at `lookBackDays` - try with `lookBackDays=0` for all-time data
3. Check red/green flags manually - sometimes one dimension dominates
4. Review individual scores: maybe one dimension is off

Example: Player A and B have identical K/D but different play times
- Stat similarity: 0.90 (high)
- Behavioral: 0.40 (low)
- Network: 0.50 (medium)
- Overall: Could be 0.60 (Potential)
→ Inconclusive, need manual review
