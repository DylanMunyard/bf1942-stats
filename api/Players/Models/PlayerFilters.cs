namespace api.PlayerStats.Models;

public class PlayerFilters
{
    public string? PlayerName { get; set; }
    public int? MinPlayTime { get; set; }
    public int? MaxPlayTime { get; set; }
    public DateTime? LastSeenFrom { get; set; }
    public DateTime? LastSeenTo { get; set; }
    public bool? IsActive { get; set; }
    public string? ServerName { get; set; }
    public string? GameId { get; set; }
    public string? Game { get; set; }
    public string? MapName { get; set; }

    // Additional session-specific filters
    public string? ServerGuid { get; set; }
    public string? GameType { get; set; }
    public DateTime? StartTimeFrom { get; set; }
    public DateTime? StartTimeTo { get; set; }
    public int? MinScore { get; set; }
    public int? MaxScore { get; set; }
    public int? MinKills { get; set; }
    public int? MaxKills { get; set; }
    public int? MinDeaths { get; set; }
    public int? MaxDeaths { get; set; }
}
