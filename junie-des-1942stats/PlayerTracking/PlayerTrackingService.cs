using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.Bflist;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
        await GetOrCreateServerAsync(server);
        
        if (!server.Players.Any())
            return;

        var playerNames = server.Players.Select(p => p.Name).ToList();
        
        // Get players from database and attach to context
        var existingPlayers = await _dbContext.Players
            .Where(p => playerNames.Contains(p.Name))
            .ToListAsync();

        var activeSessions = await GetActiveSessionsAsync(playerNames, server.Guid);

        var playerMap = existingPlayers.ToDictionary(p => p.Name);
        var sessionsByPlayer = activeSessions
            .GroupBy(s => s.PlayerName)
            .ToDictionary(g => g.Key, g => g.ToList());

        var newPlayers = new List<Player>();
        var sessionsToUpdate = new List<PlayerSession>();
        var sessionsToCreate = new List<PlayerSession>();
        var pendingObservations = new List<(PlayerInfo Info, PlayerSession Session)>();

        foreach (var playerInfo in server.Players)
        {
            if (!playerMap.TryGetValue(playerInfo.Name, out var player))
            {
                player = new Player
                {
                    Name = playerInfo.Name,
                    FirstSeen = timestamp,
                    LastSeen = timestamp,
                    AiBot = playerInfo.AiBot,
                };
                newPlayers.Add(player);
                playerMap.Add(player.Name, player);
            }
            else
            {
                // Update existing player
                player.AiBot = playerInfo.AiBot;
                player.LastSeen = timestamp;
                _dbContext.Players.Update(player);
            }

            // Handle sessions
            if (sessionsByPlayer.TryGetValue(playerInfo.Name, out var playerSessions))
            {
                var matchingSession = playerSessions.FirstOrDefault(s => 
                    !string.IsNullOrEmpty(server.MapName) && 
                    s.MapName == server.MapName);

                if (matchingSession != null)
                {
                    // Update existing session for current map
                    UpdateSessionData(matchingSession, playerInfo, server, timestamp);
                    sessionsToUpdate.Add(matchingSession);
                    pendingObservations.Add((playerInfo, matchingSession));
                    
                    // Update player playtime
                    player.TotalPlayTimeMinutes += CalculatePlayTime(matchingSession, timestamp);
                }
                else
                {
                    // Close all existing sessions (map changed)
                    foreach (var session in playerSessions)
                    {
                        session.IsActive = false;
                        sessionsToUpdate.Add(session);
                    }
                    
                    // Create new session for new map
                    var newSession = CreateNewSession(playerInfo, server, timestamp);
                    sessionsToCreate.Add(newSession);
                    pendingObservations.Add((playerInfo, newSession));
                }
            }
            else
            {
                // No existing sessions - create new one
                var newSession = CreateNewSession(playerInfo, server, timestamp);
                sessionsToCreate.Add(newSession);
                pendingObservations.Add((playerInfo, newSession));
            }
        }

        // Execute all database operations
        using (var transaction = await _dbContext.Database.BeginTransactionAsync())
        {
            try
            {
                // 1. Save new players first
                if (newPlayers.Any())
                {
                    await _dbContext.Players.AddRangeAsync(newPlayers);
                    await _dbContext.SaveChangesAsync();
                }

                // 2. Save sessions
                if (sessionsToCreate.Any())
                {
                    await _dbContext.PlayerSessions.AddRangeAsync(sessionsToCreate);
                    await _dbContext.SaveChangesAsync();
                }

                if (sessionsToUpdate.Any())
                {
                    _dbContext.PlayerSessions.UpdateRange(sessionsToUpdate);
                    await _dbContext.SaveChangesAsync();
                }

                // 3. Save observations
                var observations = pendingObservations.Select(x => 
                {
                    if (x.Session.SessionId == 0)
                        throw new InvalidOperationException("Session not saved before creating observation");

                    // Get team label from Teams array if TeamLabel is empty
                    var teamLabel = x.Info.TeamLabel;
                    if (string.IsNullOrEmpty(teamLabel) && server.Teams?.Any() == true)
                    {
                        var team = server.Teams.FirstOrDefault(t => t.Index == x.Info.Team);
                        teamLabel = team?.Label ?? "";
                    }

                    return new PlayerObservation
                    {
                        SessionId = x.Session.SessionId,
                        Timestamp = timestamp,
                        Score = x.Info.Score,
                        Kills = x.Info.Kills,
                        Deaths = x.Info.Deaths,
                        Ping = x.Info.Ping,
                        Team = x.Info.Team,
                        TeamLabel = teamLabel
                    };
                }).ToList();

                if (observations.Any())
                {
                    await _dbContext.PlayerObservations.AddRangeAsync(observations);
                    await _dbContext.SaveChangesAsync();
                }

                await transaction.CommitAsync();
                Console.WriteLine($"Successfully tracked {server.Players.Count()} players with {observations.Count} observations");
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

    private async Task<List<PlayerSession>> GetActiveSessionsAsync(
        IEnumerable<string> playerNames, string serverGuid)
    {
        return await _dbContext.PlayerSessions
            .Where(s => s.IsActive && 
                       playerNames.Contains(s.PlayerName) && 
                       s.ServerGuid == serverGuid)
            .OrderByDescending(s => s.LastSeenTime) // Most recent first
            .ToListAsync();
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
                Console.WriteLine($"Closed {timedOutSessions.Count} timed-out sessions");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error closing timed-out sessions: {ex.Message}");
        }
    }

    // Helper methods
    private PlayerSession CreateNewSession(PlayerInfo playerInfo, IGameServer server, DateTime timestamp)
    {
        return new PlayerSession
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
    }

    private void UpdateSessionData(PlayerSession session, PlayerInfo playerInfo, IGameServer server, DateTime timestamp)
    {
        int additionalMinutes = (int)(timestamp - session.LastSeenTime).TotalMinutes;
        session.LastSeenTime = timestamp;
        session.ObservationCount++;
        session.TotalScore = Math.Max(session.TotalScore, playerInfo.Score);
        session.TotalKills = Math.Max(session.TotalKills, playerInfo.Kills);
        session.TotalDeaths = playerInfo.Deaths;
        
        if (!string.IsNullOrEmpty(server.MapName))
            session.MapName = server.MapName;
        
        if (!string.IsNullOrEmpty(server.GameType))
            session.GameType = server.GameType;
    }

    private int CalculatePlayTime(PlayerSession session, DateTime timestamp)
    {
        return Math.Max(0, (int)(timestamp - session.LastSeenTime).TotalMinutes);
    }
}