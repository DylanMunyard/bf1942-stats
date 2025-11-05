using api.Gamification.Models;

namespace api.Gamification.Services;

public interface IBadgeDefinitionsService
{
    BadgeDefinition? GetBadgeDefinition(string badgeId);
    List<BadgeDefinition> GetAllBadges();
    List<BadgeDefinition> GetBadgesByCategory(string category);
    List<BadgeDefinition> GetBadgesByTier(string tier);
}
