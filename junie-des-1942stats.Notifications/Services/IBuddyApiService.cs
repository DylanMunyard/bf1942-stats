using Microsoft.Extensions.Logging;

namespace junie_des_1942stats.Notifications.Services;

public interface IBuddyApiService
{
    Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName);
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

    public async Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName)
    {
        try
        {
            // TEMPORARY: For testing, always return dmunyard@gmail.com
            // TODO: Implement API call to main service
            _logger.LogInformation("TESTING: Returning dmunyard@gmail.com for buddy {BuddyName}", buddyPlayerName);
            return new[] { "dmunyard@gmail.com" };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
            return Enumerable.Empty<string>();
        }
    }
}