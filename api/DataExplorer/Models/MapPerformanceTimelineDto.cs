namespace api.DataExplorer.Models;

public record MapPerformanceTimelineResponse(string PlayerName, string Game, List<MapTimelineMonthDto> Months);

public record MapTimelineMonthDto(int Year, int Month, string MonthLabel, List<MapTimelineEntryDto> Maps);

public record MapTimelineEntryDto(string MapName, int Kills, int Deaths, double KdRatio, int Score, int Sessions, double PlayTimeMinutes);