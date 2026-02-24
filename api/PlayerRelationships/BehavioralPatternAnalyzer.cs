using api.PlayerTracking;
using api.PlayerRelationships.Models;
using Microsoft.EntityFrameworkCore;
using System.Data;

namespace api.PlayerRelationships;

/// <summary>
/// Analyzes behavioral patterns: play times, server preferences, ping consistency.
/// Uses raw SQL to offload aggregation to database - does NOT load sessions into memory.
/// </summary>
public class BehavioralPatternAnalyzer(PlayerTrackerDbContext dbContext)
{
    /// <summary>
    /// Analyze behavioral patterns between two players.
    /// All heavy lifting done in database - minimal memory usage.
    /// </summary>
    public async Task<BehavioralAnalysis> AnalyzeBehaviorAsync(
        string player1,
        string player2,
        int lookBackDays = 90)
    {
        var cutoffDate = DateTime.UtcNow.AddDays(-lookBackDays);

        // Check if we have any session data for both players using raw SQL
        var sessionCounts = await dbContext.Database
            .SqlQueryRaw<SessionCountResult>("""
                SELECT PlayerName, COUNT(*) as Count
                FROM PlayerSessions
                WHERE (PlayerName = @player1 OR PlayerName = @player2)
                  AND StartTime >= @cutoff
                GROUP BY PlayerName
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@player1", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@player2", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoffDate))
            .ToListAsync();

        var count1 = sessionCounts.FirstOrDefault(x => x.PlayerName == player1)?.Count ?? 0;
        var count2 = sessionCounts.FirstOrDefault(x => x.PlayerName == player2)?.Count ?? 0;

        if (count1 == 0 || count2 == 0)
        {
            return new BehavioralAnalysis(
                Score: 0.0,
                PlayTimeOverlapScore: 0.0,
                ServerAffinityScore: 0.0,
                PingConsistencyScore: 0.5,
                SessionPatternScore: 0.0,
                Analysis: "Insufficient session data"
            );
        }

        // Calculate individual behavioral metrics using raw SQL (no in-memory loading)
        var playTimeOverlap = await CalculatePlayTimeOverlapAsync(player1, player2, cutoffDate);
        var serverAffinity = await CalculateServerAffinityAsync(player1, player2, cutoffDate);
        var pingConsistency = await CalculatePingConsistencyAsync(player1, player2);
        var sessionPattern = await CalculateSessionPatternSimilarityAsync(player1, player2, cutoffDate);

        // Overall score: weighted average
        var overallScore = (playTimeOverlap * 0.30) +
                          (serverAffinity * 0.30) +
                          (pingConsistency * 0.20) +
                          (sessionPattern * 0.20);

        var analysis = BuildAnalysis(
            player1, player2,
            playTimeOverlap, serverAffinity, pingConsistency, sessionPattern,
            count1, count2
        );

        return new BehavioralAnalysis(
            Score: Math.Min(1.0, overallScore),
            PlayTimeOverlapScore: playTimeOverlap,
            ServerAffinityScore: serverAffinity,
            PingConsistencyScore: pingConsistency,
            SessionPatternScore: sessionPattern,
            Analysis: analysis
        );
    }

    /// <summary>
    /// Calculate similarity of play time patterns (hour-of-day) using raw SQL.
    /// Returns 1.0 if they play at similar times, 0.0 if completely different times.
    /// </summary>
    private async Task<double> CalculatePlayTimeOverlapAsync(string player1, string player2, DateTime cutoff)
    {
        // Get hour-of-day distribution from database (returns only 24 rows per player)
        var hourStats1 = await dbContext.Database
            .SqlQueryRaw<HourDistributionResult>("""
                SELECT CAST(strftime('%H', StartTime) AS INTEGER) as Hour, COUNT(*) as Count
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND StartTime >= @cutoff
                GROUP BY Hour
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .ToListAsync();

        var hourStats2 = await dbContext.Database
            .SqlQueryRaw<HourDistributionResult>("""
                SELECT CAST(strftime('%H', StartTime) AS INTEGER) as Hour, COUNT(*) as Count
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND StartTime >= @cutoff
                GROUP BY Hour
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .ToListAsync();

        // Build normalized probability vectors from aggregated data
        var probs1 = BuildHourProbabilities(hourStats1);
        var probs2 = BuildHourProbabilities(hourStats2);

        // Jensen-Shannon divergence (symmetric KL divergence)
        var jsDiv = JensenShannonDivergence(probs1, probs2);

        // Convert to similarity (1 - divergence, capped at 0)
        return Math.Max(0.0, 1.0 - jsDiv);
    }

    /// <summary>
    /// Build 24-hour probability distribution from hour stats.
    /// </summary>
    private static List<double> BuildHourProbabilities(List<HourDistributionResult> hourStats)
    {
        var probs = new double[24];
        var total = hourStats.Sum(x => x.Count);

        foreach (var stat in hourStats)
        {
            if (stat.Hour >= 0 && stat.Hour < 24)
                probs[stat.Hour] = total > 0 ? (double)stat.Count / total : 0.0;
        }

        return probs.ToList();
    }

    /// <summary>
    /// Calculate server affinity similarity using raw SQL.
    /// Returns % of servers in common between their play lists.
    /// </summary>
    private async Task<double> CalculateServerAffinityAsync(string player1, string player2, DateTime cutoff)
    {
        // Get distinct servers for both players (only server guids, not full data)
        var commonServerCount = await dbContext.Database
            .SqlQueryRaw<int>("""
                SELECT COUNT(DISTINCT ServerGuid)
                FROM (
                    SELECT DISTINCT ServerGuid FROM PlayerSessions
                    WHERE PlayerName = @player1 AND StartTime >= @cutoff
                    INTERSECT
                    SELECT DISTINCT ServerGuid FROM PlayerSessions
                    WHERE PlayerName = @player2 AND StartTime >= @cutoff
                )
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@player1", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@player2", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .SingleAsync();

        // Get union count
        var totalUniqueServers = await dbContext.Database
            .SqlQueryRaw<int>("""
                SELECT COUNT(DISTINCT ServerGuid)
                FROM (
                    SELECT DISTINCT ServerGuid FROM PlayerSessions
                    WHERE PlayerName = @player1 AND StartTime >= @cutoff
                    UNION
                    SELECT DISTINCT ServerGuid FROM PlayerSessions
                    WHERE PlayerName = @player2 AND StartTime >= @cutoff
                )
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@player1", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@player2", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .SingleAsync();

        if (totalUniqueServers == 0)
            return 0.0;

        // Jaccard similarity
        return (double)commonServerCount / totalUniqueServers;
    }

    /// <summary>
    /// Calculate ping consistency on common servers.
    /// For aliases, average ping on same server should be nearly identical.
    /// Uses raw SQL - only returns aggregated data.
    /// </summary>
    private async Task<double> CalculatePingConsistencyAsync(string player1, string player2)
    {
        var cutoff = DateTime.UtcNow.AddDays(-7); // Recent data only

        // Get average ping per server for both players (aggregated in database)
        var pings1 = await dbContext.Database
            .SqlQueryRaw<PingStatResult>("""
                SELECT ps.ServerGuid, AVG(CAST(o.Ping AS REAL)) as AvgPing
                FROM PlayerObservations o
                JOIN PlayerSessions ps ON o.SessionId = ps.SessionId
                WHERE ps.PlayerName = @playerName AND ps.StartTime >= @cutoff AND o.Ping > 0
                GROUP BY ps.ServerGuid
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .ToListAsync();

        var pings2 = await dbContext.Database
            .SqlQueryRaw<PingStatResult>("""
                SELECT ps.ServerGuid, AVG(CAST(o.Ping AS REAL)) as AvgPing
                FROM PlayerObservations o
                JOIN PlayerSessions ps ON o.SessionId = ps.SessionId
                WHERE ps.PlayerName = @playerName AND ps.StartTime >= @cutoff AND o.Ping > 0
                GROUP BY ps.ServerGuid
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .ToListAsync();

        if (pings1.Count == 0 || pings2.Count == 0)
            return 0.5; // Neutral if no ping data

        var commonServers = pings1.Select(p => p.ServerGuid)
            .Intersect(pings2.Select(p => p.ServerGuid))
            .ToList();

        if (commonServers.Count == 0)
            return 0.3; // No common servers to compare

        // For each common server, check ping similarity
        var pingDifferences = new List<double>();

        foreach (var server in commonServers)
        {
            var ping1 = pings1.First(p => p.ServerGuid == server).AvgPing;
            var ping2 = pings2.First(p => p.ServerGuid == server).AvgPing;

            // Large ping differences on same server = likely different geographic locations
            var maxPing = Math.Max(ping1, ping2);
            if (maxPing > 0)
            {
                var percentDiff = Math.Abs(ping1 - ping2) / maxPing;
                pingDifferences.Add(percentDiff);
            }
        }

        if (pingDifferences.Count == 0)
            return 0.5;

        // Average percent difference
        var avgDifference = pingDifferences.Average();

        // < 5% diff = likely same location (score 0.95)
        // 5-15% diff = could be same or nearby (score 0.7)
        // > 30% diff = likely different locations (score 0.1)

        if (avgDifference < 0.05)
            return 0.95;
        if (avgDifference < 0.15)
            return 0.70;
        if (avgDifference < 0.30)
            return 0.40;

        return 0.1;
    }

    /// <summary>
    /// Calculate similarity of session patterns (frequency, duration) using raw SQL.
    /// </summary>
    private async Task<double> CalculateSessionPatternSimilarityAsync(string player1, string player2, DateTime cutoff)
    {
        // Get session statistics from database (single row per player)
        var stats1 = await dbContext.Database
            .SqlQueryRaw<SessionStatResult>("""
                SELECT
                    COUNT(*) as SessionCount,
                    SUM(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS INTEGER)) as TotalMinutes,
                    AVG(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS REAL)) as AvgSessionMinutes
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND StartTime >= @cutoff
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player1),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .SingleAsync();

        var stats2 = await dbContext.Database
            .SqlQueryRaw<SessionStatResult>("""
                SELECT
                    COUNT(*) as SessionCount,
                    SUM(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS INTEGER)) as TotalMinutes,
                    AVG(CAST((julianday(LastSeenTime) - julianday(StartTime)) * 1440 AS REAL)) as AvgSessionMinutes
                FROM PlayerSessions
                WHERE PlayerName = @playerName AND StartTime >= @cutoff
                """,
                new Microsoft.Data.Sqlite.SqliteParameter("@playerName", player2),
                new Microsoft.Data.Sqlite.SqliteParameter("@cutoff", cutoff))
            .SingleAsync();

        // Compare average session duration
        var avgDuration1 = stats1.AvgSessionMinutes;
        var avgDuration2 = stats2.AvgSessionMinutes;

        var maxDuration = Math.Max(avgDuration1, avgDuration2);
        var durationSimilarity = maxDuration > 0
            ? 1.0 - Math.Min(1.0, Math.Abs(avgDuration1 - avgDuration2) / maxDuration)
            : 0.5;

        // Session frequency (sessions per day)
        var daySpan1 = (int)((DateTime.UtcNow - cutoff).TotalDays);
        var daySpan2 = (int)((DateTime.UtcNow - cutoff).TotalDays);
        var freq1 = daySpan1 > 0 ? stats1.SessionCount / (double)daySpan1 : 0;
        var freq2 = daySpan2 > 0 ? stats2.SessionCount / (double)daySpan2 : 0;

        var maxFreq = Math.Max(freq1 + 0.1, freq2 + 0.1);
        var freqSimilarity = 1.0 - Math.Min(1.0, Math.Abs(freq1 - freq2) / maxFreq);

        // Combined score
        return (durationSimilarity * 0.6) + (freqSimilarity * 0.4);
    }

    /// <summary>
    /// Calculate Jensen-Shannon divergence between two probability distributions.
    /// </summary>
    private static double JensenShannonDivergence(List<double> p, List<double> q)
    {
        if (p.Count != q.Count)
            return 1.0;

        var m = new List<double>();
        for (int i = 0; i < p.Count; i++)
        {
            m.Add((p[i] + q[i]) / 2.0);
        }

        var kld_pm = KullbackLeiblerDivergence(p, m);
        var kld_qm = KullbackLeiblerDivergence(q, m);

        return (kld_pm + kld_qm) / 2.0;
    }

    /// <summary>
    /// Calculate Kullback-Leibler divergence between two distributions.
    /// </summary>
    private static double KullbackLeiblerDivergence(List<double> p, List<double> q)
    {
        var divergence = 0.0;
        for (int i = 0; i < p.Count; i++)
        {
            if (p[i] > 0 && q[i] > 0)
            {
                divergence += p[i] * Math.Log(p[i] / q[i]);
            }
        }
        return divergence;
    }

    private static string BuildAnalysis(
        string player1,
        string player2,
        double playTimeOverlap,
        double serverAffinity,
        double pingConsistency,
        double sessionPattern,
        int count1,
        int count2)
    {
        var parts = new List<string>();

        parts.Add($"{player1}: {count1} sessions, {player2}: {count2} sessions");

        if (playTimeOverlap > 0.75)
            parts.Add("Play at very similar times of day");
        else if (playTimeOverlap > 0.50)
            parts.Add("Significant play time overlap");
        else if (playTimeOverlap < 0.30)
            parts.Add("Play at different times");

        if (serverAffinity > 0.70)
            parts.Add("Very similar server preferences");

        if (pingConsistency > 0.80)
            parts.Add("Nearly identical ping on same servers (likely same location)");
        else if (pingConsistency < 0.30)
            parts.Add("Very different pings (likely different geographic locations)");

        return string.Join("; ", parts);
    }
}
