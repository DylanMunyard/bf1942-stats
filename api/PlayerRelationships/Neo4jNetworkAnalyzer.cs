using Neo4j.Driver;
using api.PlayerRelationships.Models;

namespace api.PlayerRelationships;

/// <summary>
/// Analyzes player networks and temporal consistency using Neo4j.
/// </summary>
public class Neo4jNetworkAnalyzer(IDriver neoDriver)
{
    /// <summary>
    /// Analyze network similarity and temporal consistency between two players.
    /// </summary>
    public async Task<(NetworkAnalysis NetworkAnalysis, TemporalAnalysis TemporalAnalysis)> AnalyzeNetworkAndTemporalAsync(
        string player1,
        string player2,
        int lookBackDays = 90)
    {
        try
        {
            using var session = neoDriver.AsyncSession();

            var networkAnalysis = await AnalyzeNetworkSimilarityAsync(session, player1, player2);
            var temporalAnalysis = await AnalyzeTemporalConsistencyAsync(session, player1, player2, lookBackDays);

            return (networkAnalysis, temporalAnalysis);
        }
        catch (Exception)
        {
            // Neo4j unavailable or data missing - return neutral analyses
            return (
                new NetworkAnalysis(
                    Score: 0.5,
                    SharedTeammateCount: 0,
                    TeammateOverlapPercentage: 0.0,
                    MutualConnectionScore: 0.0,
                    HasDirectConnection: false,
                    NetworkShapeSimilarity: 0.5,
                    Analysis: "Neo4j data unavailable"
                ),
                new TemporalAnalysis(
                    Score: 0.5,
                    TemporalOverlapMinutes: 0,
                    SignificantTemporalOverlap: false,
                    InvertedActivityScore: 0.5,
                    ActivityGapConsistency: 0.5,
                    Analysis: "Neo4j data unavailable"
                )
            );
        }
    }

    private async Task<NetworkAnalysis> AnalyzeNetworkSimilarityAsync(
        IAsyncSession session,
        string player1,
        string player2)
    {
        // Get teammates for each player - use OPTIONAL MATCH to handle players with no teammates
        var query = """
            MATCH (p1:Player {name: $player1})
            OPTIONAL MATCH (p1)-[r1:PLAYED_WITH]-(t1:Player)
            WITH p1, collect(DISTINCT t1.name) as teammates1
            
            MATCH (p2:Player {name: $player2})
            OPTIONAL MATCH (p2)-[r2:PLAYED_WITH]-(t2:Player)
            WITH p1, teammates1, p2, collect(DISTINCT t2.name) as teammates2
            
            WITH teammates1, teammates2,
                 size([x IN teammates1 WHERE x IN teammates2]) as intersection
            
            RETURN intersection,
                   size(teammates1) as count1,
                   size(teammates2) as count2,
                   size([x IN teammates1 WHERE x IN teammates2]) +
                   size([x IN teammates2 WHERE NOT (x IN teammates1)]) as union
            """;

        var result = await session.ExecuteReadAsync(async tx =>
        {
            var res = await tx.RunAsync(query, new { player1, player2 });
            var records = await res.ToListAsync();
            if (records.Count == 0)
            {
                // Players don't exist in Neo4j - return neutral analysis
                return null;
            }
            return records.First();
        });

        // If players don't exist in Neo4j, return neutral analysis
        if (result == null)
        {
            return new NetworkAnalysis(
                Score: 0.5,
                SharedTeammateCount: 0,
                TeammateOverlapPercentage: 0.0,
                MutualConnectionScore: 0.0,
                HasDirectConnection: false,
                NetworkShapeSimilarity: 0.5,
                Analysis: "Insufficient Neo4j data"
            );
        }

        var sharedTeammates = result["intersection"].As<int>();
        var count1 = result["count1"].As<int>();
        var count2 = result["count2"].As<int>();
        var union = result["union"].As<int>();

        var jaccardSimilarity = union > 0 ? (double)sharedTeammates / union : 0.0;

        // Check for direct connection (they played together)
        var hasConnection = await CheckDirectConnectionAsync(session, player1, player2);

        // Get shared mutual connections (friends of friends)
        var mutualConnections = await GetMutualConnectionsAsync(session, player1, player2);

        // Calculate network shape similarity
        var networkShapeSimilarity = await CalculateNetworkShapeSimilarityAsync(session, player1, player2);

        // Score: high teammate overlap + no direct connection = suspicious
        var score = (jaccardSimilarity * 0.6) + (networkShapeSimilarity * 0.4);

        // If they have direct connection, that's actually suspicious too (could be same session)
        // but it slightly lowers the alias likelihood
        if (hasConnection)
            score *= 0.9;

        var analysis = $"Shared teammates: {sharedTeammates}; " +
                      $"Player1 teammates: {count1}, Player2 teammates: {count2}; " +
                      $"Direct connection: {(hasConnection ? "YES" : "NO")}; " +
                      $"Mutual connections: {mutualConnections}";

        return new NetworkAnalysis(
            Score: Math.Min(1.0, score),
            SharedTeammateCount: sharedTeammates,
            TeammateOverlapPercentage: jaccardSimilarity,
            MutualConnectionScore: Math.Min(1.0, (double)mutualConnections / 10), // Score capped at 10 mutual connections
            HasDirectConnection: hasConnection,
            NetworkShapeSimilarity: networkShapeSimilarity,
            Analysis: analysis
        );
    }

