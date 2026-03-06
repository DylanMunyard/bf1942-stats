namespace api.DataExplorer.Models;

public record PlayerActivityHeatmapResponse(string PlayerName, List<HeatmapCellDto> Cells, int TotalDays);

public record HeatmapCellDto(int DayOfWeek, int Hour, int MinutesActive, string? MostPlayedMap);