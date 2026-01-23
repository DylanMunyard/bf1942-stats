using System.ComponentModel.DataAnnotations;

namespace api.Gamification.Models;

/// <summary>
/// Request to create a new team
/// </summary>
public record CreateTeamRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string TeamName { get; init; } = "";

    [StringLength(20)]
    public string? Tag { get; init; }

    [Required]
    public string PlayerName { get; init; } = "";

    [Required]
    public bool RulesAcknowledged { get; init; }
}
