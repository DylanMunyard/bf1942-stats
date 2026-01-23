using api.PlayerTracking;
using NodaTime;

namespace api.Gamification.Models;

/// <summary>
/// User's registration status for a tournament
/// </summary>
public record RegistrationStatusResponse
{
    public bool IsRegistrationOpen { get; init; }
    public List<string> LinkedPlayerNames { get; init; } = [];
    public TeamMembershipInfo? TeamMembership { get; init; }
    /// <summary>
    /// True if the user is the tournament admin (creator)
    /// </summary>
    public bool IsTournamentAdmin { get; init; }
}

/// <summary>
/// Information about the user's team membership
/// </summary>
public record TeamMembershipInfo
{
    public int TeamId { get; init; }
    public string TeamName { get; init; } = "";
    public string? Tag { get; init; }
    public bool IsLeader { get; init; }
    public string PlayerName { get; init; } = "";
    public Instant JoinedAt { get; init; }
    /// <summary>
    /// Membership status: 0=Pending, 1=Approved
    /// </summary>
    public TeamMembershipStatus MembershipStatus { get; init; }
}
