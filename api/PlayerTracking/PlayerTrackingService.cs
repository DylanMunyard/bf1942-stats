using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using api.Bflist.Models;
using api.Services;
using api.ServerStats;
using System.Text.Json;

namespace api.PlayerTracking;

public class PlayerTrackingService
{
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly IPlayerEventPublisher? _eventPublisher;
    private readonly ILogger<PlayerTrackingService> _logger;
    private readonly IBotDetectionService _botDetectionService;
    private readonly TimeSpan _sessionTimeout = TimeSpan.FromMinutes(5);
    private static readonly SemaphoreSlim IpInfoSemaphore = new SemaphoreSlim(10); // max 10 concurrent
    private static DateTime _lastIpInfoRequest = DateTime.MinValue;
    private static readonly object IpInfoLock = new object();

    public PlayerTrackingService(PlayerTrackerDbContext dbContext, IBotDetectionService botDetectionService, IPlayerEventPublisher? eventPublisher = null, ILogger<PlayerTrackingService>? logger = null)
    {
        _dbContext = dbContext;
        _botDetectionService = botDetectionService;
        _eventPublisher = eventPublisher;
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<PlayerTrackingService>.Instance;
    }

    public async Task TrackPlayersFromServerInfo(IGameServer server, DateTime timestamp, string game)
    {
        await TrackPlayersFromServer(server, timestamp, game);
    }

    // Core method that works with the common interface
    private async Task TrackPlayersFromServer(IGameServer server, DateTime timestamp, string game)
    {
        var (gameServer, serverMapChangeOldMap) = await GetOrCreateServerAsync(server, game);

        // Publish server map change event if detected
        if (!string.IsNullOrEmpty(serverMapChangeOldMap))
        {
            _logger.LogInformation("TRACKING: Detected map change for {ServerGuid} / {ServerName}: {OldMap} -> {NewMap}",
                server.Guid, server.Name, serverMapChangeOldMap, server.MapName);
            try
            {
                await PublishServerMapChangeEvent(server, serverMapChangeOldMap);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing server map change event for {ServerName}: {OldMap} -> {NewMap}. Continuing with stats collection.",
                    server.Name, serverMapChangeOldMap, server.MapName);
            }
        }

        // Ensure active round and record round observation regardless of player count (if enabled)
        var activeRound = await EnsureActiveRoundAsync(server, timestamp, serverMapChangeOldMap);
        await RecordRoundObservationAsync(activeRound, server, timestamp);

        // If no players, we skip session handling but still tracked round + observation
        if (!server.Players.Any())
        {
            return;
        }

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
            // Skip bot players entirely - don't store them in database at all
            if (_botDetectionService.IsBotPlayer(playerInfo.Name, playerInfo.AiBot))
            {
                continue;
            }

