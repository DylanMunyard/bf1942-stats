using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.Notifications.Services;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Net.Http;
using System.Text.Json;
using System.Threading;

namespace junie_des_1942stats.PlayerTracking;

public class PlayerTrackingService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly IPlayerEventPublisher? _eventPublisher;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim IpInfoSemaphore = new SemaphoreSlim(10); // max 10 concurrent
    private static DateTime _lastIpInfoRequest = DateTime.MinValue;
    private static readonly object IpInfoLock = new object();

    public PlayerTrackingService(PlayerTrackerDbContext dbContext, IPlayerEventPublisher? eventPublisher = null)
    {
        _dbContext = dbContext;
        _eventPublisher = eventPublisher;
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

    // Method to track players from BfvServerInfo
    public async Task TrackPlayersFromServerInfo(BfvietnamServerInfo serverInfo, DateTime timestamp)
    {
        // Create an adapter to treat BfvServerInfo as a generic IGameServer
        var adapter = new BfvietnamServerAdapter(serverInfo);
        await TrackPlayersFromServer(adapter, timestamp);
    }
    
    public async Task TrackPlayersFromServerInfo(IGameServer server, DateTime timestamp)
    {
        await TrackPlayersFromServer(server, timestamp);
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
        
        // Track events to publish after successful database operations
        var eventsToPublish = new List<(string EventType, PlayerInfo PlayerInfo, PlayerSession Session, string? OldMapName)>();

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
                    var oldMapName = playerSessions.FirstOrDefault()?.MapName ?? "";
                    foreach (var session in playerSessions)
                    {
                        session.IsActive = false;
                        sessionsToUpdate.Add(session);
                    }
                    
                    // Create new session for new map
                    var newSession = CreateNewSession(playerInfo, server, timestamp);
                    sessionsToCreate.Add(newSession);
                    pendingObservations.Add((playerInfo, newSession));
                    
                    // Track map change event (only if player had active sessions before)
                    eventsToPublish.Add(("map_change", playerInfo, newSession, oldMapName));
                    Console.WriteLine($"Queued map_change event for player {playerInfo.Name} (Old Map: {oldMapName}, New Map: {server.MapName})");
                }
            }
            else
            {
                // No existing sessions - create new one (player coming online)
                var newSession = CreateNewSession(playerInfo, server, timestamp);
                sessionsToCreate.Add(newSession);
                pendingObservations.Add((playerInfo, newSession));
                
                // Track player online event (true first time online)
                eventsToPublish.Add(("player_online", playerInfo, newSession, null));
                Console.WriteLine($"Queued player_online event for player {playerInfo.Name} on server {server.Name}");
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
                
                // Publish events after successful database operations
                await PublishPlayerEvents(eventsToPublish, server);
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

        bool ipChanged = false;
        if (server == null)
        {
            server = new GameServer
            {
                Guid = serverInfo.Guid,
                Name = serverInfo.Name,
                Ip = serverInfo.Ip,
                Port = serverInfo.Port,
                GameId = serverInfo.GameId,
                MaxPlayers = serverInfo.MaxPlayers,
                MapName = serverInfo.MapName,
                JoinLink = serverInfo.JoinLink
            };
            _dbContext.Servers.Add(server);
            ipChanged = true;
        }
        else
        {
            if (server.Ip != serverInfo.Ip)
            {
                server.Ip = serverInfo.Ip;
                ipChanged = true;
            }
            if (server.Name != serverInfo.Name || server.GameId != serverInfo.GameId)
            {
                server.Name = serverInfo.Name;
                server.GameId = serverInfo.GameId;
            }
            
            // Update server info fields
            server.MaxPlayers = serverInfo.MaxPlayers;
            server.MapName = serverInfo.MapName;
            server.JoinLink = serverInfo.JoinLink;
        }

        // Geo lookup if IP changed or no geolocation stored
        if (ipChanged || server.GeoLookupDate == null)
        {
            var geo = await LookupGeoLocationAsync(server.Ip);
            if (geo != null)
            {
                server.Country = geo.Country;
                server.Region = geo.Region;
                server.City = geo.City;
                server.Loc = geo.Loc;
                server.Timezone = geo.Timezone;
                server.Org = geo.Org;
                server.Postal = geo.Postal;
                server.GeoLookupDate = DateTime.UtcNow;
            }
        }

        await _dbContext.SaveChangesAsync();
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

    private class IpInfoGeoResult
    {
        public string? Country { get; set; }
        public string? Region { get; set; }
        public string? City { get; set; }
        public string? Loc { get; set; }
        public string? Timezone { get; set; }
        public string? Org { get; set; }
        public string? Postal { get; set; }
    }

    private static async Task<IpInfoGeoResult?> LookupGeoLocationAsync(string ip)
    {
        if (string.IsNullOrWhiteSpace(ip)) return null;
        await IpInfoSemaphore.WaitAsync();
        try
        {
            // Ensure at least 200ms between requests
            lock (IpInfoLock)
            {
                var now = DateTime.UtcNow;
                var sinceLast = (now - _lastIpInfoRequest).TotalMilliseconds;
                if (sinceLast < 200)
                {
                    Thread.Sleep(200 - (int)sinceLast);
                }
                _lastIpInfoRequest = DateTime.UtcNow;
            }
            using var httpClient = new HttpClient();
            var url = $"https://ipinfo.io/{ip}/json";
            var response = await httpClient.GetStringAsync(url);
            using var doc = JsonDocument.Parse(response);
            var root = doc.RootElement;
            return new IpInfoGeoResult
            {
                Country = root.TryGetProperty("country", out var c) ? c.GetString() : null,
                Region = root.TryGetProperty("region", out var r) ? r.GetString() : null,
                City = root.TryGetProperty("city", out var ci) ? ci.GetString() : null,
                Loc = root.TryGetProperty("loc", out var l) ? l.GetString() : null,
                Timezone = root.TryGetProperty("timezone", out var t) ? t.GetString() : null,
                Org = root.TryGetProperty("org", out var o) ? o.GetString() : null,
                Postal = root.TryGetProperty("postal", out var p) ? p.GetString() : null
            };
        }
        catch
        {
            return null;
        }
        finally
        {
            IpInfoSemaphore.Release();
        }
    }

    private async Task PublishPlayerEvents(List<(string EventType, PlayerInfo PlayerInfo, PlayerSession Session, string? OldMapName)> eventsToPublish, IGameServer server)
    {
        if (_eventPublisher == null)
        {
            Console.WriteLine("No event publisher configured - skipping event publishing");
            return;
        }

        if (!eventsToPublish.Any())
        {
            Console.WriteLine("No events to publish");
            return;
        }

        var eventCounts = eventsToPublish
            .GroupBy(e => e.EventType)
            .ToDictionary(g => g.Key, g => g.Count());

        Console.WriteLine($"Publishing {eventsToPublish.Count} events: {string.Join(", ", eventCounts.Select(kvp => $"{kvp.Key}: {kvp.Value}"))}");

        foreach (var eventData in eventsToPublish)
        {
            try
            {
                Console.WriteLine($"Publishing {eventData.EventType} event for player {eventData.PlayerInfo.Name} on server {server.Name} (ID: {server.Guid})");
                
                switch (eventData.EventType)
                {
                    case "player_online":
                        await _eventPublisher.PublishPlayerOnlineEvent(
                            eventData.PlayerInfo.Name,
                            server.Guid,
                            server.Name,
                            server.MapName ?? "",
                            server.GameId ?? "",
                            eventData.Session.SessionId);
                        Console.WriteLine($"Successfully published player_online event for {eventData.PlayerInfo.Name} (Session: {eventData.Session.SessionId})");
                        break;

                    case "map_change":
                        await _eventPublisher.PublishMapChangeEvent(
                            eventData.PlayerInfo.Name,
                            server.Guid,
                            server.Name,
                            eventData.OldMapName ?? "",
                            server.MapName ?? "",
                            eventData.Session.SessionId);
                        Console.WriteLine($"Successfully published map_change event for {eventData.PlayerInfo.Name} (Session: {eventData.Session.SessionId}, Old Map: {eventData.OldMapName}, New Map: {server.MapName})");
                        break;
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error publishing {eventData.EventType} event for {eventData.PlayerInfo.Name}: {ex.Message}");
                Console.WriteLine($"Exception details: {ex}");
            }
        }
    }
}