using NodaTime;

namespace api.Data.Entities;

/// <summary>
/// Pre-computed hourly player count predictions per game.
/// 168 rows per game (7 days × 24 hours).
/// Used by GetSmartPredictionInsightsAsync for 8-hour forecasts.
/// </summary>
public class HourlyPlayerPrediction
{
    public required string Game { get; set; } // bf1942, fh2, bfvietnam
    public int DayOfWeek { get; set; } // 0=Sunday, 6=Saturday (SQLite convention)
    public int HourOfDay { get; set; } // 0-23
    public double PredictedPlayers { get; set; }
    public int DataPoints { get; set; }
    public Instant UpdatedAt { get; set; }
}
