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
            await GetOrCreatePlayerAsync(playerInfo.Name, timestamp);

            // Find if player has an active session on this server
            var activeSession = activeSessions
                .FirstOrDefault(s => s.PlayerName == playerInfo.Name && s.ServerGuid == server.Guid);

            if (activeSession == null)
            {
                // Create a new session
                await CreateNewSessionAsync(playerInfo, server.Guid, timestamp);
            }
            else
            {
                // Update the existing session
                await UpdateExistingSessionAsync(activeSession, playerInfo, timestamp);
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
                Port = serverInfo.Port
            };
            _dbContext.Servers.Add(server);
            await _dbContext.SaveChangesAsync();
        }
        else if (server.Name != serverInfo.Name)
        {
            server.Name = serverInfo.Name;
            await _dbContext.SaveChangesAsync();
        }

        return server;
    }

    private async Task<Player> GetOrCreatePlayerAsync(string playerName, DateTime timestamp)
    {
        var player = await _dbContext.Players
            .FirstOrDefaultAsync(p => p.Name == playerName);

        if (player == null)
        {
            player = new Player
            {
                Name = playerName,
                FirstSeen = timestamp,
                LastSeen = timestamp
            };
            _dbContext.Players.Add(player);
            await _dbContext.SaveChangesAsync();
        }
        else
        {
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

    private async Task CreateNewSessionAsync(PlayerInfo playerInfo, string serverGuid, DateTime timestamp)
    {
        var session = new PlayerSession
        {
            PlayerName = playerInfo.Name,
            ServerGuid = serverGuid,
            StartTime = timestamp,
            LastSeenTime = timestamp,
            IsActive = true,
            ObservationCount = 1,
            TotalScore = playerInfo.Score,
            TotalKills = playerInfo.Kills,
            TotalDeaths = playerInfo.Deaths
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
            Ping = playerInfo.Ping
        };

        _dbContext.PlayerObservations.Add(observation);
        await _dbContext.SaveChangesAsync();
    }

    private async Task UpdateExistingSessionAsync(PlayerSession session, PlayerInfo playerInfo, DateTime timestamp)
    {
        // Update the session
        session.LastSeenTime = timestamp;
        session.ObservationCount++;
        session.TotalScore = Math.Max(session.TotalScore, playerInfo.Score); // Store highest score
        session.TotalKills = Math.Max(session.TotalKills, playerInfo.Kills); // Store highest kills
        session.TotalDeaths = playerInfo.Deaths; // Store latest deaths

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

        await _dbContext.SaveChangesAsync();
    }
}