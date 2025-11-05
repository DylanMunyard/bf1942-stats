using System.Text.Json;
using api.Bflist.Models;
using api.Caching;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;
using api.Telemetry;
using api.PlayerTracking;

namespace api.Bflist;

public interface IBfListApiService
{
    Task<object[]> FetchServersAsync(string game, int perPage = 100, string? cursor = null, string? after = null);
    Task<object[]> FetchAllServersAsync(string game);
    Task<object?> FetchSingleServerAsync(string game, string serverIdentifier);

    // Helper methods for UI that need ServerSummary
    Task<Models.ServerSummary[]> FetchServerSummariesAsync(string game, int perPage = 100, string? cursor = null, string? after = null);
    Task<Models.ServerSummary[]> FetchAllServerSummariesWithCacheStatusAsync(string game);
    Task<Models.ServerSummary[]> FetchAllServerSummariesAsync(string game);
    Task<Models.ServerSummary?> FetchSingleServerSummaryAsync(string game, string serverIdentifier);
}

public class BfListApiService : IBfListApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly ILogger<BfListApiService> _logger;
    private readonly PlayerTrackerDbContext _dbContext;
    private readonly Models.ServerFilteringConfig _serverFilteringConfig;

    private const int ServerListCacheSeconds = 30;
    private const int SingleServerCacheSeconds = 8; // 8 seconds for individual server updates

    public BfListApiService(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<BfListApiService> logger, PlayerTrackerDbContext dbContext, IConfiguration configuration)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _logger = logger;
        _dbContext = dbContext;
        _serverFilteringConfig = configuration.GetSection("ServerFiltering").Get<Models.ServerFilteringConfig>() ?? new Models.ServerFilteringConfig();
    }

    public async Task<object[]> FetchServersAsync(string game, int perPage = 100, string? cursor = null, string? after = null)
    {
        using var activity = ActivitySources.BfListApi.StartActivity("FetchServers");
        activity?.SetTag("bflist.game", game);
        activity?.SetTag("bflist.per_page", perPage);
        activity?.SetTag("bflist.has_cursor", !string.IsNullOrEmpty(cursor));
        activity?.SetTag("bflist.has_after", !string.IsNullOrEmpty(after));

        var httpClient = _httpClientFactory.CreateClient("BfListApi");
        var baseUrl = $"https://api.bflist.io/v2/{game}/servers?perPage={perPage}";

        if (!string.IsNullOrEmpty(cursor))
        {
            baseUrl += $"&cursor={Uri.EscapeDataString(cursor)}";
        }
        if (!string.IsNullOrEmpty(after))
        {
            baseUrl += $"&after={Uri.EscapeDataString(after)}";
        }

        activity?.SetTag("bflist.url", baseUrl);

        _logger.LogDebug("Fetching servers from BFList API: {Url}", baseUrl);

        var response = await httpClient.GetAsync(baseUrl);
        response.EnsureSuccessStatusCode();
        var content = await response.Content.ReadAsStringAsync();

        if (game.ToLower() == "bf1942")
        {
            var bf1942Response = JsonSerializer.Deserialize<Bf1942ServersResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return bf1942Response?.Servers?.Cast<object>().ToArray() ?? [];
        }
        else if (game.ToLower() == "bfvietnam")
        {
            var bfvResponse = JsonSerializer.Deserialize<Models.BfvietnamServersResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return bfvResponse?.Servers?.Cast<object>().ToArray() ?? [];
        }
        else // fh2
        {
            var fh2Response = JsonSerializer.Deserialize<Models.Fh2ServersResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return fh2Response?.Servers?.Cast<object>().ToArray() ?? [];
        }
    }

    public async Task<object[]> FetchAllServersAsync(string game)
    {
        if (game.ToLower() == "bf1942")
        {
            var cacheKey = $"raw_servers:{game}";
            var cachedResult = await _cacheService.GetAsync<Bf1942ServerInfo[]>(cacheKey);

            if (cachedResult != null)
            {
                _logger.LogDebug("Cache hit for raw servers of game {Game}", game);
                return cachedResult.Cast<object>().ToArray();
            }

            _logger.LogDebug("Cache miss for raw servers of game {Game}", game);
            var freshServers = await FetchAllServersFromApiAsync(game);
            var typedServers = freshServers.Cast<Bf1942ServerInfo>()
                .Where(server => !IsStuckServer(server.Name))
                .ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return typedServers.Cast<object>().ToArray();
        }
        else if (game.ToLower() == "bfvietnam")
        {
            var cacheKey = $"raw_servers:{game}";
            var cachedResult = await _cacheService.GetAsync<BfvietnamServerInfo[]>(cacheKey);

            if (cachedResult != null)
            {
                _logger.LogDebug("Cache hit for raw servers of game {Game}", game);
                return cachedResult.Cast<object>().ToArray();
            }

            _logger.LogDebug("Cache miss for raw servers of game {Game}", game);
            var freshServers = await FetchAllServersFromApiAsync(game);
            var typedServers = freshServers.Cast<BfvietnamServerInfo>()
                .Where(server => !IsStuckServer(server.Name))
                .ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return typedServers.Cast<object>().ToArray();
        }
        else // fh2
        {
            var cacheKey = $"raw_servers:{game}";
            var cachedResult = await _cacheService.GetAsync<Fh2ServerInfo[]>(cacheKey);

            if (cachedResult != null)
            {
                _logger.LogDebug("Cache hit for raw servers of game {Game}", game);
                return cachedResult.Cast<object>().ToArray();
            }

            _logger.LogDebug("Cache miss for raw servers of game {Game}", game);
            var freshServers = await FetchAllServersFromApiAsync(game);
            var typedServers = freshServers.Cast<Fh2ServerInfo>()
                .Where(server => !IsStuckServer(server.Name))
                .ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return typedServers.Cast<object>().ToArray();
        }
    }

    private async Task<object[]> FetchAllServersFromApiAsync(string game)
    {
        var allServers = new List<object>();
        string? cursor = null;
        string? after = null;
        var pageCount = 0;
        const int maxPages = 50; // Increased from 10 to ensure we get all servers
        bool hasMore = true;

        while (hasMore && pageCount < maxPages)
        {
            pageCount++;

            var httpClient = _httpClientFactory.CreateClient("BfListApi");
            var baseUrl = $"https://api.bflist.io/v2/{game}/servers?perPage=100";

            if (!string.IsNullOrEmpty(cursor))
            {
                baseUrl += $"&cursor={Uri.EscapeDataString(cursor)}";
            }
            if (!string.IsNullOrEmpty(after))
            {
                baseUrl += $"&after={Uri.EscapeDataString(after)}";
            }

            _logger.LogDebug("Fetching servers page {PageCount} from BFList API: {Url}", pageCount, baseUrl);

            var response = await httpClient.GetAsync(baseUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            if (game.ToLower() == "bf1942")
            {
                var bf1942Response = JsonSerializer.Deserialize<Bf1942ServersResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (bf1942Response?.Servers != null && bf1942Response.Servers.Length > 0)
                {
                    allServers.AddRange(bf1942Response.Servers.Cast<object>());

                    // Set pagination parameters for next request
                    cursor = bf1942Response.Cursor;
                    after = $"{bf1942Response.Servers.Last().Ip}:{bf1942Response.Servers.Last().Port}";
                    hasMore = bf1942Response.HasMore;
                }
                else
                {
                    hasMore = false;
                }
            }
            else if (game.ToLower() == "bfvietnam")
            {
                var bfvResponse = JsonSerializer.Deserialize<Models.BfvietnamServersResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (bfvResponse?.Servers != null && bfvResponse.Servers.Length > 0)
                {
                    allServers.AddRange(bfvResponse.Servers.Cast<object>());

                    // Set pagination parameters for next request
                    cursor = bfvResponse.Cursor;
                    after = $"{bfvResponse.Servers.Last().Ip}:{bfvResponse.Servers.Last().Port}";
                    hasMore = bfvResponse.HasMore;
                }
                else
                {
                    hasMore = false;
                }
            }
            else // fh2
            {
                var fh2Response = JsonSerializer.Deserialize<Models.Fh2ServersResponse>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (fh2Response?.Servers != null && fh2Response.Servers.Length > 0)
                {
                    allServers.AddRange(fh2Response.Servers.Cast<object>());

                    // Set pagination parameters for next request
                    cursor = fh2Response.Cursor;
                    after = $"{fh2Response.Servers.Last().Ip}:{fh2Response.Servers.Last().Port}";
                    hasMore = fh2Response.HasMore;
                }
                else
                {
                    hasMore = false;
                }
            }
        }

        if (pageCount >= maxPages && hasMore)
        {
            _logger.LogWarning("Reached maximum pages ({MaxPages}) while fetching all servers for game {Game}, there may be more servers", maxPages, game);
        }

        _logger.LogDebug("Fetched {TotalServers} servers across {PageCount} pages for game {Game}", allServers.Count, pageCount, game);

        return allServers.ToArray();
    }

    public async Task<object?> FetchSingleServerAsync(string game, string serverIdentifier)
    {
        var httpClient = _httpClientFactory.CreateClient("BfListApi");
        var baseUrl = $"https://api.bflist.io/v2/{game}/servers/{serverIdentifier}";

        _logger.LogDebug("Fetching single server from BFList API: {Url}", baseUrl);

        try
        {
            var response = await httpClient.GetAsync(baseUrl);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();

            if (game.ToLower() == "bf1942")
            {
                var bf1942Server = JsonSerializer.Deserialize<Bf1942ServerInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return bf1942Server;
            }
            else if (game.ToLower() == "bfvietnam")
            {
                var bfvServer = JsonSerializer.Deserialize<BfvietnamServerInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return bfvServer;
            }
            else // fh2
            {
                var fh2Server = JsonSerializer.Deserialize<Fh2ServerInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                return fh2Server;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Failed to fetch single server {ServerIdentifier}: {Error}", serverIdentifier, ex.Message);
            return null;
        }
    }

    // Helper methods for UI that need ServerSummary
    public async Task<Models.ServerSummary[]> FetchServerSummariesAsync(string game, int perPage = 100, string? cursor = null, string? after = null)
    {
        var servers = await FetchServersAsync(game, perPage, cursor, after);
        return ConvertToServerSummaries(servers, game);
    }

    public async Task<Models.ServerSummary[]> FetchAllServerSummariesWithCacheStatusAsync(string game)
    {
        var servers = await FetchAllServersAsync(game);
        return ConvertToServerSummaries(servers, game);
    }

    public async Task<Models.ServerSummary[]> FetchAllServerSummariesAsync(string game)
    {
        return await FetchAllServerSummariesWithCacheStatusAsync(game);
    }

    public async Task<Models.ServerSummary?> FetchSingleServerSummaryAsync(string game, string serverIdentifier)
    {
        var cacheKey = $"server:{game}:{serverIdentifier}";
        var cachedResult = await _cacheService.GetAsync<Models.ServerSummary>(cacheKey);

        if (cachedResult != null)
        {
            _logger.LogDebug("Cache hit for server {Game}:{ServerIdentifier}", game, serverIdentifier);
            return cachedResult;
        }

        _logger.LogDebug("Cache miss for server {Game}:{ServerIdentifier}", game, serverIdentifier);
        var server = await FetchSingleServerAsync(game, serverIdentifier);

        if (server == null) return null;

        if (game.ToLower() == "bf1942" && server is Bf1942ServerInfo bf1942Server)
        {
            var summary = MapBf1942ToSummary(bf1942Server);
            await _cacheService.SetAsync(cacheKey, summary, TimeSpan.FromSeconds(SingleServerCacheSeconds));
            return summary;
        }
        else if (game.ToLower() == "bfvietnam" && server is BfvietnamServerInfo bfvServer)
        {
            var summary = MapBfvToSummary(bfvServer);
            await _cacheService.SetAsync(cacheKey, summary, TimeSpan.FromSeconds(SingleServerCacheSeconds));
            return summary;
        }
        else if (server is Fh2ServerInfo fh2Server)
        {
            var summary = MapFh2ToSummary(fh2Server);
            await _cacheService.SetAsync(cacheKey, summary, TimeSpan.FromSeconds(SingleServerCacheSeconds));
            return summary;
        }

        return null;
    }

    private Models.ServerSummary[] ConvertToServerSummaries(object[] servers, string game)
    {
        if (game.ToLower() == "bf1942")
        {
            return servers.Cast<Bf1942ServerInfo>()
                .Select(MapBf1942ToSummary)
                .OrderByDescending(s => s.NumPlayers)
                .ToArray();
        }
        else if (game.ToLower() == "bfvietnam")
        {
            return servers.Cast<BfvietnamServerInfo>()
                .Select(MapBfvToSummary)
                .OrderByDescending(s => s.NumPlayers)
                .ToArray();
        }
        else // fh2
        {
            return servers.Cast<Fh2ServerInfo>()
                .Select(MapFh2ToSummary)
                .OrderByDescending(s => s.NumPlayers)
                .ToArray();
        }
    }

    private bool IsStuckServer(string serverName)
    {
        if (_serverFilteringConfig.StuckServers.Contains(serverName))
        {
            _logger.LogDebug("Filtering out stuck server: {ServerName}", serverName);
            return true;
        }
        return false;
    }

    private PlayerInfo[] FilterDuplicatePlayers(PlayerInfo[] players, string serverName)
    {
        if (players == null || players.Length == 0)
            return players ?? [];

        var groupedPlayers = players.GroupBy(p => p.Name).ToArray();
        var duplicateGroups = groupedPlayers.Where(g => g.Count() > 1).ToArray();

        if (duplicateGroups.Any())
        {
            _logger.LogWarning("Found {DuplicateCount} duplicate player groups in server {ServerName}: {DuplicateNames}",
                duplicateGroups.Length,
                serverName,
                string.Join(", ", duplicateGroups.Select(g => $"{g.Key} (x{g.Count()})")));
        }

        // For each group, keep only the player with the highest score
        var filteredPlayers = groupedPlayers
            .Select(group => group.OrderByDescending(p => p.Score).First())
            .ToArray();

        return filteredPlayers;
    }

    private Models.ServerSummary MapBf1942ToSummary(Bf1942ServerInfo server)
    {
        var filteredPlayers = FilterDuplicatePlayers(server.Players ?? [], server.Name);

        return new Models.ServerSummary
        {
            Guid = server.Guid,
            Name = server.Name,
            Ip = server.Ip,
            Port = server.Port,
            NumPlayers = server.NumPlayers,
            MaxPlayers = server.MaxPlayers,
            MapName = server.MapName,
            GameType = server.GameType,
            JoinLink = server.JoinLink,
            RoundTimeRemain = server.RoundTimeRemain,
            Tickets1 = server.Tickets1,
            Tickets2 = server.Tickets2,
            Players = filteredPlayers,
            Teams = server.Teams ?? [],
            GameId = server.GameId
        };
    }

    private Models.ServerSummary MapBfvToSummary(BfvietnamServerInfo server)
    {
        var filteredPlayers = FilterDuplicatePlayers(server.Players ?? [], server.Name);

        return new Models.ServerSummary
        {
            Guid = server.Guid,
            Name = server.Name,
            Ip = server.Ip,
            Port = server.Port,
            NumPlayers = server.NumPlayers,
            MaxPlayers = server.MaxPlayers,
            MapName = server.MapName,
            GameType = server.GameType,
            JoinLink = server.JoinLink,
            RoundTimeRemain = 0, // BFV doesn't have this field in the provided sample
            Tickets1 = server.Teams?.FirstOrDefault(t => t.Index == 1)?.Tickets ?? 0,
            Tickets2 = server.Teams?.FirstOrDefault(t => t.Index == 2)?.Tickets ?? 0,
            Players = filteredPlayers,
            Teams = server.Teams ?? [],
            GameId = server.GameId
        };
    }

    private Models.ServerSummary MapFh2ToSummary(Fh2ServerInfo server)
    {
        var filteredPlayers = FilterDuplicatePlayers(server.Players?.ToArray() ?? [], server.Name);

        return new Models.ServerSummary
        {
            Guid = server.Guid,
            Name = server.Name,
            Ip = server.Ip,
            Port = server.Port,
            NumPlayers = server.NumPlayers,
            MaxPlayers = server.MaxPlayers,
            MapName = server.MapName,
            GameType = server.GameType,
            JoinLink = "", // FH2 doesn't have join links in the current model
            RoundTimeRemain = server.Timelimit,
            Tickets1 = 0, // FH2 doesn't have tickets in the current model
            Tickets2 = 0,
            Players = filteredPlayers,
            Teams = server.Teams?.ToArray() ?? [],
            GameId = server.GameVariant
        };
    }
}
