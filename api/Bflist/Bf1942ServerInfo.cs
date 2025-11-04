namespace junie_des_1942stats.Bflist;

public class Bf1942ServerInfo
{
    public string Guid { get; set; } = "";
    public string Ip { get; set; } = "";
    public int Port { get; set; }
    public int QueryPort { get; set; }
    public string Name { get; set; } = "";
    public int NumPlayers { get; set; }
    public int MaxPlayers { get; set; }
    public string MapName { get; set; } = "";
    public bool Password { get; set; }
    public string GameType { get; set; } = "";
    public string GameVersion { get; set; } = "";
    public string GameMode { get; set; } = "";
    public int AverageFps { get; set; }
    public bool ContentCheck { get; set; }
    public int Dedicated { get; set; }
    public string GameId { get; set; } = "";
    public string MapId { get; set; } = "";
    public int ReservedSlots { get; set; }
    public int RoundTime { get; set; }
    public int RoundTimeRemain { get; set; }
    public int Status { get; set; }
    public bool Anticheat { get; set; }
    public int Tickets1 { get; set; }
    public int Tickets2 { get; set; }
    public string UnpureMods { get; set; } = "";
    public string JoinLink { get; set; } = "";
    public string JoinLinkWeb { get; set; } = "";
    public TeamInfo[] Teams { get; set; } = [];
    public PlayerInfo[] Players { get; set; } = [];
}

public class Bf1942ServersResponse
{
    public Bf1942ServerInfo[] Servers { get; set; } = [];
    public string Cursor { get; set; } = "";
    public bool HasMore { get; set; }
}