using api.PlayerTracking;
using api.PlayerRelationships.Models;
using Microsoft.EntityFrameworkCore;

namespace api.PlayerRelationships;

/// <summary>
/// Analyzes player activity timelines to detect account switchover patterns.
/// Uses raw SQL to aggregate data - does NOT load sessions into memory.
/// </summary>
public class ActivityTimelineAnalyzer(PlayerTrackerDbContext dbContext)
{
    /// <summary>
    /// Analyze activity timelines for two players and detect switchover patterns.
    /// All heavy lifting done in database - minimal memory usage.
    /// </summary>
    public async Task<ActivityTimeline> AnalyzeTimelineAsync(
        string player1,
        string player2,
        int lookBackDays = 180)
    {
        var cutoff = DateTime.UtcNow.AddDays(-lookBackDays);

        // Get activity period for each player (single row aggregation)
        var period1 = await CalculateActivityPeriodAsync(player1, cutoff);
        var period2 = await CalculateActivityPeriodAsync(player2, cutoff);

        if (period1.TotalSessions == 0 || period2.TotalSessions == 0)
        {
            return new ActivityTimeline
            {
                Player1Activity = period1,
                Player2Activity = period2,
                Gap = new GapAnalysis { PatternDescription = "Insufficient data" },
                Player1Timeline = [],
                Player2Timeline = [],
                AsciiTimeline = "Not enough data",
                Analysis = "Need more session history",
                SwitchoverSuspicionScore = 0.0
            };
        }

        // Analyze the gap
        var gapAnalysis = AnalyzeGap(period1, period2);

        // Build daily timelines using raw SQL (returns only aggregated daily data)
        var timeline1 = await BuildDailyTimelineAsync(player1, cutoff);
        var timeline2 = await BuildDailyTimelineAsync(player2, cutoff);

        // Generate ASCII visualization
        var asciiTimeline = GenerateAsciiTimeline(period1, period2, gapAnalysis);

        // Generate analysis text
        var analysis = GenerateTimelineAnalysis(period1, period2, gapAnalysis);

        // Calculate switchover suspicion score
        var switchoverScore = CalculateSwitchoverSuspicionScore(period1, period2, gapAnalysis);

        return new ActivityTimeline
        {
            Player1Activity = period1,
            Player2Activity = period2,
            Gap = gapAnalysis,
            Player1Timeline = timeline1,
            Player2Timeline = timeline2,
            AsciiTimeline = asciiTimeline,
            Analysis = analysis,
            SwitchoverSuspicionScore = switchoverScore
        };
    }

    /// <summary>
    /// Calculate activity period from aggregated database queries (single row).
    /// Does NOT load sessions into memory.
    /// </summary>
    private async Task<ActivityPeriod> CalculateActivityPeriodAsync(string playerName, DateTime cutoff)
    {
        var stats = await dbContext.Database
            .SqlQueryRaw<ActivityPeriodResult>("""
                SELECT
                    MIN(StartTime) as FirstSeen,
                    MAX(LastSeenTime) as LastSeen,
                    COUNT(*) as SessionCount,
                    COUNT(DISTINCT DATE(StartTime)) as ActiveDays
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND IsDeleted = 0
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", playerName))
            .SingleAsync();

        if (stats.SessionCount == 0 || stats.FirstSeen == null || stats.LastSeen == null)
            return new ActivityPeriod { DaysSinceLast = int.MaxValue };

        var now = DateTime.UtcNow;
        var daysSinceLast = (int)(now - stats.LastSeen.Value).TotalDays;
        var daysSpan = (stats.LastSeen.Value - stats.FirstSeen.Value).TotalDays;
        var avgPerDay = daysSpan > 0 ? stats.SessionCount / daysSpan : 0;

        return new ActivityPeriod
        {
            FirstSeen = stats.FirstSeen.Value,
            LastSeen = stats.LastSeen.Value,
            TotalActiveDays = stats.ActiveDays,
            DaysSinceLast = daysSinceLast,
            TotalSessions = stats.SessionCount,
            AvgSessionsPerDay = avgPerDay
        };
    }

    /// <summary>
    /// Build daily activity timeline from raw SQL aggregation.
    /// Returns only aggregated daily data (max ~180 rows), not individual sessions.
    /// </summary>
    private async Task<List<DailyActivity>> BuildDailyTimelineAsync(string playerName, DateTime cutoff)
    {
        var dailyStats = await dbContext.Database
            .SqlQueryRaw<DailyActivityResult>("""
                SELECT
                    DATE(StartTime) as Date,
                    COUNT(*) as SessionCount,
                    SUM(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS INTEGER)) as TotalMinutes,
                    CASE WHEN SUM(TotalDeaths) > 0
                         THEN CAST(SUM(TotalKills) AS REAL) / SUM(TotalDeaths)
                         ELSE SUM(TotalKills)
                    END as AvgKd
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND IsDeleted = 0
                GROUP BY DATE(StartTime)
                ORDER BY Date DESC
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", playerName))
            .ToListAsync();

        return dailyStats
            .Where(d => d.Date.HasValue)  // Filter out any null dates
            .Select(d => new DailyActivity
            {
                Date = d.Date.Value,
                SessionCount = d.SessionCount,
                TotalMinutes = (int)(d.TotalMinutes ?? 0),
                AvgKd = d.AvgKd ?? 0
            }).ToList();
    }

