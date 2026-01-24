namespace api.Gamification.Models;

public record TournamentFeedItem(
    string Type, // "post", "match_result", "team_created", "match_scheduled"
    string Timestamp,
    object Data
);

public record TournamentFeedResponse(
    List<TournamentFeedItem> Items,
    string? NextCursor,
    bool HasMore
);

public record FeedPostData(
    int Id,
    string Title,
    string Content,
    string? PublishAt,
    string CreatedAt
);

public record FeedMatchResultData(
    int MatchId,
    int ResultId,
    string Team1Name,
    string Team2Name,
    int Team1Tickets,
    int Team2Tickets,
    string? WinningTeamName,
    string MapName,
    string CreatedAt
);

public record FeedTeamCreatedData(
    int TeamId,
    string TeamName,
    string CreatedAt
);

public record FeedMatchScheduledData(
    int MatchId,
    string Team1Name,
    string Team2Name,
    string ScheduledDate,
    string? Week,
    List<string> Maps,
    string CreatedAt
);
