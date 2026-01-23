using NodaTime;

namespace api.Gamification.Models;

/// <summary>
/// Response after creating a team
/// </summary>
public record CreateTeamResponse
{
    public int TeamId { get; init; }
    public string TeamName { get; init; } = "";
    public string? Tag { get; init; }
    public Instant CreatedAt { get; init; }
}
