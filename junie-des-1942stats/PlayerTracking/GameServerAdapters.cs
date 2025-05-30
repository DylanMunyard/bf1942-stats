using junie_des_1942stats.Bflist;

namespace junie_des_1942stats.PlayerTracking
{
    public interface IGameServer
    {
        string Guid { get; }
        string Ip { get; }
        int Port { get; }
        string Name { get; }
        IEnumerable<PlayerInfo> Players { get; }
    }

    public class Bf1942ServerAdapter(Bf1942ServerInfo serverInfo) : IGameServer
    {
        public string Guid => serverInfo.Guid;
        public string Ip => serverInfo.Ip;
        public int Port => serverInfo.Port;
        public string Name => serverInfo.Name;
        public IEnumerable<PlayerInfo> Players => serverInfo.Players;
    }

    public class Fh2ServerAdapter(Fh2ServerInfo serverInfo) : IGameServer
    {
        public string Guid => serverInfo.Guid;
        public string Ip => serverInfo.Ip;
        public int Port => serverInfo.Port;
        public string Name => serverInfo.Name;
        public IEnumerable<PlayerInfo> Players => serverInfo.Players;
    }
}