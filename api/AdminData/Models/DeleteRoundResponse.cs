namespace api.AdminData.Models;

public record DeleteRoundResponse(
    string RoundId,
    int DeletedAchievements,
    int DeletedObservations,
    int DeletedSessions,
    int DeletedRounds,
    int AffectedPlayers
);
