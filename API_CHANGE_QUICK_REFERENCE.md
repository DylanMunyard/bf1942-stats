# API Schema Changes - Quick Reference

## 🔄 The Change in One Sentence
**`matchResult` (single object) → `matchResults` (array of objects)**

Maps can now have multiple results because they can be played multiple rounds.

---

## 📝 Before and After

### BEFORE (Single Result)
```typescript
map.matchResult = {
  id: 10,
  team1Name: "Alpha",
  team2Name: "Bravo",
  winningTeamName: "Alpha",
  team1Tickets: 200,
  team2Tickets: 100
}

// Accessing
const winner = map.matchResult?.winningTeamName;
const hasResult = map.matchResult != null;
```

### AFTER (Multiple Results)
```typescript
map.matchResults = [
  {
    id: 10,
    team1Name: "Alpha",
    team2Name: "Bravo",
    winningTeamName: "Alpha",
    team1Tickets: 200,
    team2Tickets: 100
  },
  {
    id: 11,
    team1Name: "Bravo",      // Note: teams might swap
    team2Name: "Alpha",
    winningTeamName: "Bravo",
    team1Tickets: 180,
    team2Tickets: 120
  }
]

// Accessing
const firstResult = map.matchResults[0];
const winner = map.matchResults[0]?.winningTeamName;
const hasResults = map.matchResults.length > 0;
```

---

## ❌ Removed Properties from TournamentMatchMapResponse

| Property | Why Removed | Where to Find Now |
|----------|------------|-------------------|
| `roundId` | Moved to individual results | `matchResults[i].roundId` (in database, not API) |
| `round` | Moved to individual results | Fetch separately if needed via roundId |

---

## ✅ What Stays the Same

- `mapName` ✓
- `mapOrder` ✓
- `teamId` ✓
- `teamName` ✓
- `matchResult.id` ✓
- `matchResult.team1Id` ✓
- `matchResult.team2Id` ✓
- `matchResult.winningTeamId` ✓
- `matchResult.team1Tickets` ✓
- `matchResult.team2Tickets` ✓

The result object structure is **exactly the same**, just now in an array.

---

## 🔧 Common Code Patterns

### Pattern 1: Display Results
```javascript
// OLD
if (map.matchResult) {
  console.log(`${map.matchResult.team1Name} beat ${map.matchResult.team2Name}`);
}

// NEW
map.matchResults.forEach(result => {
  console.log(`${result.team1Name} beat ${result.team2Name}`);
});
```

### Pattern 2: Check if Results Exist
```javascript
// OLD
const hasResult = map.matchResult != null;

// NEW
const hasResults = map.matchResults?.length > 0;
```

### Pattern 3: Get Winner
```javascript
// OLD
const winner = map.matchResult?.winningTeamName;

// NEW
const winner = map.matchResults?.[0]?.winningTeamName;
// Or get winner from latest result:
const winner = map.matchResults?.[map.matchResults.length - 1]?.winningTeamName;
```

### Pattern 4: Display Round-by-Round
```javascript
// OLD - Not applicable

// NEW
map.matchResults.forEach((result, round) => {
  console.log(`Round ${round + 1}: ${result.team1Name} (${result.team1Tickets}) vs ${result.team2Name} (${result.team2Tickets})`);
});
```

---

## 📊 Data Flow Example

### Tournament Detail Response
```json
{
  "matches": [
    {
      "id": 100,
      "scheduledDate": "2025-01-01T19:00:00Z",
      "team1Name": "Alpha",
      "team2Name": "Bravo",
      "maps": [
        {
          "id": 1,
          "mapName": "Colditz",
          "mapOrder": 0,
          "matchResults": [
            {
              "id": 10,
              "team1Id": 5,
              "team1Name": "Alpha",
              "team2Id": 6,
              "team2Name": "Bravo",
              "winningTeamId": 5,
              "winningTeamName": "Alpha",
              "team1Tickets": 200,
              "team2Tickets": 100
            },
            {
              "id": 11,
              "team1Id": 6,
              "team1Name": "Bravo",
              "team2Id": 5,
              "team2Name": "Alpha",
              "winningTeamId": 6,
              "winningTeamName": "Bravo",
              "team1Tickets": 180,
              "team2Tickets": 120
            }
          ]
        }
      ]
    }
  ]
}
```

---

## 🎯 Why This Change?

The old schema assumed one result per map. Now we support:

1. **Multiple rounds per map** - Colditz might be played Round 1 and Round 2 with different outcomes
2. **Team swaps** - Teams might be Team1 in Round 1, Team2 in Round 2
3. **Optional round linking** - Results can be manually entered without a round link

---

## 🔗 Affected Endpoints

All tournament detail endpoints now return `matchResults` array:
- `GET /stats/admin/tournaments/{id}`
- `GET /stats/admin/tournaments/{id}/matches/{matchId}`
- `GET /stats/tournaments/{idOrName}` (public)

---

## 💡 Implementation Notes

1. **Empty arrays are valid** - If no results yet: `matchResults: []`
2. **Order matters** - Results are ordered by creation (first round first)
3. **No breaking changes to result object** - Each result object is identical to before
4. **TeamIds can differ per result** - Team1 in Round 1 might be Team2 in Round 2
