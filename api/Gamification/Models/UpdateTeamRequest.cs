using System.ComponentModel.DataAnnotations;

namespace api.Gamification.Models;

/// <summary>
/// Request to update team details (leader only)
/// </summary>
public record UpdateTeamRequest
{
    [Required]
    [StringLength(100, MinimumLength = 2)]
    public string TeamName { get; init; } = "";

    [StringLength(20)]
    public string? Tag { get; init; }
}
