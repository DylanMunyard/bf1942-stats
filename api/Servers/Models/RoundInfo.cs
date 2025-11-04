using System;

namespace api.ServerStats.Models;

public class RoundInfo
{
    public string RoundId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsActive { get; set; }
    public int ParticipantCount { get; set; }
    public string? WinningTeamLabel { get; set; }
    public int? WinningTeamScore { get; set; }
    public int? LosingTeamScore { get; set; }
    public string? TopPlayerName { get; set; }
    public int? TopPlayerScore { get; set; }
}