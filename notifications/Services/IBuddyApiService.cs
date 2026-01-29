using notifications.Telemetry;
using System.Diagnostics;

namespace notifications.Services;

public interface IBuddyApiService
{
    Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName);
    Task<IEnumerable<string>> GetUsersWithFavouriteServer(string serverGuid);
}

public class BuddyApiService(HttpClient httpClient, ILogger<BuddyApiService> logger, IConfiguration configuration)
    : IBuddyApiService
{
    private readonly string _apiBaseUrl = configuration["ApiBaseUrl"] ?? throw new InvalidOperationException("ApiBaseUrl configuration is required");

    public async Task<IEnumerable<string>> GetUsersWithBuddy(string buddyPlayerName)
    {
        using var activity = ActivitySources.Http.StartActivity("GetUsersWithBuddy");
        activity?.SetTag("buddy.player_name", buddyPlayerName);

        try
        {
            logger.LogInformation("Getting users with buddy {BuddyName} from API", buddyPlayerName);

            var response = await httpClient.GetAsync($"{_apiBaseUrl}/stats/notification/users-with-buddy?buddyPlayerName={Uri.EscapeDataString(buddyPlayerName)}");
            activity?.SetTag("http.status_code", (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var userEmails = await response.Content.ReadFromJsonAsync<string[]>();
                var count = userEmails?.Length ?? 0;
                activity?.SetTag("users.count", count);
                logger.LogInformation("Found {Count} users with buddy {BuddyName}", count, buddyPlayerName);
                return userEmails ?? Enumerable.Empty<string>();
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                logger.LogWarning("API call failed with status {StatusCode} for buddy {BuddyName}", response.StatusCode, buddyPlayerName);
                return Enumerable.Empty<string>();
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error getting users with buddy {BuddyName}", buddyPlayerName);
            return Enumerable.Empty<string>();
        }
    }

    public async Task<IEnumerable<string>> GetUsersWithFavouriteServer(string serverGuid)
    {
        using var activity = ActivitySources.Http.StartActivity("GetUsersWithFavouriteServer");
        activity?.SetTag("server.guid", serverGuid);

        try
        {
            logger.LogDebug("Getting users with favourite server {ServerGuid} from API", serverGuid);

            var response = await httpClient.GetAsync($"{_apiBaseUrl}/stats/notification/users-with-favourite-server?serverGuid={Uri.EscapeDataString(serverGuid)}");
            activity?.SetTag("http.status_code", (int)response.StatusCode);

            if (response.IsSuccessStatusCode)
            {
                var userEmails = await response.Content.ReadFromJsonAsync<string[]>();
                var count = userEmails?.Length ?? 0;
                activity?.SetTag("users.count", count);
                logger.LogDebug("Found {Count} users with favourite server {ServerGuid}", count, serverGuid);
                return userEmails ?? Enumerable.Empty<string>();
            }
            else
            {
                activity?.SetStatus(ActivityStatusCode.Error, $"API call failed with status {response.StatusCode}");
                logger.LogWarning("API call failed with status {StatusCode} for favourite server {ServerGuid}", response.StatusCode, serverGuid);
                return [];
            }
        }
        catch (Exception ex)
        {
            activity?.SetStatus(ActivityStatusCode.Error, ex.Message);
            logger.LogError(ex, "Error getting users with favourite server {ServerGuid}", serverGuid);
            return [];
        }
    }
}
