﻿using junie_des_1942stats.Bflist;

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
        int? MaxPlayers { get; }
        string? JoinLink { get; }
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
        public int? MaxPlayers => serverInfo.MaxPlayers;
        public string? JoinLink => serverInfo.JoinLink;

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
        public int? MaxPlayers => serverInfo.MaxPlayers;
        public string? JoinLink => null; // FH2 doesn't have JoinLink field
        public IEnumerable<PlayerInfo> Players => serverInfo.Players;
        public IEnumerable<TeamInfo> Teams => serverInfo.Teams;
    }

    public class BfvietnamServerAdapter(BfvietnamServerInfo serverInfo) : IGameServer
    {
        private readonly BfvietnamServerInfo _serverInfo = serverInfo;
        public string Guid => _serverInfo.Guid;
        public string Name => _serverInfo.Name;
        public string Ip => _serverInfo.Ip;
        public int Port => _serverInfo.Port;
        public string GameId => "bfvietnam";
        public string GameType => _serverInfo.GameType;
        public string MapName => _serverInfo.MapName;
        public int? MaxPlayers => _serverInfo.MaxPlayers;
        public string? JoinLink => _serverInfo.JoinLink;
        public IEnumerable<PlayerInfo> Players => _serverInfo.Players;
        public IEnumerable<TeamInfo> Teams => _serverInfo.Teams;
    }
}