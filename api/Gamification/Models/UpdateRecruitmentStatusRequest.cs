using System.ComponentModel.DataAnnotations;
using api.PlayerTracking;

namespace api.Gamification.Models;

/// <summary>
/// Request to update team recruitment status
/// </summary>
public record UpdateRecruitmentStatusRequest
{
    [Required]
    public TeamRecruitmentStatus RecruitmentStatus { get; init; }
}
