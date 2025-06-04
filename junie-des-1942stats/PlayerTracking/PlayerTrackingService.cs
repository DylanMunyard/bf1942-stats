using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.Bflist;

namespace junie_des_1942stats.PlayerTracking;

public class PlayerTrackingService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(5);

    public PlayerTrackingService(PlayerTrackerDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    // Method to track players from Bf1942ServerInfo
    public async Task TrackPlayersFromServerInfo(Bf1942ServerInfo serverInfo, DateTime timestamp)
    {
        var adapter = new Bf1942ServerAdapter(serverInfo);
        await TrackPlayersFromServer(adapter, timestamp);
    }

    // Method to track players from Fh2ServerInfo
    public async Task TrackPlayersFromServerInfo(Fh2ServerInfo serverInfo, DateTime timestamp)
    {
        var adapter = new Fh2ServerAdapter(serverInfo);
        await TrackPlayersFromServer(adapter, timestamp);
    }

    // Core method that works with the common interface
    private async Task TrackPlayersFromServer(IGameServer server, DateTime timestamp)
    {
        if (!server.Players.Any())
            return;

        // Get or create the server record
        await GetOrCreateServerAsync(server);

        // Process each player (no session timeout logic here)
        foreach (var playerInfo in server.Players)
        {
            var player = await GetOrCreatePlayerAsync(playerInfo, timestamp);
            var activeSession = await _dbContext.PlayerSessions
                .FirstOrDefaultAsync(s => s.IsActive && 
                                        s.PlayerName == playerInfo.Name && 
                                        s.ServerGuid == server.Guid);

            // Handle session creation/updates (unchanged)
            if (activeSession == null || 
                (!string.IsNullOrEmpty(server.MapName) && activeSession.MapName != server.MapName))
            {
                await CreateNewSessionAsync(playerInfo, server, timestamp);
            }
            else
            {
                await UpdateExistingSessionAsync(player, activeSession, playerInfo, server, timestamp);
            }
        }
    }

    private async Task<GameServer> GetOrCreateServerAsync(IGameServer serverInfo)
    {
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Guid == serverInfo.Guid);

        if (server == null)
        {
            server = new GameServer
            {
                Guid = serverInfo.Guid,
                Name = serverInfo.Name,
                Ip = serverInfo.Ip,
                Port = serverInfo.Port,
                GameId = serverInfo.GameId
            };
            _dbContext.Servers.Add(server);
            await _dbContext.SaveChangesAsync();
        }
        else if (server.Name != serverInfo.Name || server.GameId != serverInfo.GameId)
        {
            server.Name = serverInfo.Name;
            server.GameId = serverInfo.GameId;
            await _dbContext.SaveChangesAsync();
        }

        return server;
    }

    private async Task<Player> GetOrCreatePlayerAsync(PlayerInfo playerInfo, DateTime timestamp)
    {
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerInfo.Name);

        if (player == null)
        {
            player = new Player
            {
                Name = playerInfo.Name,
                FirstSeen = timestamp,
                LastSeen = timestamp,
                AiBot = playerInfo.AiBot,
            };
            _dbContext.Players.Add(player);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
            player.AiBot = playerInfo.AiBot;
            player.LastSeen = timestamp;
            await _dbContext.SaveChangesAsync();
        }

        return player;
    }

    private async Task CreateNewSessionAsync(PlayerInfo playerInfo, IGameServer server,
        DateTime timestamp)
    {
        var session = new PlayerSession
        {
            PlayerName = playerInfo.Name,
            ServerGuid = server.Guid,
            StartTime = timestamp,
            LastSeenTime = timestamp,
            IsActive = true,
            ObservationCount = 1,
            TotalScore = playerInfo.Score,
            TotalKills = playerInfo.Kills,
            TotalDeaths = playerInfo.Deaths,
            MapName = server.MapName,
            GameType = server.GameType
        };

        _dbContext.PlayerSessions.Add(session);
        await _dbContext.SaveChangesAsync();

        var observation = new PlayerObservation
        {
            SessionId = session.SessionId,
            Timestamp = timestamp,
            Score = playerInfo.Score,
            Kills = playerInfo.Kills,
            Deaths = playerInfo.Deaths,
            Ping = playerInfo.Ping,
            TeamLabel = playerInfo.TeamLabel,
        };

        _dbContext.PlayerObservations.Add(observation);
        await _dbContext.SaveChangesAsync();
    }

    private async Task UpdateExistingSessionAsync(Player player, PlayerSession session, PlayerInfo playerInfo,
        IGameServer server, DateTime timestamp)
    {
        // Calculate the additional play time for this update
        int additionalMinutes = (int)(timestamp - session.LastSeenTime).TotalMinutes;
        
        // Update the session
        session.LastSeenTime = timestamp;
        session.ObservationCount++;
        session.TotalScore = Math.Max(session.TotalScore, playerInfo.Score); // Store highest score
        session.TotalKills = Math.Max(session.TotalKills, playerInfo.Kills); // Store highest kills
        session.TotalDeaths = playerInfo.Deaths; // Store latest deaths
        
        // Update map and game type info if needed
        if (!string.IsNullOrEmpty(server.MapName) && session.MapName != server.MapName)
        {
            session.MapName = server.MapName;
        }
        
        if (!string.IsNullOrEmpty(server.GameType) && session.GameType != server.GameType)
        {
            session.GameType = server.GameType;
        }

        _dbContext.PlayerSessions.Update(session);

        // Add an observation
        var observation = new PlayerObservation
        {
            SessionId = session.SessionId,
            Timestamp = timestamp,
            Score = playerInfo.Score,
            Kills = playerInfo.Kills,
            Deaths = playerInfo.Deaths,
            Ping = playerInfo.Ping
        };
        _dbContext.PlayerObservations.Add(observation);
    
        // Update player aggregate data
        player.TotalPlayTimeMinutes += additionalMinutes > 0 ? additionalMinutes : 0;
        
        _dbContext.Players.Update(player);

        await _dbContext.SaveChangesAsync();
    }

    // Add this public method to handle global session timeouts
    public async Task CloseAllTimedOutSessionsAsync(DateTime currentTime)
    {
        try
        {
            // Directly query and close all timed-out sessions in one batch
            var timeoutThreshold = currentTime - _sessionTimeout;
            var timedOutSessions = await _dbContext.PlayerSessions
                .Where(s => s.IsActive && s.LastSeenTime < timeoutThreshold)
                .ToListAsync();

            foreach (var session in timedOutSessions)
            {
                session.IsActive = false;
            }

            if (timedOutSessions.Any())
            {
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing timed-out sessions: {ex.Message}");
        }
    }
}