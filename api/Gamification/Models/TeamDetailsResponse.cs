using api.PlayerTracking;
using NodaTime;

namespace api.Gamification.Models;

/// <summary>
/// Detailed team information for team leaders
/// </summary>
public record TeamDetailsResponse
{
    public int TeamId { get; init; }
    public string TeamName { get; init; } = "";
    public string? Tag { get; init; }
    public Instant CreatedAt { get; init; }
    public TeamRecruitmentStatus RecruitmentStatus { get; init; }
    public List<TeamPlayerInfo> Players { get; init; } = [];
}

/// <summary>
/// Information about a team player
/// </summary>
public record TeamPlayerInfo
{
    public string PlayerName { get; init; } = "";
    public bool IsLeader { get; init; }
    public bool RulesAcknowledged { get; init; }
    public Instant JoinedAt { get; init; }
    public int? UserId { get; init; }
    public TeamMembershipStatus MembershipStatus { get; init; }
}
