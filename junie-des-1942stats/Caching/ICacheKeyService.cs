using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace junie_des_1942stats.Caching;

public interface ICacheKeyService
{
    string GetPlayerComparisonKey(string player1, string player2, string? serverGuid = null);
    string GetServerStatisticsKey(string serverName, int daysToAnalyze);
    string GetServerRankingsKey(string serverName, int? year, int page, int pageSize, string? playerName, int? minScore, int? minKills, int? minDeaths, double? minKdRatio, int? minPlayTimeMinutes, string? orderBy, string? orderDirection);
    string GetServerInsightsKey(string serverName, int daysToAnalyze);
    string GetServerInsightsKey(string serverName, string period);
    string GetServersPageKey(int page, int pageSize, string sortBy, string sortOrder, object? filters);
}

public class CacheKeyService : ICacheKeyService
{
    public string GetPlayerComparisonKey(string player1, string player2, string? serverGuid = null)
    {
        var orderedPlayers = new[] { player1, player2 }.OrderBy(p => p).ToArray();
        var baseKey = $"player_comparison:{orderedPlayers[0]}:{orderedPlayers[1]}";
        return serverGuid != null ? $"{baseKey}:{serverGuid}" : baseKey;
    }

    public string GetServerStatisticsKey(string serverName, int daysToAnalyze)
    {
        return $"server_stats:{serverName}:{daysToAnalyze}";
    }

    public string GetServerRankingsKey(string serverName, int? year, int page, int pageSize, string? playerName, int? minScore, int? minKills, int? minDeaths, double? minKdRatio, int? minPlayTimeMinutes, string? orderBy, string? orderDirection)
    {
        var parameters = new[]
        {
            year?.ToString() ?? "null",
            page.ToString(),
            pageSize.ToString(),
            playerName ?? "null",
            minScore?.ToString() ?? "null",
            minKills?.ToString() ?? "null",
            minDeaths?.ToString() ?? "null",
            minKdRatio?.ToString() ?? "null",
            minPlayTimeMinutes?.ToString() ?? "null",
            orderBy ?? "null",
            orderDirection ?? "null"
        };
        
        var parametersHash = ComputeHash(string.Join("|", parameters));
        return $"server_rankings:{serverName}:{parametersHash}";
    }

    public string GetServerInsightsKey(string serverName, int daysToAnalyze)
    {
        return $"server_insights:{serverName}:{daysToAnalyze}";
    }

    public string GetServerInsightsKey(string serverName, string period)
    {
        return $"server_insights:{serverName}:{period}";
    }

    public string GetServersPageKey(int page, int pageSize, string sortBy, string sortOrder, object? filters)
    {
        var parameters = new[]
        {
            page.ToString(),
            pageSize.ToString(),
            sortBy ?? "null",
            sortOrder ?? "null",
            JsonSerializer.Serialize(filters) ?? "null"
        };
        
        var parametersHash = ComputeHash(string.Join("|", parameters));
        return $"servers_page:{parametersHash}";
    }

    private static string ComputeHash(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes)[..8];
    }
}