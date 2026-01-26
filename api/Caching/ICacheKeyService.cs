using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace api.Caching;

public interface ICacheKeyService
{
    string GetPlayerComparisonKey(string player1, string player2, string? serverGuid = null);
    string GetServerStatisticsKey(string serverName, int daysToAnalyze);
    string GetServerLeaderboardsKey(string serverName, int days);
    string GetServerPlaytimeRankingsKey(IEnumerable<string> serverGuids, int days);
    string GetServerInsightsKey(string serverName, int daysToAnalyze, int? rollingWindowDays = null);
    string GetServerMapsInsightsKey(string serverName, int daysToAnalyze);
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

    public string GetServerLeaderboardsKey(string serverName, int days)
    {
        return $"server_leaderboards:{serverName}:{days}";
    }

    public string GetServerPlaytimeRankingsKey(IEnumerable<string> serverGuids, int days)
    {
        var orderedServerGuids = serverGuids.OrderBy(g => g).ToArray();
        var serverGuidsHash = ComputeHash(string.Join("|", orderedServerGuids));
        return $"server_playtime_rankings:{serverGuidsHash}:{days}";
    }

    public string GetServerInsightsKey(string serverName, int daysToAnalyze, int? rollingWindowDays = null)
    {
        var key = $"server_insights:{serverName}:{daysToAnalyze}";
        if (rollingWindowDays.HasValue)
        {
            key += $":{rollingWindowDays}";
        }
        return key;
    }

    public string GetServerMapsInsightsKey(string serverName, int daysToAnalyze)
    {
        return $"server_maps_insights:{serverName}:{daysToAnalyze}";
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