    /// <summary>
    /// Analyze the gap between two activity periods.
    /// </summary>
    private static GapAnalysis AnalyzeGap(ActivityPeriod period1, ActivityPeriod period2)
    {
        // Determine which account stopped first
        var stoppedFirst = period1.LastSeen <= period2.LastSeen ? "Player1" : "Player2";
        var startedSecond = stoppedFirst == "Player1" ? "Player2" : "Player1";

        var accountStoppedFirst = stoppedFirst == "Player1" ? "Player 1" : "Player 2";
        var accountStartedSecond = startedSecond == "Player1" ? "Player 1" : "Player 2";

        var switchoverStart = stoppedFirst == "Player1" ? period1.LastSeen : period2.LastSeen;
        var switchoverEnd = startedSecond == "Player1" ? period1.FirstSeen : period2.FirstSeen;

        var daysBetween = (int)(switchoverEnd - switchoverStart).TotalDays;
        var switchoverWindow = Math.Abs(daysBetween);

        // Calculate overlap
        var periodStart1 = period1.FirstSeen;
        var periodEnd1 = period1.LastSeen;
        var periodStart2 = period2.FirstSeen;
        var periodEnd2 = period2.LastSeen;

        var overlapStart = new[] { periodStart1, periodStart2 }.Max();
        var overlapEnd = new[] { periodEnd1, periodEnd2 }.Min();
        var overlapDays = Math.Max(0, (overlapEnd - overlapStart).TotalDays);
        var totalSpan = Math.Max((periodEnd1 - periodStart1).TotalDays, (periodEnd2 - periodStart2).TotalDays);
        var overlapRatio = totalSpan > 0 ? overlapDays / totalSpan : 0;

        // Describe the pattern
        var patternDescription = GeneratePatternDescription(daysBetween, switchoverWindow, overlapRatio);

        return new GapAnalysis
        {
            DaysBetween = daysBetween,
            AccountStoppedFirst = accountStoppedFirst,
            AccountStartedSecond = accountStartedSecond,
            SwitchoverStart = switchoverStart,
            SwitchoverEnd = switchoverEnd,
            SwitchoverWindowDays = switchoverWindow,
            OverlapRatio = overlapRatio,
            PatternDescription = patternDescription
        };
    }

    /// <summary>
    /// Generate human-readable pattern description.
    /// </summary>
    private static string GeneratePatternDescription(int daysBetween, int windowDays, double overlapRatio)
    {
        if (overlapRatio > 0.3)
            return $"Significant overlap ({overlapRatio:P0}) - accounts played simultaneously";

        if (daysBetween < 0)
            return $"Accounts overlapped - Player 2 started {Math.Abs(daysBetween)} days before Player 1 ended";

        if (daysBetween == 0)
            return "Perfect handoff - Player 2 started exactly when Player 1 stopped (suspicious timing)";

        if (daysBetween <= 3)
            return $"Very tight switchover - gap of only {daysBetween} days (highly suspicious)";

        if (daysBetween <= 7)
            return $"Tight switchover - gap of {daysBetween} days (suspicious pattern)";

        if (daysBetween <= 30)
            return $"Moderate gap - {daysBetween} days between accounts (possible alt)";

        return $"Large gap - {daysBetween} days between accounts (less suspicious)";
    }

