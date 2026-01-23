namespace api.Gamification.Models;

public record AvailableTeamResponse(
    int Id,
    string Name,
    string? Tag,
    int PlayerCount);
