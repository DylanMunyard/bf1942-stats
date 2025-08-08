using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Notifications.Services;

public interface IBuddyApiService
{
    Task<IEnumerable<int>> GetUsersWithBuddy(string buddyPlayerName);
}

public class BuddyApiService : IBuddyApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BuddyApiService> _logger;

    public BuddyApiService(HttpClient httpClient, ILogger<BuddyApiService> logger)
    {
        _httpClient = httpClient;
        _logger = logger;
    }

    public async Task<IEnumerable<int>> GetUsersWithBuddy(string buddyPlayerName)
    {
        try
        {
            // TODO: Implement API call to main service
            // For now, return empty list
            _logger.LogInformation("Getting users with buddy {BuddyName} - API call not yet implemented", buddyPlayerName);
            return Enumerable.Empty<int>();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
            return Enumerable.Empty<int>();
        }
    }
}