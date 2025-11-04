namespace api.Gamification.Models;

public class BadgeDefinition
{
    public string Id { get; set; } = "";
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public string UIDescription { get; set; } = ""; // Concise, gamer-friendly description for UI
    public string Tier { get; set; } = "";
    public string Category { get; set; } = ""; // 'performance', 'milestone', 'social'
    public Dictionary<string, object> Requirements { get; set; } = new();
}
