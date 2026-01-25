using NodaTime;

namespace api.AdminData.Models;

public record RoundDetailResponse(
    string RoundId,
    string ServerName,
    Instant StartTime,
    Instant? EndTime,
    string MapName,
    List<RoundPlayerInfo> Players,
    int AchievementCount,
    bool IsDeleted = false
);

public record RoundPlayerInfo(
    string PlayerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths
);
