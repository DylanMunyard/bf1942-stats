using System.Text.Json;
using junie_des_1942stats.Bflist;
using junie_des_1942stats.StatsCollectors.Modals;
using junie_des_1942stats.Caching;
using Microsoft.Extensions.Logging;
using junie_des_1942stats.Telemetry;
using System.Diagnostics;
using Microsoft.EntityFrameworkCore;
using junie_des_1942stats.PlayerTracking;

namespace junie_des_1942stats.Services;

public interface IBfListApiService
{
    Task<object[]> FetchServersAsync(string game, int perPage = 100, string? cursor = null, string? after = null);
    Task<object[]> FetchAllServersAsync(string game);
    Task<object?> FetchSingleServerAsync(string game, string serverIdentifier);

    // Helper methods for UI that need ServerSummary
    Task<ServerSummary[]> FetchServerSummariesAsync(string game, int perPage = 100, string? cursor = null, string? after = null);
    Task<ServerSummary[]> FetchAllServerSummariesWithCacheStatusAsync(string game);
    Task<ServerSummary[]> FetchAllServerSummariesAsync(string game);
    Task<ServerSummary?> FetchSingleServerSummaryAsync(string game, string serverIdentifier);
}

public class BfListApiService : IBfListApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ICacheService _cacheService;
    private readonly ILogger<BfListApiService> _logger;
    private readonly PlayerTrackerDbContext _dbContext;

    private const int ServerListCacheSeconds = 30;
    private const int SingleServerCacheSeconds = 8; // 8 seconds for individual server updates

    public BfListApiService(IHttpClientFactory httpClientFactory, ICacheService cacheService, ILogger<BfListApiService> logger, PlayerTrackerDbContext dbContext)
    {
        _httpClientFactory = httpClientFactory;
        _cacheService = cacheService;
        _logger = logger;
        _dbContext = dbContext;
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
            var bfvResponse = JsonSerializer.Deserialize<BfvietnamServersResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });

            return bfvResponse?.Servers?.Cast<object>().ToArray() ?? [];
        }
        else // fh2
        {
            var fh2Response = JsonSerializer.Deserialize<Fh2ServersResponse>(content, new JsonSerializerOptions
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
            var typedServers = freshServers.Cast<Bf1942ServerInfo>().ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return freshServers;
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
            var typedServers = freshServers.Cast<BfvietnamServerInfo>().ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return freshServers;
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
            var typedServers = freshServers.Cast<Fh2ServerInfo>().ToArray();
            await _cacheService.SetAsync(cacheKey, typedServers, TimeSpan.FromSeconds(ServerListCacheSeconds));
            return freshServers;
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
                var bfvResponse = JsonSerializer.Deserialize<BfvietnamServersResponse>(content, new JsonSerializerOptions
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
                var fh2Response = JsonSerializer.Deserialize<Fh2ServersResponse>(content, new JsonSerializerOptions
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
    public async Task<ServerSummary[]> FetchServerSummariesAsync(string game, int perPage = 100, string? cursor = null, string? after = null)
    {
        var servers = await FetchServersAsync(game, perPage, cursor, after);
        return ConvertToServerSummaries(servers, game);
    }

    public async Task<ServerSummary[]> FetchAllServerSummariesWithCacheStatusAsync(string game)
    {
        var servers = await FetchAllServersAsync(game);
        return ConvertToServerSummaries(servers, game);
    }

    public async Task<ServerSummary[]> FetchAllServerSummariesAsync(string game)
    {
        return await FetchAllServerSummariesWithCacheStatusAsync(game);
    }

    public async Task<ServerSummary?> FetchSingleServerSummaryAsync(string game, string serverIdentifier)
    {
        var cacheKey = $"server:{game}:{serverIdentifier}";
        var cachedResult = await _cacheService.GetAsync<ServerSummary>(cacheKey);

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

    private ServerSummary[] ConvertToServerSummaries(object[] servers, string game)
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

    private static ServerSummary MapBf1942ToSummary(Bf1942ServerInfo server)
    {
        return new ServerSummary
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
            Players = server.Players ?? [],
            Teams = server.Teams ?? []
        };
    }

    private static ServerSummary MapBfvToSummary(BfvietnamServerInfo server)
    {
        return new ServerSummary
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
            Players = server.Players ?? [],
            Teams = server.Teams ?? []
        };
    }

    private static ServerSummary MapFh2ToSummary(Fh2ServerInfo server)
    {
        return new ServerSummary
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
            Players = server.Players?.ToArray() ?? [],
            Teams = server.Teams?.ToArray() ?? []
        };
    }
}

/// <summary>
/// Response DTO for server list endpoint
/// </summary>
public class ServerListResponse
{
    /// <summary>
    /// Array of server summaries
    /// </summary>
    public ServerSummary[] Servers { get; set; } = [];

    /// <summary>
    /// ISO timestamp of when the data was last updated
    /// </summary>
    public string LastUpdated { get; set; } = "";

}

public class BfvietnamServersResponse : ServerListResponse
{
    public new BfvietnamServerInfo[] Servers { get; set; } = [];
    public string? Cursor { get; set; }
    public bool HasMore { get; set; }
}

/// <summary>
/// Minimal server summary containing only fields needed for the servers table
/// </summary>
public class ServerSummary
{
    /// <summary>
    /// Unique server identifier
    /// </summary>
    public string Guid { get; set; } = "";

    /// <summary>
    /// Server name
    /// </summary>
    public string Name { get; set; } = "";

    /// <summary>
    /// Server IP address
    /// </summary>
    public string Ip { get; set; } = "";

    /// <summary>
    /// Server port
    /// </summary>
    public int Port { get; set; }

    /// <summary>
    /// Current number of players
    /// </summary>
    public int NumPlayers { get; set; }

    /// <summary>
    /// Maximum number of players
    /// </summary>
    public int MaxPlayers { get; set; }

    /// <summary>
    /// Current map name
    /// </summary>
    public string MapName { get; set; } = "";

    /// <summary>
    /// Game type/mode
    /// </summary>
    public string GameType { get; set; } = "";

    /// <summary>
    /// Direct join link
    /// </summary>
    public string JoinLink { get; set; } = "";

    /// <summary>
    /// Remaining round time in seconds
    /// </summary>
    public int RoundTimeRemain { get; set; }

    /// <summary>
    /// Team 1 tickets
    /// </summary>
    public int Tickets1 { get; set; }

    /// <summary>
    /// Team 2 tickets
    /// </summary>
    public int Tickets2 { get; set; }

    /// <summary>
    /// Current players on the server
    /// </summary>
    public PlayerInfo[] Players { get; set; } = [];

    /// <summary>
    /// Team information
    /// </summary>
    public TeamInfo[] Teams { get; set; } = [];

    /// <summary>
    /// Server country from geo location data
    /// </summary>
    public string? Country { get; set; }

    /// <summary>
    /// Server region from geo location data
    /// </summary>
    public string? Region { get; set; }

    /// <summary>
    /// Server city from geo location data
    /// </summary>
    public string? City { get; set; }

    /// <summary>
    /// Server location coordinates (latitude,longitude) from geo location data
    /// </summary>
    public string? Loc { get; set; }

    /// <summary>
    /// Server timezone from geo location data
    /// </summary>
    public string? Timezone { get; set; }

    /// <summary>
    /// Server organization/ASN from geo location data
    /// </summary>
    public string? Org { get; set; }

    /// <summary>
    /// Server postal code from geo location data
    /// </summary>
    public string? Postal { get; set; }

    /// <summary>
    /// Date when geo location was last looked up
    /// </summary>
    public DateTime? GeoLookupDate { get; set; }

    /// <summary>
    /// Whether the server is currently online
    /// </summary>
    public bool IsOnline { get; set; } = true;

    /// <summary>
    /// When the server was last seen online
    /// </summary>
    public DateTime LastSeenTime { get; set; } = DateTime.UtcNow;

    /// <summary>
    /// Server Discord invite URL
    /// </summary>
    public string? DiscordUrl { get; set; }

    /// <summary>
    /// Server forum URL
    /// </summary>
    public string? ForumUrl { get; set; }
}

public class PlayersOnlineHistoryResponse
{
    /// <summary>
    /// Array of player count data points over time
    /// </summary>
    public PlayersOnlineDataPoint[] DataPoints { get; set; } = [];
    
    /// <summary>
    /// Trend analysis and insights for the requested period
    /// </summary>
    public PlayerTrendsInsights? Insights { get; set; }
    
    /// <summary>
    /// The period for which the data was requested (e.g., "7d", "3d", "1d", "1month", "3months", "thisyear", "alltime")
    /// </summary>
    public string Period { get; set; } = "";
    
    /// <summary>
    /// The game for which the data was requested
    /// </summary>
    public string Game { get; set; } = "";
    
    /// <summary>
    /// When the data was last updated
    /// </summary>
    public string LastUpdated { get; set; } = "";
}

public class PlayersOnlineDataPoint
{
    /// <summary>
    /// Timestamp of the data point
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// Total number of players online at this timestamp
    /// </summary>
    public int TotalPlayers { get; set; }
}

public class PlayerTrendsInsights
{
    /// <summary>
    /// Overall average players for the entire period
    /// </summary>
    public double OverallAverage { get; set; }
    
    /// <summary>
    /// 7-day rolling average data points (for periods longer than 7 days)
    /// </summary>
    public RollingAverageDataPoint[] RollingAverage { get; set; } = [];
    
    /// <summary>
    /// Trend direction: "increasing", "decreasing", "stable"
    /// </summary>
    public string TrendDirection { get; set; } = "";
    
    /// <summary>
    /// Percentage change from start to end of period
    /// </summary>
    public double PercentageChange { get; set; }
    
    /// <summary>
    /// Peak player count in the period
    /// </summary>
    public int PeakPlayers { get; set; }
    
    /// <summary>
    /// Timestamp when peak was reached
    /// </summary>
    public DateTime PeakTimestamp { get; set; }
    
    /// <summary>
    /// Lowest player count in the period
    /// </summary>
    public int LowestPlayers { get; set; }
    
    /// <summary>
    /// Timestamp when lowest count was reached
    /// </summary>
    public DateTime LowestTimestamp { get; set; }
    
    /// <summary>
    /// Explanation of how player counts are calculated for this time period
    /// </summary>
    public string CalculationMethod { get; set; } = "";
}

public class RollingAverageDataPoint
{
    /// <summary>
    /// Timestamp of the rolling average data point
    /// </summary>
    public DateTime Timestamp { get; set; }
    
    /// <summary>
    /// 7-day rolling average of players online
    /// </summary>
    public double Average { get; set; }
}
