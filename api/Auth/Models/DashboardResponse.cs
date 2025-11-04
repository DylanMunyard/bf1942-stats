namespace api.Auth.Models;

public class DashboardResponse
{
    public List<OnlineBuddyResponse> OnlineBuddies { get; set; } = [];
    public List<OfflineBuddyResponse> OfflineBuddies { get; set; } = [];
    public List<FavoriteServerStatusResponse> FavoriteServers { get; set; } = [];
}
