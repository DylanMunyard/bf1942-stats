using System.Text.Json;
using junie_des_1942stats.Bflist;
using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Services;

public interface IBfListApiService
{
    Task<ServerSummary[]> FetchServersAsync(string game, int perPage = 100, string? cursor = null, string? after = null);
    Task<ServerSummary[]> FetchAllServersAsync(string game);
    Task<ServerSummary?> FetchSingleServerAsync(string game, string serverIdentifier);
}

public class BfListApiService : IBfListApiService
{
    private readonly IHttpClientFactory _httpClientFactory;
    private readonly ILogger<BfListApiService> _logger;
    
    public BfListApiService(IHttpClientFactory httpClientFactory, ILogger<BfListApiService> logger)
    {
        _httpClientFactory = httpClientFactory;
        _logger = logger;
    }
    
    public async Task<ServerSummary[]> FetchServersAsync(string game, int perPage = 100, string? cursor = null, string? after = null)
    {
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
            
            var servers = bf1942Response?.Servers?.Select(MapBf1942ToSummary).ToArray() ?? [];
            
            // Sort by player count descending (as per requirements)
            return servers.OrderByDescending(s => s.NumPlayers).ToArray();
        }
        else // fh2
        {
            var fh2Response = JsonSerializer.Deserialize<Fh2ServersResponse>(content, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            });
            
            var servers = fh2Response?.Servers?.Select(MapFh2ToSummary).ToArray() ?? [];
            
            // Sort by player count descending (as per requirements)
            return servers.OrderByDescending(s => s.NumPlayers).ToArray();
        }
    }
    
    public async Task<ServerSummary[]> FetchAllServersAsync(string game)
    {
        var allServers = new List<ServerSummary>();
        string? cursor = null;
        string? after = null;
        var pageCount = 0;
        const int maxPages = 10;
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
                    var servers = bf1942Response.Servers.Select(MapBf1942ToSummary).ToArray();
                    allServers.AddRange(servers);
                    
                    // Set pagination parameters for next request
                    cursor = bf1942Response.Cursor;
                    after = $"{servers.Last().Ip}:{servers.Last().Port}";
                    hasMore = bf1942Response.HasMore;
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
                    var servers = fh2Response.Servers.Select(MapFh2ToSummary).ToArray();
                    allServers.AddRange(servers);
                    
                    // Set pagination parameters for next request
                    cursor = fh2Response.Cursor;
                    after = $"{servers.Last().Ip}:{servers.Last().Port}";
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
        
        // Sort all servers by player count descending
        return allServers.OrderByDescending(s => s.NumPlayers).ToArray();
    }
    
    public async Task<ServerSummary?> FetchSingleServerAsync(string game, string serverIdentifier)
    {
        var httpClient = _httpClientFactory.CreateClient("BfListApi");
        var url = $"https://api.bflist.io/v2/{game}/servers/{Uri.EscapeDataString(serverIdentifier)}";
        
        _logger.LogDebug("Fetching single server from BFList API: {Url}", url);
        
        try
        {
            var response = await httpClient.GetAsync(url);
            response.EnsureSuccessStatusCode();
            var content = await response.Content.ReadAsStringAsync();
            
            if (game.ToLower() == "bf1942")
            {
                var bf1942Server = JsonSerializer.Deserialize<Bf1942ServerInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                return bf1942Server != null ? MapBf1942ToSummary(bf1942Server) : null;
            }
            else // fh2
            {
                var fh2Server = JsonSerializer.Deserialize<Fh2ServerInfo>(content, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
                
                return fh2Server != null ? MapFh2ToSummary(fh2Server) : null;
            }
        }
        catch (HttpRequestException ex)
        {
            _logger.LogWarning("Failed to fetch single server {ServerIdentifier}: {Error}", serverIdentifier, ex.Message);
            return null;
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
    
    /// <summary>
    /// Whether this response was served from cache
    /// </summary>
    public bool CacheHit { get; set; }
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
}