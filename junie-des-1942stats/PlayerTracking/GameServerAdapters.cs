using junie_des_1942stats.Bflist;

namespace junie_des_1942stats.PlayerTracking
{
    public interface IGameServer
    {
        string Guid { get; }
        string Ip { get; }
        int Port { get; }
        string Name { get; }
        string GameId { get; }
        string MapName { get; }
        string GameType { get; }
        IEnumerable<PlayerInfo> Players { get; }
        IEnumerable<TeamInfo> Teams { get; }
    }

    public class Bf1942ServerAdapter(Bf1942ServerInfo serverInfo) : IGameServer
    {
        public string Guid => serverInfo.Guid;
        public string Ip => serverInfo.Ip;
        public int Port => serverInfo.Port;
        public string Name => serverInfo.Name;
        public string GameId => serverInfo.GameId;
        public string MapName => serverInfo.MapName;
        public string GameType => serverInfo.GameType;
        
        public IEnumerable<PlayerInfo> Players => serverInfo.Players;
        public IEnumerable<TeamInfo> Teams => serverInfo.Teams;
    }

    public class Fh2ServerAdapter(Fh2ServerInfo serverInfo) : IGameServer
    {
        public string Guid => serverInfo.Guid;
        public string Ip => serverInfo.Ip;
        public int Port => serverInfo.Port;
        public string Name => serverInfo.Name;
        public string GameId => "fh2";
        public string MapName => serverInfo.MapName;
        public string GameType => serverInfo.GameType;
        public IEnumerable<PlayerInfo> Players => serverInfo.Players;
        public IEnumerable<TeamInfo> Teams => serverInfo.Teams;
    }
}