    private async Task<bool> CheckDirectConnectionAsync(IAsyncSession session, string player1, string player2)
    {
        var query = """
            MATCH (p1:Player {name: $player1})-[r:PLAYED_WITH]-(p2:Player {name: $player2})
            RETURN COUNT(r) as count
            """;

        try
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var res = await tx.RunAsync(query, new { player1, player2 });
                var records = await res.ToListAsync();
                if (records.Count == 0)
                    return 0L;
                return records.First()["count"].As<long>();
            });

            return result > 0;
        }
        catch
        {
            return false;
        }
    }

    private async Task<int> GetMutualConnectionsAsync(IAsyncSession session, string player1, string player2)
    {
        var query = """
            MATCH (p1:Player {name: $player1})-[r1:PLAYED_WITH]-(mutual:Player)-[r2:PLAYED_WITH]-(p2:Player {name: $player2})
            WHERE mutual.name <> $player1 AND mutual.name <> $player2
            RETURN COUNT(DISTINCT mutual) as count
            """;

        try
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var res = await tx.RunAsync(query, new { player1, player2 });
                var records = await res.ToListAsync();
                if (records.Count == 0)
                    return 0L;
                return records.First()["count"].As<long>();
            });

            return (int)result;
        }
        catch
        {
            return 0;
        }
    }

    private async Task<double> CalculateNetworkShapeSimilarityAsync(IAsyncSession session, string player1, string player2)
    {
        // Get network metrics (degree centrality, clustering coefficient)
        var query = """
            MATCH (p1:Player {name: $player1})
            OPTIONAL MATCH (p1)-[r1:PLAYED_WITH]-(t1:Player)
            WITH count(r1) as degree1
            
            MATCH (p2:Player {name: $player2})
            OPTIONAL MATCH (p2)-[r2:PLAYED_WITH]-(t2:Player)
            WITH degree1, count(r2) as degree2
            
            RETURN degree1, degree2
            """;

        try
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var res = await tx.RunAsync(query, new { player1, player2 });
                var records = await res.ToListAsync();
                if (records.Count == 0)
                    return null;
                return records.First();
            });

            if (result == null)
                return 0.5;

            var degree1 = result["degree1"].As<long>();
            var degree2 = result["degree2"].As<long>();

            if (degree1 == 0 || degree2 == 0)
                return 0.5;

            // Compare network sizes
            var ratio = (double)degree1 / degree2;
            if (ratio < 1)
                ratio = 1 / ratio;

            // Normalize: similar sizes = high score
            return Math.Max(0.0, 1.0 - Math.Min(1.0, (ratio - 1) / 2));
        }
        catch
        {
            return 0.5;
        }
    }

    private async Task<TemporalAnalysis> AnalyzeTemporalConsistencyAsync(
        IAsyncSession session,
        string player1,
        string player2,
        int lookBackDays)
    {
        // Check if they ever appear in same session (would indicate NOT aliases)
        var coSessionCount = await GetCoSessionCountAsync(session, player1, player2, lookBackDays);

        // Get activity periods
        var activities = await GetActivityPeriodsAsync(session, player1, player2, lookBackDays);

        var score = 1.0;
        var redFlags = new List<string>();

        if (coSessionCount > 0)
        {
            // They played together - reduces alias likelihood significantly
            score *= 0.3;
            redFlags.Add("Played together in sessions");
        }

        // Check for temporal separation (one plays while other doesn't)
        var invertedActivityScore = CalculateInvertedActivityScore(activities.LastSessionPlayer1, activities.LastSessionPlayer2);
        score *= invertedActivityScore;

        var analysis = $"Co-sessions in period: {coSessionCount}; " +
                      $"Player1 last active: {activities.LastSessionPlayer1:yyyy-MM-dd}; " +
                      $"Player2 last active: {activities.LastSessionPlayer2:yyyy-MM-dd}; " +
                      $"Temporal separation score: {invertedActivityScore:F2}";

        if (coSessionCount > 10)
            analysis = "SIGNIFICANT CO-SESSION OVERLAP - Not aliases";
        else if (coSessionCount > 0)
            analysis = "Some co-sessions found - unlikely aliases";

        return new TemporalAnalysis(
            Score: Math.Min(1.0, score),
            TemporalOverlapMinutes: 0, // TODO: Compute actual overlap from session data
            SignificantTemporalOverlap: coSessionCount > 10,
            InvertedActivityScore: invertedActivityScore,
            ActivityGapConsistency: 0.5, // TODO: Compute gap consistency
            Analysis: analysis
        );
    }

    private async Task<int> GetCoSessionCountAsync(IAsyncSession session, string player1, string player2, int lookBackDays)
    {
        var query = """
            MATCH (p1:Player {name: $player1})-[r1:PLAYED_WITH]-(mutual:Player)-[r2:PLAYED_WITH]-(p2:Player {name: $player2})
            WHERE r1.lastPlayedTogether > datetime({timezone: "UTC"}).minus(duration({days: $lookBackDays}))
            RETURN COUNT(DISTINCT mutual) as count
            """;

        try
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var res = await tx.RunAsync(query, new { player1, player2, lookBackDays });
                return await res.SingleAsync();
            });

            return (int)result["count"].As<long>();
        }
        catch
        {
            // Query might fail if relationship data doesn't have timestamp
            return 0;
        }
    }

    private async Task<(DateTime LastSessionPlayer1, DateTime LastSessionPlayer2)> GetActivityPeriodsAsync(
        IAsyncSession session,
        string player1,
        string player2,
        int lookBackDays)
    {
        var query = """
            MATCH (p1:Player {name: $player1})-[r1:PLAYED_WITH]-(t1:Player)
            WITH max(r1.lastPlayedTogether) as lastActivity1

            MATCH (p2:Player {name: $player2})-[r2:PLAYED_WITH]-(t2:Player)
            WITH lastActivity1, max(r2.lastPlayedTogether) as lastActivity2

            RETURN lastActivity1, lastActivity2
            """;

        try
        {
            var result = await session.ExecuteReadAsync(async tx =>
            {
                var res = await tx.RunAsync(query, new { player1, player2 });
                return await res.SingleAsync();
            });

            var last1 = result["lastActivity1"].As<DateTime?>();
            var last2 = result["lastActivity2"].As<DateTime?>();

            return (last1 ?? DateTime.MinValue, last2 ?? DateTime.MinValue);
        }
        catch
        {
            return (DateTime.MinValue, DateTime.MinValue);
        }
    }

    private static double CalculateInvertedActivityScore(DateTime lastActive1, DateTime lastActive2)
    {
        if (lastActive1 == DateTime.MinValue || lastActive2 == DateTime.MinValue)
            return 0.5;

        var gap = Math.Abs((lastActive1 - lastActive2).TotalDays);

        // If activities are far apart (e.g., one active, then other active days later), higher score
        if (gap > 30)
            return 0.9; // Strong separation
        if (gap > 14)
            return 0.7;
        if (gap > 7)
            return 0.5;

        return 0.2; // Overlapping activity
    }
}
