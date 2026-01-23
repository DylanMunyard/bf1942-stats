using System.ComponentModel.DataAnnotations;

namespace api.Gamification.Models;

/// <summary>
/// Request to join an existing team
/// </summary>
public record JoinTeamRequest
{
    [Required]
    public string PlayerName { get; init; } = "";

    [Required]
    public bool RulesAcknowledged { get; init; }
}
