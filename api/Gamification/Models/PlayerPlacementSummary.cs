namespace api.Gamification.Models;

public class PlayerPlacementSummary
{
    public string PlayerName { get; set; } = "";
    public string? ServerGuid { get; set; }
    public string? MapName { get; set; }
    public int FirstPlaces { get; set; }
    public int SecondPlaces { get; set; }
    public int ThirdPlaces { get; set; }
    public int TotalPlacements => FirstPlaces + SecondPlaces + ThirdPlaces;
    public int PlacementPoints => (FirstPlaces * 3) + (SecondPlaces * 2) + (ThirdPlaces * 1);
    public string? BestTeamLabel { get; set; }
}
