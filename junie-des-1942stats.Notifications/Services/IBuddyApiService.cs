using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Configuration;

namespace junie_des_1942stats.Notifications.Services;

public interface IBuddyApiService
{
    Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName);
    Task<IEnumerable<string>> GetUsersWithFavouriteServer(string serverGuid);
}

public class BuddyApiService : IBuddyApiService
{
    private readonly HttpClient _httpClient;
    private readonly ILogger<BuddyApiService> _logger;
    private readonly string _apiBaseUrl;

    public BuddyApiService(HttpClient httpClient, ILogger<BuddyApiService> logger, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _logger = logger;
        _apiBaseUrl = configuration["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl configuration is required");
    }

    public async Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName)
    {
        try
        {
            _logger.LogInformation("Getting users with buddy {BuddyName} from API", buddyPlayerName);
            
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/stats/notification/users-with-buddy?buddyPlayerName={Uri.EscapeDataString(buddyPlayerName)}");
            
            if (response.IsSuccessStatusCode)
            {
                var userEmails = await response.Content.ReadFromJsonAsync<string[]>();
                _logger.LogInformation("Found {Count} users with buddy {BuddyName}", userEmails?.Length ?? 0, buddyPlayerName);
                return userEmails ?? Enumerable.Empty<string>();
            }
            else
            {
                _logger.LogWarning("API call failed with status {StatusCode} for buddy {BuddyName}", response.StatusCode, buddyPlayerName);
                return Enumerable.Empty<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<string>> GetUsersWithFavouriteServer(string serverGuid)
    {
        try
        {
            _logger.LogInformation("Getting users with favourite server {ServerGuid} from API", serverGuid);
            
            var response = await _httpClient.GetAsync($"{_apiBaseUrl}/stats/notification/users-with-favourite-server?serverGuid={Uri.EscapeDataString(serverGuid)}");
            
            if (response.IsSuccessStatusCode)
            {
                var userEmails = await response.Content.ReadFromJsonAsync<string[]>();
                _logger.LogInformation("Found {Count} users with favourite server {ServerGuid}", userEmails?.Length ?? 0, serverGuid);
                return userEmails ?? Enumerable.Empty<string>();
            }
            else
            {
                _logger.LogWarning("API call failed with status {StatusCode} for favourite server {ServerGuid}", response.StatusCode, serverGuid);
                return Enumerable.Empty<string>();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error getting users with favourite server {ServerGuid}", serverGuid);
            return Enumerable.Empty<string>();
        }
    }
}