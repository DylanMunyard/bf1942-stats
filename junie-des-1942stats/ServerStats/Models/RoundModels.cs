using PlayerStatsModels = junie_des_1942stats.PlayerStats.Models;

namespace junie_des_1942stats.ServerStats.Models;

public class RoundListItem
{
    public string RoundId { get; set; } = string.Empty;
    public string ServerName { get; set; } = string.Empty;
    public string ServerGuid { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public string? GameType { get; set; }
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public int DurationMinutes { get; set; }
    public int ParticipantCount { get; set; }
    public int TotalSessions { get; set; }
    public bool IsActive { get; set; }
    public string? Team1Label { get; set; }
    public string? Team2Label { get; set; }
}


public class RoundFilters
{
    public string? ServerName { get; set; }
    public string? ServerGuid { get; set; }
    public string? MapName { get; set; }
    public string? GameType { get; set; }
    public DateTime? StartTimeFrom { get; set; }
    public DateTime? StartTimeTo { get; set; }
    public DateTime? EndTimeFrom { get; set; }
    public DateTime? EndTimeTo { get; set; }
    public int? MinDuration { get; set; }
    public int? MaxDuration { get; set; }
    public int? MinParticipants { get; set; }
    public int? MaxParticipants { get; set; }
    public bool? IsActive { get; set; }
    public string? GameId { get; set; }
}

