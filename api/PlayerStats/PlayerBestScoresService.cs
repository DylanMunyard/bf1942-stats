using System.Diagnostics;
using api.Data.Entities;
using api.PlayerTracking;
using api.Telemetry;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using NodaTime;

namespace api.PlayerStats;

/// <summary>
/// Service for updating player best scores when sessions complete.
/// Maintains top 3 scores per player for each time period.
/// </summary>
public class PlayerBestScoresService(
    PlayerTrackerDbContext dbContext,
    ILogger<PlayerBestScoresService> logger,
    IClock clock
) : IPlayerBestScoresService
{
    private static readonly string[] Periods = ["all_time", "last_30_days", "this_week"];

    public async Task UpdateBestScoresForCompletedSessionsAsync(
        IEnumerable<PlayerSession> completedSessions,
        CancellationToken ct = default)
    {
        var sessions = completedSessions.ToList();
        if (sessions.Count == 0) return;

        logger.LogInformation("Processing {SessionCount} completed sessions for best scores", sessions.Count);

        using var activity = ActivitySources.SqliteAnalytics.StartActivity("UpdateBestScoresForCompletedSessions");
        var stopwatch = Stopwatch.StartNew();

        // Filter to sessions with positive scores worth tracking
        var qualifyingSessions = sessions
            .Where(s => s.TotalScore > 0)
            .ToList();

        if (qualifyingSessions.Count == 0)
        {
            logger.LogDebug("No qualifying sessions with positive scores out of {TotalSessions} sessions", sessions.Count);
            return;
        }

        logger.LogInformation(
            "Found {QualifyingCount} sessions with positive scores out of {TotalCount} total",
            qualifyingSessions.Count, sessions.Count);

        var playerNames = qualifyingSessions.Select(s => s.PlayerName).Distinct().ToList();
        var updatedCount = 0;

        foreach (var playerName in playerNames)
        {
            var playerSessions = qualifyingSessions
                .Where(s => s.PlayerName == playerName)
                .OrderByDescending(s => s.TotalScore)
                .ToList();

            foreach (var period in Periods)
            {
                var updated = await TryUpdateBestScoresForPeriodAsync(playerName, playerSessions, period, ct);
                if (updated) updatedCount++;
            }
        }

        stopwatch.Stop();
        activity?.SetTag("result.sessions_checked", qualifyingSessions.Count);
        activity?.SetTag("result.players_processed", playerNames.Count);
        activity?.SetTag("result.periods_updated", updatedCount);
        activity?.SetTag("result.duration_ms", stopwatch.ElapsedMilliseconds);

        if (updatedCount > 0)
        {
            logger.LogInformation(
                "Updated best scores: {UpdatedCount} period updates for {PlayerCount} players from {SessionCount} sessions in {Duration}ms",
                updatedCount, playerNames.Count, qualifyingSessions.Count, stopwatch.ElapsedMilliseconds);
        }
        else
        {
            logger.LogDebug(
                "No best score updates needed for {PlayerCount} players from {SessionCount} sessions in {Duration}ms",
                playerNames.Count, qualifyingSessions.Count, stopwatch.ElapsedMilliseconds);
        }
    }

    private async Task<bool> TryUpdateBestScoresForPeriodAsync(
        string playerName,
        List<PlayerSession> newSessions,
        string period,
        CancellationToken ct)
    {
        // Get current best scores for this player and period
        // Use AsNoTracking since we only need data for comparison, and ExecuteDeleteAsync doesn't clear the tracker
        var currentBest = await dbContext.PlayerBestScores
            .AsNoTracking()
            .Where(pbs => pbs.PlayerName == playerName && pbs.Period == period)
            .OrderBy(pbs => pbs.Rank)
            .ToListAsync(ct);

        // Get the minimum score to beat (rank 3's score, or 0 if less than 3 records)
        var minScoreToBeat = currentBest.Count >= 3
            ? currentBest[2].FinalScore
            : 0;

        // Filter sessions that could qualify for this period
        var now = clock.GetCurrentInstant();
        var periodCutoff = GetPeriodCutoff(period, now);

        var qualifyingSessions = newSessions
            .Where(s => s.TotalScore > minScoreToBeat)
            .Where(s => periodCutoff == null || Instant.FromDateTimeUtc(
                DateTime.SpecifyKind(s.LastSeenTime, DateTimeKind.Utc)) >= periodCutoff)
            .ToList();

        if (qualifyingSessions.Count == 0)
        {
            var bestNewScore = newSessions.Count > 0 ? newSessions.Max(s => s.TotalScore) : 0;
            logger.LogDebug(
                "Player {PlayerName} period {Period}: best new score {BestNewScore} doesn't beat minimum {MinScore} (current records: {CurrentCount})",
                playerName, period, bestNewScore, minScoreToBeat, currentBest.Count);
            return false;
        }

        logger.LogDebug(
            "Player {PlayerName} period {Period}: {QualifyingCount} sessions qualify (best: {BestScore}, min to beat: {MinScore})",
            playerName, period, qualifyingSessions.Count, qualifyingSessions.Max(s => s.TotalScore), minScoreToBeat);

        // Merge new qualifying sessions with existing best scores
        var allCandidates = currentBest
            .Select(pbs => new ScoreCandidate
            {
                Score = pbs.FinalScore,
                Kills = pbs.FinalKills,
                Deaths = pbs.FinalDeaths,
                MapName = pbs.MapName,
                ServerGuid = pbs.ServerGuid,
                RoundEndTime = pbs.RoundEndTime,
                RoundId = pbs.RoundId
            })
            .Concat(qualifyingSessions.Select(s => new ScoreCandidate
            {
                Score = s.TotalScore,
                Kills = s.TotalKills,
                Deaths = s.TotalDeaths,
                MapName = s.MapName,
                ServerGuid = s.ServerGuid,
                RoundEndTime = Instant.FromDateTimeUtc(DateTime.SpecifyKind(s.LastSeenTime, DateTimeKind.Utc)),
                RoundId = s.RoundId ?? ""
            }))
            .OrderByDescending(c => c.Score)
            .Take(3)
            .ToList();

        // Delete existing records for this player/period
        await dbContext.PlayerBestScores
            .Where(pbs => pbs.PlayerName == playerName && pbs.Period == period)
            .ExecuteDeleteAsync(ct);

        // Insert new top 3
        for (var rank = 1; rank <= allCandidates.Count; rank++)
        {
            var candidate = allCandidates[rank - 1];
            dbContext.PlayerBestScores.Add(new PlayerBestScore
            {
                PlayerName = playerName,
                Period = period,
                Rank = rank,
                FinalScore = candidate.Score,
                FinalKills = candidate.Kills,
                FinalDeaths = candidate.Deaths,
                MapName = candidate.MapName,
                ServerGuid = candidate.ServerGuid,
                RoundEndTime = candidate.RoundEndTime,
                RoundId = candidate.RoundId
            });
        }

        await dbContext.SaveChangesAsync(ct);

        logger.LogInformation(
            "Player {PlayerName} period {Period}: updated top {Count} scores [{Scores}]",
            playerName, period, allCandidates.Count,
            string.Join(", ", allCandidates.Select(c => c.Score)));

        return true;
    }

    private static Instant? GetPeriodCutoff(string period, Instant now)
    {
        return period switch
        {
            "this_week" => now.Minus(Duration.FromDays(7)),
            "last_30_days" => now.Minus(Duration.FromDays(30)),
            "all_time" => null,
            _ => null
        };
    }

    private record ScoreCandidate
    {
        public int Score { get; init; }
        public int Kills { get; init; }
        public int Deaths { get; init; }
        public required string MapName { get; init; }
        public required string ServerGuid { get; init; }
        public Instant RoundEndTime { get; init; }
        public required string RoundId { get; init; }
    }
}
