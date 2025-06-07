using junie_des_1942stats.Bflist;


public class Fh2ServerInfo
{
    public string Guid { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public int QueryPort { get; set; }
    public string Name { get; set; } = "";
    public int NumPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public bool Password { get; set; }
    public string GameType { get; set; } = "";
    public string MapName { get; set; } = "";
    public int MapSize { get; set; }
    public string GameVersion { get; set; } = "";
    public string GameVariant { get; set; } = "";
    public int Timelimit { get; set; }
    public int RoundsPerMap { get; set; }
    public bool Ranked { get; set; }
    public bool Anticheat { get; set; }
    public bool Battlerecorder { get; set; }
    public string DemoIndex { get; set; } = "";
    public string DemoDownload { get; set; } = "";
    public bool Voip { get; set; }
    public bool Autobalance { get; set; }
    public bool Friendlyfire { get; set; }
    public string Tkmode { get; set; } = "";
    public int Startdelay { get; set; }
    public int Spawntime { get; set; }
    public string SponsorText { get; set; } = "";
    public string SponsorLogoUrl { get; set; } = "";
    public string CommunityLogoUrl { get; set; } = "";
    public int Scorelimit { get; set; }
    public int Ticketratio { get; set; }
    public int Teamratio { get; set; }
    public string Team1 { get; set; } = "";
    public string Team2 { get; set; } = "";
    public bool Pure { get; set; }
    public bool GlobalUnlocks { get; set; }
    public int ReservedSlots { get; set; }
    public bool Dedicated { get; set; }
    public string Os { get; set; } = "";
    public bool Bots { get; set; }
    public int Fps { get; set; }
    public bool Plasma { get; set; }
    public int CoopBotRatio { get; set; }
    public int CoopBotCount { get; set; }
    public int CoopBotDiff { get; set; }
    public bool NoVehicles { get; set; }
    public List<TeamInfo> Teams { get; set; } = [];
    public List<PlayerInfo> Players { get; set; } = [];
}

public class Fh2ServersResponse
{
    public Fh2ServerInfo[] Servers { get; set; } = [];
    public string Cursor { get; set; } = "";
    public bool HasMore { get; set; }
}