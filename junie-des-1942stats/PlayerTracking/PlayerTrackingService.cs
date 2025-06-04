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

        var playerNames = server.Players.Select(p => p.Name).ToList();
        
        // Batch fetch all players and active sessions
        var existingPlayers = await GetPlayersByNameAsync(playerNames);
        var existingSessions = await GetActiveSessionsAsync(playerNames, server.Guid);

        var newPlayers = new List<Player>();
        var newSessions = new List<PlayerSession>();
        var updatedSessions = new List<PlayerSession>();
        var updatedPlayers = new List<Player>();
        var pendingObservations = new List<(PlayerInfo, PlayerSession)>();

        foreach (var playerInfo in server.Players)
        {
            // Get or create player
            if (!existingPlayers.TryGetValue(playerInfo.Name, out var player))
            {
                player = new Player
                {
                    Name = playerInfo.Name,
                    FirstSeen = timestamp,
                    LastSeen = timestamp,
                    AiBot = playerInfo.AiBot,
                };
                newPlayers.Add(player);
                existingPlayers[player.Name] = player;
            }
            else
            {
                player.AiBot = playerInfo.AiBot;
                player.LastSeen = timestamp;
                updatedPlayers.Add(player);
            }

            // Handle session
            var sessionKey = (playerInfo.Name, server.Guid);
            if (!existingSessions.TryGetValue(sessionKey, out var session) || 
                (!string.IsNullOrEmpty(server.MapName) && session.MapName != server.MapName))
            {
                // Create new session
                session = new PlayerSession
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
                newSessions.Add(session);
                pendingObservations.Add((playerInfo, session));
            }
            else
            {
                // Update existing session
                int additionalMinutes = (int)(timestamp - session.LastSeenTime).TotalMinutes;
                session.LastSeenTime = timestamp;
                session.ObservationCount++;
                session.TotalScore = Math.Max(session.TotalScore, playerInfo.Score);
                session.TotalKills = Math.Max(session.TotalKills, playerInfo.Kills);
                session.TotalDeaths = playerInfo.Deaths;
                
                if (!string.IsNullOrEmpty(server.MapName) && session.MapName != server.MapName)
                    session.MapName = server.MapName;
                
                if (!string.IsNullOrEmpty(server.GameType) && session.GameType != server.GameType)
                    session.GameType = server.GameType;

                updatedSessions.Add(session);
                
                // Add observation
                _dbContext.PlayerObservations.Add(new PlayerObservation
                {
                    SessionId = session.SessionId,
                    Timestamp = timestamp,
                    Score = playerInfo.Score,
                    Kills = playerInfo.Kills,
                    Deaths = playerInfo.Deaths,
                    Ping = playerInfo.Ping
                });

                // Update player playtime
                player.TotalPlayTimeMinutes += additionalMinutes > 0 ? additionalMinutes : 0;
            }
        }

        // Execute all database operations in a single transaction
        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // Save new players first
                if (newPlayers.Any())
                    await _dbContext.Players.AddRangeAsync(newPlayers);
                
                // Save updated players
                if (updatedPlayers.Any())
                    _dbContext.Players.UpdateRange(updatedPlayers);
                
                // Save all changes to get player IDs
                await _dbContext.SaveChangesAsync();

                // Save new sessions
                if (newSessions.Any())
                    await _dbContext.PlayerSessions.AddRangeAsync(newSessions);
                
                // Save updated sessions
                if (updatedSessions.Any())
                    _dbContext.PlayerSessions.UpdateRange(updatedSessions);
                
                // Save all changes to get session IDs
                await _dbContext.SaveChangesAsync();

                // Now create observations for new sessions
                var observations = pendingObservations.Select(x => new PlayerObservation
                {
                    SessionId = x.Item2.SessionId,
                    Timestamp = timestamp,
                    Score = x.Item1.Score,
                    Kills = x.Item1.Kills,
                    Deaths = x.Item1.Deaths,
                    Ping = x.Item1.Ping,
                    TeamLabel = x.Item1.TeamLabel,
                });

                await _dbContext.PlayerObservations.AddRangeAsync(observations);
                
                await _dbContext.SaveChangesAsync();
                await transaction.CommitAsync();
            }
            catch (Exception ex)
            {
                await transaction.RollbackAsync();
                Console.WriteLine($"Error tracking players: {ex.Message}");
                throw;
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

    private async Task<Dictionary<string, Player>> GetPlayersByNameAsync(IEnumerable<string> playerNames)
    {
        return await _dbContext.Players
            .Where(p => playerNames.Contains(p.Name))
            .ToDictionaryAsync(p => p.Name);
    }

    private async Task<Dictionary<(string, string), PlayerSession>> GetActiveSessionsAsync(
        IEnumerable<string> playerNames, string serverGuid)
    {
        return await _dbContext.PlayerSessions
            .Where(s => s.IsActive && 
                       playerNames.Contains(s.PlayerName) && 
                       s.ServerGuid == serverGuid)
            .ToDictionaryAsync(s => (s.PlayerName, s.ServerGuid));
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