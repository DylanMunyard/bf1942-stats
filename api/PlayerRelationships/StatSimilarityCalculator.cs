using api.Analytics.Models;
using api.Players.Models;
using api.PlayerStats;
using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Calculates statistical similarity between two players.
/// Compares K/D ratios, kill rates, map performance patterns, and server performance.
/// </summary>
public class StatSimilarityCalculator(ISqlitePlayerStatsService statsService)
{
    /// <summary>
    /// Calculate overall stat similarity between two players.
    /// Returns score 0-1 where 1.0 = identical, 0.0 = completely different.
    /// Uses all available historical stats to identify behavioral patterns.
    /// </summary>
    public async Task<StatSimilarityAnalysis> CalculateSimilarityAsync(
        string player1,
        string player2,
        int lookBackDays = 3650)  // 10 years of history for dormant accounts
    {
        var stats1 = await statsService.GetPlayerStatsAsync(player1, lookBackDays);
        var stats2 = await statsService.GetPlayerStatsAsync(player2, lookBackDays);

        if (stats1 == null || stats2 == null)
        {
            // Not enough data
            return new StatSimilarityAnalysis(
                Score: 0.0,
                KdRatioDifference: 0.0,
                KillRateDifference: 0.0,
                ScorePerRoundDifference: 0.0,
                MapPerformanceSimilarity: 0.0,
                ServerPerformanceSimilarity: 0.0,
                Analysis: "Insufficient player data for comparison"
            );
        }

        // Calculate individual stat differences (normalized)
        var kdRatioDiff = CalculateNormalizedDifference(stats1.KdRatio, stats2.KdRatio, minThreshold: 0.5, maxThreshold: 5.0);
        var killRateDiff = CalculateNormalizedDifference(stats1.KillRate, stats2.KillRate, minThreshold: 0.1, maxThreshold: 2.0);
        var scorePerRoundDiff = CalculateNormalizedDifference(stats1.AvgScorePerRound, stats2.AvgScorePerRound, minThreshold: 100, maxThreshold: 1500);

        // Get map-specific stats (use Last30Days as default, will be filtered by lookBackDays in service)
        var mapStats1 = await statsService.GetPlayerMapStatsAsync(player1, TimePeriod.Last30Days);
        var mapStats2 = await statsService.GetPlayerMapStatsAsync(player2, TimePeriod.Last30Days);
        var mapSimilarity = CalculateMapPerformanceSimilarity(mapStats1, mapStats2);

        // Get server performance stats
        var serverStats1 = await statsService.GetPlayerServerInsightsAsync(player1, lookBackDays);
        var serverStats2 = await statsService.GetPlayerServerInsightsAsync(player2, lookBackDays);
        var serverSimilarity = CalculateServerPerformanceSimilarity(serverStats1, serverStats2);

        // Calculate overall score: weighted average
        // K/D similarity is most important for identifying alts
        var overallScore = (kdRatioDiff * 0.40) +
                          (killRateDiff * 0.25) +
                          (scorePerRoundDiff * 0.15) +
                          (mapSimilarity * 0.15) +
                          (serverSimilarity * 0.05);

        var analysis = BuildAnalysis(stats1, stats2, kdRatioDiff, killRateDiff, scorePerRoundDiff, mapSimilarity, serverSimilarity);

        return new StatSimilarityAnalysis(
            Score: Math.Min(1.0, overallScore),
            KdRatioDifference: kdRatioDiff,
            KillRateDifference: killRateDiff,
            ScorePerRoundDifference: scorePerRoundDiff,
            MapPerformanceSimilarity: mapSimilarity,
            ServerPerformanceSimilarity: serverSimilarity,
            Analysis: analysis
        );
    }

    /// <summary>
    /// Normalize the difference between two values.
    /// Returns 1.0 if values are within tolerance, 0.0 if very different.
    /// </summary>
    private static double CalculateNormalizedDifference(double val1, double val2, double minThreshold, double maxThreshold)
    {
        if (val1 == 0 && val2 == 0)
            return 1.0; // Both zero = identical

        if (val1 == 0 || val2 == 0)
            return 0.0; // One zero, one not = very different

        var ratio = val1 / val2;
        if (ratio < 1)
            ratio = 1 / ratio; // Normalize to > 1

        // ratio of 1.0 = identical (score 1.0)
        // ratio of 1.05 = very similar (score 0.95)
        // ratio of 1.2 = somewhat similar (score 0.70)
        // ratio of 2.0 = different (score ~0.15)
        // ratio of 3.0+ = very different (score ~0.0)

        var similarity = Math.Pow(1.0 / ratio, 1.5); // Steeper penalty for divergence
        return Math.Max(0.0, Math.Min(1.0, similarity));
    }