            if (!playerMap.TryGetValue(playerInfo.Name, out var player))
            {
                player = new Player
                {
                    Name = playerInfo.Name,
                    FirstSeen = timestamp,
                    LastSeen = timestamp,
                    AiBot = false, // Since we skip bots above, this is always false
                };
                newPlayers.Add(player);
                playerMap.Add(player.Name, player);
            }
            else
            {
                // Update existing player
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
                    // Calculate playtime BEFORE updating session data
                    var additionalPlayTime = CalculatePlayTime(matchingSession, timestamp);

                    // Update existing session for current map
                    UpdateSessionData(matchingSession, playerInfo, server, timestamp);
                    // Ensure the session is linked to the active round
                    if (activeRound != null)
                    {
                        matchingSession.RoundId = activeRound.RoundId;
                    }
                    sessionsToUpdate.Add(matchingSession);
                    pendingObservations.Add((playerInfo, matchingSession));

                    // Update player playtime
                    player.TotalPlayTimeMinutes += additionalPlayTime;
                }
                else
                {
                    // Close all existing sessions (map changed)
                    await CalculateAndSetAveragePingAsync(playerSessions);
                    foreach (var session in playerSessions)
                    {
                        session.IsActive = false;
                        sessionsToUpdate.Add(session);
                    }

                    // Create new session for new map
                    var newSession = CreateNewSession(playerInfo, server, timestamp, activeRound?.RoundId);
                    sessionsToCreate.Add(newSession);
                    pendingObservations.Add((playerInfo, newSession));
                }
            }
            else
            {
                // No existing sessions - create new one (player coming online)
                var newSession = CreateNewSession(playerInfo, server, timestamp, activeRound?.RoundId);
                sessionsToCreate.Add(newSession);
                pendingObservations.Add((playerInfo, newSession));

                // Track player online event (bots are already filtered out above)
                _logger.LogInformation("TRACKING: Detected player online for {PlayerName} on {ServerName}: ",
                    server.Name, playerInfo.Name);
                eventsToPublish.Add(("player_online", playerInfo, newSession, null));
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

                // Update participant count for the round (all players since bots are no longer stored)
                if (activeRound != null)
                {
                    var playerCount = await _dbContext.PlayerSessions
                        .Where(ps => ps.RoundId == activeRound.RoundId)
                        .Select(ps => ps.PlayerName)
                        .Distinct()
                        .CountAsync();

                    activeRound.ParticipantCount = playerCount;
                    activeRound.Tickets1 = server.Tickets1;
                    activeRound.Tickets2 = server.Tickets2;
                    _dbContext.Rounds.Update(activeRound);
                    await _dbContext.SaveChangesAsync();
                }

                await transaction.CommitAsync();

                // Publish events after successful database operations
                try
                {
                    await PublishPlayerEvents(eventsToPublish, server);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Error publishing player events for {ServerName}. Stats collection completed successfully.", server.Name);
                }
            }
            catch (Exception)
            {
                await transaction.RollbackAsync();
                throw;
            }
        }
    }

    private async Task<(GameServer server, string? oldMapName)> GetOrCreateServerAsync(IGameServer serverInfo, string game)
    {
        var server = await _dbContext.Servers
            .FirstOrDefaultAsync(s => s.Guid == serverInfo.Guid);

        bool ipChanged = false;
        string? oldMapName = null;

        if (server == null)
        {
            server = new GameServer
            {
                Guid = serverInfo.Guid,
                Name = serverInfo.Name,
                Ip = serverInfo.Ip,
                Port = serverInfo.Port,
                GameId = serverInfo.GameId,
                Game = game,
                MaxPlayers = serverInfo.MaxPlayers,
                MapName = serverInfo.MapName,
                JoinLink = serverInfo.JoinLink,
                CurrentMap = serverInfo.MapName
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

            // Update game if provided and different
            if (!string.IsNullOrEmpty(game) && server.Game != game)
            {
                server.Game = game;
            }

            // Check for map change before updating
            if (server.MapName != serverInfo.MapName && !string.IsNullOrEmpty(server.MapName))
            {
                oldMapName = server.MapName;
            }

            // Update server info fields
            server.MaxPlayers = serverInfo.MaxPlayers;
            server.MapName = serverInfo.MapName;
            server.JoinLink = serverInfo.JoinLink;

            // Update current map from active sessions or server info
            server.CurrentMap = serverInfo.MapName;
        }

        // Always update online status and last seen time when server is polled
        server.IsOnline = true;
        server.LastSeenTime = DateTime.UtcNow;

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
        return (server, oldMapName);
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

            await CalculateAndSetAveragePingAsync(timedOutSessions);
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
            _logger.LogError(ex, "Error closing timed out player sessions");
        }
    }

    public async Task MarkOfflineServersAsync(DateTime currentTime)
    {
        try
        {
            var offlineThreshold = currentTime.AddMinutes(-5); // Mark servers offline if not seen for 5 minutes

            var serversToMarkOffline = await _dbContext.Servers
                .Where(s => s.IsOnline && s.LastSeenTime < offlineThreshold)
                .ToListAsync();

            foreach (var server in serversToMarkOffline)
            {
                server.IsOnline = false;
            }

            if (serversToMarkOffline.Any())
            {
                Console.WriteLine($"[{DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}] Marked {serversToMarkOffline.Count} servers as offline");
                await _dbContext.SaveChangesAsync();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error marking servers as offline");
        }
    }

    // Helper methods
    private PlayerSession CreateNewSession(PlayerInfo playerInfo, IGameServer server, DateTime timestamp, string? roundId)
    {
        // Get team label from Teams array if TeamLabel is empty
        var teamLabel = playerInfo.TeamLabel;
        if (string.IsNullOrEmpty(teamLabel) && server.Teams?.Any() == true)
        {
            var team = server.Teams.FirstOrDefault(t => t.Index == playerInfo.Team);
            teamLabel = team?.Label ?? "";
        }

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
            GameType = server.GameType,
            RoundId = roundId,
            // Denormalized current state fields for performance
            CurrentPing = playerInfo.Ping,
            CurrentTeam = playerInfo.Team,
            CurrentTeamLabel = teamLabel
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

        // Update denormalized current state fields for live server performance
        session.CurrentPing = playerInfo.Ping;
        session.CurrentTeam = playerInfo.Team;

        // Get team label from Teams array if TeamLabel is empty
        var teamLabel = playerInfo.TeamLabel;
        if (string.IsNullOrEmpty(teamLabel) && server.Teams?.Any() == true)
        {
            var team = server.Teams.FirstOrDefault(t => t.Index == playerInfo.Team);
            teamLabel = team?.Label ?? "";
        }
        session.CurrentTeamLabel = teamLabel;
    }

    private async Task CalculateAndSetAveragePingAsync(IEnumerable<PlayerSession> sessions)
    {
        var sessionsList = sessions.ToList();
        if (!sessionsList.Any()) return;

        try
        {
            var sessionIds = sessionsList.Select(s => s.SessionId).ToList();

            // Calculate average ping for all sessions in one query
            var avgPings = await _dbContext.PlayerObservations
                .Where(o => sessionIds.Contains(o.SessionId) && o.Ping > 0)
                .GroupBy(o => o.SessionId)
                .Select(g => new { SessionId = g.Key, AvgPing = g.Average(o => (double)o.Ping) })
                .ToListAsync();

            var avgPingLookup = avgPings.ToDictionary(x => x.SessionId, x => x.AvgPing);

            // Update all sessions with their calculated average ping
            foreach (var session in sessionsList)
            {
                session.AveragePing = avgPingLookup.TryGetValue(session.SessionId, out var avgPing) && avgPing > 0
                    ? avgPing
                    : null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to calculate average ping for {SessionCount} sessions", sessionsList.Count);
            // Set all sessions to null on error
            foreach (var session in sessionsList)
            {
                session.AveragePing = null;
            }
        }
    }

    private static string ComputeRoundId(string serverGuid, string mapName, DateTime startTimeUtc)
    {
        // Normalize to second precision for stability
        var normalized = new DateTime(startTimeUtc.Ticks - (startTimeUtc.Ticks % TimeSpan.TicksPerSecond), DateTimeKind.Utc);
        var payload = $"{serverGuid}|{mapName}|{normalized:yyyy-MM-ddTHH:mm:ssZ}";
        using var sha = SHA256.Create();
        var hash = sha.ComputeHash(Encoding.UTF8.GetBytes(payload));
        var hex = Convert.ToHexString(hash);
        return hex[..20].ToLowerInvariant();
    }

    private async Task<Round?> EnsureActiveRoundAsync(IGameServer server, DateTime timestamp, string? oldMapName)
    {
        // Skip round tracking if map name is empty
        if (string.IsNullOrWhiteSpace(server.MapName)) return null;

        var active = await _dbContext.Rounds
            .Where(r => r.ServerGuid == server.Guid && r.IsActive)
            .OrderByDescending(r => r.StartTime)
            .FirstOrDefaultAsync();

        // Detect map change via server update or explicit oldMapName
        var mapChanged = !string.IsNullOrEmpty(oldMapName) || (active != null && !string.Equals(active.MapName, server.MapName, StringComparison.Ordinal));

        if (active != null && mapChanged)
        {
            active.IsActive = false;
            active.EndTime = timestamp;
            active.DurationMinutes = (int)Math.Max(0, (active.EndTime.Value - active.StartTime).TotalMinutes);
            _dbContext.Rounds.Update(active);
            await _dbContext.SaveChangesAsync();
            active = null;
        }

        if (active == null)
        {
            var (team1Label, team2Label) = GetTeamLabels(server);
            var newRound = new Round
            {
                ServerGuid = server.Guid,
                ServerName = server.Name,
                MapName = server.MapName,
                GameType = server.GameType ?? "",
                StartTime = timestamp,
                IsActive = true,
                Tickets1 = server.Tickets1,
                Tickets2 = server.Tickets2,
                Team1Label = team1Label,
                Team2Label = team2Label,
                RoundTimeRemain = server.RoundTimeRemain
            };
            newRound.RoundId = ComputeRoundId(newRound.ServerGuid, newRound.MapName, newRound.StartTime.ToUniversalTime());

            // Upsert semantics: if a round with same RoundId exists, load it
            var existing = await _dbContext.Rounds.FindAsync(newRound.RoundId);
            if (existing == null)
            {
                await _dbContext.Rounds.AddAsync(newRound);
                await _dbContext.SaveChangesAsync();
                active = newRound;
            }
            else
            {
                // If the existing is not active, reopen only if it matches same map and close time is very recent
                active = existing;
                active.IsActive = true;
                active.EndTime = null;
                _dbContext.Rounds.Update(active);
                await _dbContext.SaveChangesAsync();
            }
        }
        else
        {
            // Keep metadata fresh
            var (team1Label, team2Label) = GetTeamLabels(server);
            active.ServerName = server.Name;
            active.GameType = server.GameType ?? "";
            active.Tickets1 = server.Tickets1;
            active.Tickets2 = server.Tickets2;
            active.Team1Label = team1Label;
            active.Team2Label = team2Label;
            active.RoundTimeRemain = server.RoundTimeRemain;
            _dbContext.Rounds.Update(active);
            await _dbContext.SaveChangesAsync();
        }

        return active;
    }

    private (string? team1Label, string? team2Label) GetTeamLabels(IGameServer server)
    {
        string? team1Label = null;
        string? team2Label = null;
        if (server.Teams != null)
        {
            var t1 = server.Teams.FirstOrDefault(t => t.Index == 1);
            var t2 = server.Teams.FirstOrDefault(t => t.Index == 2);
            team1Label = t1?.Label;
            team2Label = t2?.Label;
        }
        return (team1Label, team2Label);
    }

    private async Task RecordRoundObservationAsync(Round? round, IGameServer server, DateTime timestamp)
    {
        if (round == null) return;

        var (team1Label, team2Label) = GetTeamLabels(server);

        var observation = new RoundObservation
        {
            RoundId = round.RoundId,
            Timestamp = timestamp,
            Tickets1 = server.Tickets1,
            Tickets2 = server.Tickets2,
            Team1Label = team1Label,
            Team2Label = team2Label,
            RoundTimeRemain = server.RoundTimeRemain
        };
        await _dbContext.RoundObservations.AddAsync(observation);
        await _dbContext.SaveChangesAsync();
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

    private async Task<IpInfoGeoResult?> LookupGeoLocationAsync(string ip)
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
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to lookup geolocation for IP {IpAddress}", ip);
            return null;
        }
        finally
        {
            IpInfoSemaphore.Release();
        }
    }

    private async Task PublishPlayerEvents(List<(string EventType, PlayerInfo PlayerInfo, PlayerSession Session, string? OldMapName)> eventsToPublish, IGameServer server)
    {
        if (_eventPublisher == null || !eventsToPublish.Any())
        {
            return;
        }

        foreach (var eventData in eventsToPublish)
        {
            try
            {
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
                        break;


                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error publishing player event for {PlayerName} on {ServerName}", eventData.PlayerInfo.Name, server.Name);
            }
        }
    }

    private async Task PublishServerMapChangeEvent(IGameServer server, string oldMapName)
    {
        if (_eventPublisher == null)
        {
            return;
        }

        try
        {
            await _eventPublisher.PublishServerMapChangeEvent(
                server.Guid,
                server.Name,
                oldMapName,
                server.MapName ?? "",
                server.GameType ?? "",
                server.JoinLink);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing server map change event for {ServerName}: {OldMap} -> {NewMap}", server.Name, oldMapName, server.MapName);
        }
    }
}
