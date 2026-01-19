using System.ComponentModel.DataAnnotations;

namespace api.Gamification.Models;

/// <summary>
/// Request to add a player to the team (leader only)
/// </summary>
public record AddPlayerRequest
{
    [Required]
    public string PlayerName { get; init; } = "";
}