    /// <summary>
    /// Compare map-specific performance patterns.
    /// Uses cosine similarity on K/D vectors across maps.
    /// </summary>
    private static double CalculateMapPerformanceSimilarity(List<ServerStatistics> maps1, List<ServerStatistics> maps2)
    {
        if (maps1.Count == 0 || maps2.Count == 0)
            return 0.5; // Neutral score if no map data

        // Build vectors of K/D ratios for each map
        var mapNames1 = maps1.Select(m => m.MapName).ToHashSet();
        var mapNames2 = maps2.Select(m => m.MapName).ToHashSet();
        var commonMaps = mapNames1.Intersect(mapNames2).ToList();

        if (commonMaps.Count == 0)
            return 0.3; // Different map pools = likely different players

        // Calculate K/D for each common map
        var kds1 = new List<double>();
        var kds2 = new List<double>();

        foreach (var mapName in commonMaps)
        {
            var map1 = maps1.FirstOrDefault(m => m.MapName == mapName);
            var map2 = maps2.FirstOrDefault(m => m.MapName == mapName);

            if (map1?.TotalKills > 0 && map2?.TotalKills > 0)
            {
                var kd1 = map1.TotalDeaths > 0 ? (double)map1.TotalKills / map1.TotalDeaths : map1.TotalKills;
                var kd2 = map2.TotalDeaths > 0 ? (double)map2.TotalKills / map2.TotalDeaths : map2.TotalKills;
                kds1.Add(kd1);
                kds2.Add(kd2);
            }
        }

        if (kds1.Count == 0)
            return 0.5;

        // Cosine similarity on K/D vectors
        return CosineSimilarity(kds1, kds2);
    }

    /// <summary>
    /// Compare server-specific performance patterns.
    /// </summary>
    private static double CalculateServerPerformanceSimilarity(List<ServerInsight> servers1, List<ServerInsight> servers2)
    {
        if (servers1.Count == 0 || servers2.Count == 0)
            return 0.5;

        // Get common servers
        var guids1 = servers1.Select(s => s.ServerGuid).ToHashSet();
        var guids2 = servers2.Select(s => s.ServerGuid).ToHashSet();
        var commonServers = guids1.Intersect(guids2).ToList();

        if (commonServers.Count == 0)
            return 0.3; // Different server pools

        // For each common server, compare K/D performance
        var kds1 = new List<double>();
        var kds2 = new List<double>();

        foreach (var guid in commonServers)
        {
            var server1 = servers1.FirstOrDefault(s => s.ServerGuid == guid);
            var server2 = servers2.FirstOrDefault(s => s.ServerGuid == guid);

            if (server1?.TotalKills > 0 && server2?.TotalKills > 0)
            {
                var kd1 = server1.KdRatio;
                var kd2 = server2.KdRatio;
                kds1.Add(kd1);
                kds2.Add(kd2);
            }
        }

        if (kds1.Count == 0)
            return 0.5;

        return CosineSimilarity(kds1, kds2);
    }

    /// <summary>
    /// Calculate cosine similarity between two vectors.
    /// </summary>
    private static double CosineSimilarity(List<double> vec1, List<double> vec2)
    {
        if (vec1.Count != vec2.Count || vec1.Count == 0)
            return 0.0;

        var dotProduct = 0.0;
        var magnitude1 = 0.0;
        var magnitude2 = 0.0;

        for (int i = 0; i < vec1.Count; i++)
        {
            dotProduct += vec1[i] * vec2[i];
            magnitude1 += vec1[i] * vec1[i];
            magnitude2 += vec2[i] * vec2[i];
        }

        magnitude1 = Math.Sqrt(magnitude1);
        magnitude2 = Math.Sqrt(magnitude2);

        if (magnitude1 == 0 || magnitude2 == 0)
            return 0.0;

        var similarity = dotProduct / (magnitude1 * magnitude2);
        return Math.Max(0.0, Math.Min(1.0, similarity));
    }

    private static string BuildAnalysis(
        PlayerLifetimeStats stats1,
        PlayerLifetimeStats stats2,
        double kdDiff,
        double killRateDiff,
        double scorePerRoundDiff,
        double mapSim,
        double serverSim)
    {
        var parts = new List<string>();

        parts.Add($"{stats1.PlayerName} K/D: {stats1.KdRatio:F2}, {stats2.PlayerName} K/D: {stats2.KdRatio:F2}");

        if (kdDiff > 0.85)
            parts.Add("K/D ratios nearly identical");
        else if (kdDiff > 0.70)
            parts.Add("K/D ratios very similar");
        else if (kdDiff < 0.30)
            parts.Add("K/D ratios significantly different");

        if (mapSim > 0.80)
            parts.Add("Very similar map performance patterns");
        else if (mapSim < 0.40)
            parts.Add("Different map performance patterns");

        if (serverSim > 0.80)
            parts.Add("Very similar server performance");

        return string.Join("; ", parts);
    }
}
