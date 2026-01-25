namespace api.AdminData.Models;

public record SuspiciousSessionResponse(
    string PlayerName,
    string ServerName,
    int TotalScore,
    int TotalKills,
    int TotalDeaths,
    double KdRatio,
    string RoundId,
    DateTime RoundStartTime,
    bool RoundIsDeleted = false
);
