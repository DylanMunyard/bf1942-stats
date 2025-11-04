namespace api.Bflist;

public class PlayerInfo
{
    public string Name { get; set; } = "";
    public int Score { get; set; }
    public int Kills { get; set; }
    public int Deaths { get; set; }
    public int Ping { get; set; }
    public int Team { get; set; }
    public string TeamLabel { get; set; } = "";
    public bool AiBot { get; set; } /* only set by FH2 API, will default to false otherwise */
}