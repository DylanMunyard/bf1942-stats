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

        // Get currently active sessions for all players
        var activeSessions = await _dbContext.PlayerSessions
            .Where(s => s.IsActive)
            .ToListAsync();

        // Mark active sessions that have timed out
        await CloseTimedOutSessionsAsync(activeSessions, timestamp);

        // Process each player
        foreach (var playerInfo in server.Players)
        {
            // Get or create the player record
            var player = await GetOrCreatePlayerAsync(playerInfo, timestamp);

            // Find if player has an active session on this server
            var activeSession = activeSessions
                .FirstOrDefault(s => s.PlayerName == playerInfo.Name && s.ServerGuid == server.Guid);

            // Check if we need to create a new session
            bool createNewSession = activeSession == null;
            
            // Check if map has changed
            if (activeSession != null && 
                !string.IsNullOrEmpty(server.MapName) &&
                activeSession.MapName != server.MapName)
            {
                // Close the current session if map has changed
                activeSession.IsActive = false;
                _dbContext.PlayerSessions.Update(activeSession);
                await _dbContext.SaveChangesAsync();
                
                // Flag to create a new session
                createNewSession = true;
            }

            if (createNewSession || activeSession is null)
            {
                // Create a new session
                await CreateNewSessionAsync(playerInfo, server, timestamp);
            }
            else
            {
                // Update the existing session
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

    private async Task CloseTimedOutSessionsAsync(List<PlayerSession> activeSessions, DateTime currentTime)
    {
        var timeoutSessions = activeSessions
            .Where(s => currentTime - s.LastSeenTime > _sessionTimeout)
            .ToList();

        foreach (var session in timeoutSessions)
        {
            session.IsActive = false;
            _dbContext.PlayerSessions.Update(session);
        }

        if (timeoutSessions.Any())
            await _dbContext.SaveChangesAsync();
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
}