    /// <summary>
    /// Generate ASCII timeline visualization.
    /// </summary>
    private static string GenerateAsciiTimeline(ActivityPeriod period1, ActivityPeriod period2, GapAnalysis gap)
    {
        var lines = new List<string>();

        var now = DateTime.UtcNow;
        var dayRange = 180;
        var startDate = now.AddDays(-dayRange);

        lines.Add("Activity Timeline (Last 180 days)");
        lines.Add("===================================");
        lines.Add("");

        // Period 1
        lines.Add($"Player 1: {period1.FirstSeen:yyyy-MM-dd} → {period1.LastSeen:yyyy-MM-dd}");
        lines.Add($"  Sessions: {period1.TotalSessions} | Active days: {period1.TotalActiveDays}");
        lines.Add($"  Status: {(period1.IsCurrentlyActive ? "ACTIVE" : $"DORMANT ({period1.DaysSinceLast} days)")}");
        lines.Add("");

        // Period 2
        lines.Add($"Player 2: {period2.FirstSeen:yyyy-MM-dd} → {period2.LastSeen:yyyy-MM-dd}");
        lines.Add($"  Sessions: {period2.TotalSessions} | Active days: {period2.TotalActiveDays}");
        lines.Add($"  Status: {(period2.IsCurrentlyActive ? "ACTIVE" : $"DORMANT ({period2.DaysSinceLast} days)")}");
        lines.Add("");

        // Gap analysis
        lines.Add("Switchover Analysis:");
        lines.Add($"  {gap.AccountStoppedFirst} last seen: {gap.SwitchoverStart:yyyy-MM-dd}");
        lines.Add($"  {gap.AccountStartedSecond} first seen: {gap.SwitchoverEnd:yyyy-MM-dd}");
        lines.Add($"  Gap: {gap.DaysBetween} days (window: {gap.SwitchoverWindowDays} days)");
        lines.Add($"  Overlap: {gap.OverlapRatio:P0}");
        lines.Add("");

        // Visual representation
        lines.Add("Timeline Visualization (│ = active, · = dormant):");
        lines.Add("");

        var width = 40;
        var p1Bar = GenerateTimebar(period1, startDate, dayRange, width);
        var p2Bar = GenerateTimebar(period2, startDate, dayRange, width);

        lines.Add($"Player1: {p1Bar}");
        lines.Add($"Player2: {p2Bar}");
        lines.Add("");

        return string.Join("\n", lines);
    }

    /// <summary>
    /// Generate a simple timebar visualization.
    /// </summary>
    private static string GenerateTimebar(ActivityPeriod period, DateTime startDate, int dayRange, int width)
    {
        var bar = new char[width];
        for (int i = 0; i < width; i++)
            bar[i] = '·'; // Default to dormant

        var firstDayOffset = (int)((period.FirstSeen.Date - startDate.Date).TotalDays);
        var lastDayOffset = (int)((period.LastSeen.Date - startDate.Date).TotalDays);

        for (int i = firstDayOffset; i <= lastDayOffset && i >= 0 && i < dayRange; i++)
        {
            var barIndex = (int)(i * (double)width / dayRange);
            if (barIndex >= 0 && barIndex < width)
                bar[barIndex] = '│';
        }

        return "[" + new string(bar) + "]";
    }

    /// <summary>
    /// Generate analysis text.
    /// </summary>
    private static string GenerateTimelineAnalysis(ActivityPeriod period1, ActivityPeriod period2, GapAnalysis gap)
    {
        var parts = new List<string>();

        parts.Add($"Timeline Analysis: {period1.TotalSessions} sessions vs {period2.TotalSessions} sessions");

        if (gap.OverlapRatio == 0)
            parts.Add("No temporal overlap - accounts never played simultaneously");
        else
            parts.Add($"Temporal overlap: {gap.OverlapRatio:P1} - accounts played at same time");

        if (gap.SwitchoverWindowDays <= 3)
            parts.Add($"🚩 TIGHT SWITCHOVER: Only {gap.SwitchoverWindowDays} day window between accounts");
        else if (gap.SwitchoverWindowDays <= 7)
            parts.Add($"🚩 SUSPICIOUS TIMING: {gap.SwitchoverWindowDays}-day gap suggests planned switchover");

        if (gap.OverlapRatio == 0 && gap.DaysBetween >= -7 && gap.DaysBetween <= 3)
            parts.Add("⚠️  CLASSIC PATTERN: Clean account switchover with minimal/no overlap");

        return string.Join("; ", parts);
    }

    /// <summary>
    /// Calculate switchover suspicion score (0-1).
    /// </summary>
    private static double CalculateSwitchoverSuspicionScore(ActivityPeriod period1, ActivityPeriod period2, GapAnalysis gap)
    {
        var score = 0.0;

        // No overlap is suspicious
        if (gap.OverlapRatio == 0)
            score += 0.40;

        // Tight switchover is very suspicious
        if (gap.SwitchoverWindowDays == 0)
            score += 0.40; // Perfect handoff
        else if (gap.SwitchoverWindowDays <= 3)
            score += 0.35;
        else if (gap.SwitchoverWindowDays <= 7)
            score += 0.25;
        else if (gap.SwitchoverWindowDays <= 30)
            score += 0.10;

        // Similar activity intensity is suspicious
        var intensity1 = period1.AvgSessionsPerDay;
        var intensity2 = period2.AvgSessionsPerDay;
        var intensityRatio = intensity1 > 0 ? intensity2 / intensity1 : 1;
        if (intensityRatio > 0.8 && intensityRatio < 1.2)
            score += 0.15; // Same activity patterns

        // Similar session counts
        var sessionRatio = (double)period1.TotalSessions / (period2.TotalSessions + 1);
        if (sessionRatio > 0.7 && sessionRatio < 1.3)
            score += 0.10;

        return Math.Min(1.0, score);
    }
}
