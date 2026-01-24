using Microsoft.EntityFrameworkCore;
using api.PlayerTracking;
using api.Gamification.Models;
using NodaTime;
using NodaTime.Text;

namespace api.Gamification.Services;

public class TournamentFeedService(PlayerTrackerDbContext dbContext)
{
    private static readonly InstantPattern InstantExtendedIsoPattern = InstantPattern.ExtendedIso;

    public async Task<TournamentFeedResponse> GetFeedAsync(
        int tournamentId,
        Instant? cursor,
        int limit = 10)
    {
        var now = SystemClock.Instance.GetCurrentInstant();
        var feedItems = new List<(Instant Timestamp, TournamentFeedItem Item)>();

        // 1. Query published posts where PublishAt <= now (or null means immediate)
        var postsQuery = dbContext.TournamentPosts
            .Where(p => p.TournamentId == tournamentId)
            .Where(p => p.Status == "published")
            .Where(p => p.PublishAt == null || p.PublishAt <= now);

        if (cursor.HasValue)
        {
            postsQuery = postsQuery.Where(p =>
                (p.PublishAt ?? p.CreatedAt) < cursor.Value);
        }

        var posts = await postsQuery
            .OrderByDescending(p => p.PublishAt ?? p.CreatedAt)
            .Take(limit + 1)
            .Select(p => new
            {
                p.Id,
                p.Title,
                p.Content,
                PublishAt = p.PublishAt,
                p.CreatedAt
            })
            .ToListAsync();

        foreach (var post in posts)
        {
            var effectiveTimestamp = post.PublishAt ?? post.CreatedAt;
            feedItems.Add((effectiveTimestamp, new TournamentFeedItem(
                "post",
                FormatInstant(effectiveTimestamp),
                new FeedPostData(
                    post.Id,
                    post.Title,
                    post.Content,
                    post.PublishAt.HasValue ? FormatInstant(post.PublishAt.Value) : null,
                    FormatInstant(post.CreatedAt)
                )
            )));
        }

        // 2. Query match results by CreatedAt
        var resultsQuery = dbContext.TournamentMatchResults
            .Where(r => r.TournamentId == tournamentId);

        if (cursor.HasValue)
        {
            resultsQuery = resultsQuery.Where(r => r.CreatedAt < cursor.Value);
        }

        var results = await resultsQuery
            .OrderByDescending(r => r.CreatedAt)
            .Take(limit + 1)
            .Include(r => r.Team1)
            .Include(r => r.Team2)
            .Include(r => r.WinningTeam)
            .Include(r => r.Map)
            .Select(r => new
            {
                r.MatchId,
                ResultId = r.Id,
                Team1Name = r.Team1 != null ? r.Team1.Name : "Unknown",
                Team2Name = r.Team2 != null ? r.Team2.Name : "Unknown",
                r.Team1Tickets,
                r.Team2Tickets,
                WinningTeamName = r.WinningTeam != null ? r.WinningTeam.Name : null,
                MapName = r.Map != null ? r.Map.MapName : "Unknown",
                r.CreatedAt
            })
            .ToListAsync();

        foreach (var result in results)
        {
            feedItems.Add((result.CreatedAt, new TournamentFeedItem(
                "match_result",
                FormatInstant(result.CreatedAt),
                new FeedMatchResultData(
                    result.MatchId,
                    result.ResultId,
                    result.Team1Name,
                    result.Team2Name,
                    result.Team1Tickets,
                    result.Team2Tickets,
                    result.WinningTeamName,
                    result.MapName,
                    FormatInstant(result.CreatedAt)
                )
            )));
        }

        // 3. Query team creations by CreatedAt
        var teamsQuery = dbContext.TournamentTeams
            .Where(t => t.TournamentId == tournamentId);

        if (cursor.HasValue)
        {
            teamsQuery = teamsQuery.Where(t => t.CreatedAt < cursor.Value);
        }

        var teams = await teamsQuery
            .OrderByDescending(t => t.CreatedAt)
            .Take(limit + 1)
            .Select(t => new
            {
                TeamId = t.Id,
                TeamName = t.Name,
                t.CreatedAt
            })
            .ToListAsync();

        foreach (var team in teams)
        {
            feedItems.Add((team.CreatedAt, new TournamentFeedItem(
                "team_created",
                FormatInstant(team.CreatedAt),
                new FeedTeamCreatedData(
                    team.TeamId,
                    team.TeamName,
                    FormatInstant(team.CreatedAt)
                )
            )));
        }

        // 4. Query match schedulings by CreatedAt
        var matchesQuery = dbContext.TournamentMatches
            .Where(m => m.TournamentId == tournamentId);

        if (cursor.HasValue)
        {
            matchesQuery = matchesQuery.Where(m => m.CreatedAt < cursor.Value);
        }

        var matches = await matchesQuery
            .OrderByDescending(m => m.CreatedAt)
            .Take(limit + 1)
            .Include(m => m.Team1)
            .Include(m => m.Team2)
            .Include(m => m.Maps)
            .Select(m => new
            {
                MatchId = m.Id,
                Team1Name = m.Team1.Name,
                Team2Name = m.Team2.Name,
                m.ScheduledDate,
                m.Week,
                Maps = m.Maps.OrderBy(map => map.MapOrder).Select(map => map.MapName).ToList(),
                m.CreatedAt
            })
            .ToListAsync();

        foreach (var match in matches)
        {
            feedItems.Add((match.CreatedAt, new TournamentFeedItem(
                "match_scheduled",
                FormatInstant(match.CreatedAt),
                new FeedMatchScheduledData(
                    match.MatchId,
                    match.Team1Name,
                    match.Team2Name,
                    FormatInstant(match.ScheduledDate),
                    match.Week,
                    match.Maps,
                    FormatInstant(match.CreatedAt)
                )
            )));
        }

        // Merge all items by timestamp (descending) and apply pagination
        var sortedItems = feedItems
            .OrderByDescending(x => x.Timestamp)
            .Take(limit + 1)
            .ToList();

        var hasMore = sortedItems.Count > limit;
        var resultItems = sortedItems.Take(limit).Select(x => x.Item).ToList();

        string? nextCursor = null;
        if (hasMore && resultItems.Count > 0)
        {
            // Use the timestamp of the last returned item as the cursor
            var lastItem = sortedItems[limit - 1];
            nextCursor = FormatInstant(lastItem.Timestamp);
        }

        return new TournamentFeedResponse(resultItems, nextCursor, hasMore);
    }

    private static string FormatInstant(Instant instant) => InstantExtendedIsoPattern.Format(instant);
}
