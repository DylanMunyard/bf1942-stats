namespace api.AdminData.Models;

public record BulkDeleteRoundsResponse(
    int RoundsDeleted,
    int DeletedAchievements,
    int DeletedSessions,
    int AffectedPlayers
);
