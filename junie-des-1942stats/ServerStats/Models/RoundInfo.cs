using System;

namespace junie_des_1942stats.ServerStats.Models;

public class RoundInfo
{
    public string RoundId { get; set; } = string.Empty;
    public string MapName { get; set; } = string.Empty;
    public DateTime StartTime { get; set; }
    public DateTime EndTime { get; set; }
    public bool IsActive { get; set; }
}