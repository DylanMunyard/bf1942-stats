# Tournament Rankings - Points Visibility Design Discussion

## User Goal
Show a breakdown of points assigned per match, with visibility into points received per round.

## Critical Consideration: Admin Override Capability

### The Problem
If an admin needs to override a ranking tiebreaker in the future, they'll need to:
1. **Identify** which specific point(s) caused the tie
2. **Adjust** that point (or round result)
3. **Verify** the change propagates correctly to the ranking

### Current System Analysis

**Current Data Flow:**
```
TournamentMatchResult (individual round data)
    ↓
TeamRankingCalculator.CalculateTeamStatistics()
    ↓
TournamentTeamRanking (aggregated + ranked)
```

**Problem with Option 1 (Calculate on Demand):**
- Breakdown is calculated but NOT stored
- If admin wants to override a tiebreaker:
  - They'd need to edit the underlying `TournamentMatchResult` (change WinningTeamId or tickets)
  - This is indirect and requires domain knowledge
  - No audit trail of the override decision
  - No way to see "why" the override was made
- Recalculation must happen to propagate changes

**Risk:** Admin might not know which specific round/match to change to break a tie

### Design Options Reconsidered

#### Option 1A: Calculate on Demand + Override Audit
```
TournamentMatchResult (source data)
    ↓
On-demand calculation for display
    ↓
IF admin needs to override:
  - Create TournamentRankingOverride table
  - Store: (TournamentId, TeamId, OverrideType, Value, Reason, AppliedBy, Date)
  - Recalculate rankings, then apply override
```

**Problem:** Still indirect - admin must understand what to change in TournamentMatchResult

#### Option 1B: Explicit Points Audit Table (Hybrid)
```
TournamentMatchResult (source data - round wins/losses)
    ↓
TournamentRoundPoints (explicit: TeamId gets 1 or 0 points per round)
    ↓
TournamentMatchPointsAudit (aggregated: TeamId earned X points in MatchId)
    ↓
TournamentTeamRanking (aggregated across tournament)
```

**Stored Points Flow:**
- Store explicit point assignments per round in `TournamentRoundPoints`
- Admin can see and edit individual point assignments
- Match-level summaries auto-calculated from round points
- Team rankings auto-calculated from match summaries
- Audit trail preserved: who changed what and when

**Advantages for Admin Override:**
- Clear visibility: "Team A earned 1 point in Round 1, Map Midway"
- Direct editability: Admin can change that 1 point to 0 (or vice versa)
- Audit trail: Track the override decision
- Ranking recalculation is automatic and consistent
- Clear explanation: "Why did rank change?" → Because this point was adjusted

#### Option 2: Keep Everything Calculated BUT Add Override Table
```
TournamentMatchResult (source data)
    ↓
Calculate on-demand for display
    ↓
TournamentRankingOverride:
  - (TournamentId, TeamId, Week, RankAdjustment, PointsAdjustment, Reason, Admin, Date)
  - Applied AFTER calculation, before final ranking
```

**Problem:** Still requires admin to understand what change to make to source data. The override is a patch, not a fix.

### Recommendation: Option 1B - Explicit Points Audit Table

**Hybrid approach that solves the admin override problem:**

1. **Keep master ranking aggregations** on `TournamentTeamRanking` (as planned with 7 new fields)

2. **Add explicit points tracking:**
   ```csharp
   public class TournamentRoundPoints
   {
       public int Id { get; set; }
       public int TournamentId { get; set; }
       public int TournamentMatchResultId { get; set; }  // Points for this specific round
       public int TeamId { get; set; }
       public int PointsAwarded { get; set; }  // 0 or 1 (could be 0.5 for partial in future)
       public string? MapName { get; set; }  // Denormalized for clarity
       public int MatchId { get; set; }  // Denormalized to group by match
       
       // Admin adjustments
       public int? AdjustedPoints { get; set; }  // NULL = no override
       public string? AdjustmentReason { get; set; }
       public int? AdjustedByAdminId { get; set; }
       public Instant? AdjustedAt { get; set; }
       
       public Instant CreatedAt { get; set; }
       public Instant UpdatedAt { get; set; }
       
       // Navigation
       public TournamentMatchResult MatchResult { get; set; } = null!;
   }
   ```

3. **Calculation flow:**
   - `TeamRankingCalculator` populates `TournamentRoundPoints` with initial points
   - Uses `AdjustedPoints` if present, otherwise `PointsAwarded`
   - Aggregates for match totals
   - Aggregates for team rankings

4. **Admin override:**
   - UI shows per-round point assignments
   - Admin clicks "Adjust points for this round" → Enter reason
   - Sets `AdjustedPoints`, `AdjustmentReason`, `AdjustedByAdminId`, `AdjustedAt`
   - Trigger ranking recalculation
   - Change propagates clearly: Round → Match Total → Team Ranking

5. **Benefits:**
   - Clear audit trail: what changed, why, who did it
   - Transparent to users: "Points were adjusted by admin on [date] because [reason]"
   - Admin can see exact impact: "This adjustment changes ranking from #2 to #3"
   - Detail breakdown is stored (not calculated) → fast queries
   - Future extensibility: Could support partial points (0.5), modifiers, etc.

### Migration Strategy

**Phase 1 (Now):**
- Add 7 fields to `TournamentTeamRanking` (MatchesPlayed, Victories, Ties, Losses, TicketsFor, TicketsAgainst, Points)
- Update `TeamRankingCalculator`
- Update API response DTOs
- Test and verify

**Phase 2 (Future, if needed):**
- Add `TournamentRoundPoints` table
- Add fields for admin adjustments
- Create admin UI for overrides
- Implement recalculation on adjustment

**Benefit:** Phase 1 works standalone. Phase 2 is plug-and-play when admin override feature is needed.

## Final Decision

**Recommended Path Forward:**

1. **Implement Phase 1 now** (master ranking table enhancement)
   - Simpler, solves the immediate problem
   - No admin override UI needed yet
   
2. **Design Phase 2 with explicit points table** (when admin override feature is requested)
   - Provides the audit trail and clarity needed
   - Allows fine-grained adjustments
   - Clear propagation path: Round → Match → Team Rank

**This gives us:**
- ✅ Correct rankings based on points
- ✅ Visibility into how rankings were calculated (via breakdown queries)
- ✅ Future-proof for admin overrides (clear architecture for when it's needed)
- ✅ Audit trail (when Phase 2 is implemented)
