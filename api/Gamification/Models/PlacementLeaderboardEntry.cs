namespace api.Gamification.Models;

public class PlacementLeaderboardEntry
{
    public int Rank { get; set; }
    public string PlayerName { get; set; } = "";
    public int FirstPlaces { get; set; }
    public int SecondPlaces { get; set; }
    public int ThirdPlaces { get; set; }
    public int TotalPlacements => FirstPlaces + SecondPlaces + ThirdPlaces;
    public int PlacementPoints => (FirstPlaces * 3) + (SecondPlaces * 2) + (ThirdPlaces * 1);
}
