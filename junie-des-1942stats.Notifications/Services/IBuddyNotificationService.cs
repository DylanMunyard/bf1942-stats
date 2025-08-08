namespace junie_des_1942stats.Notifications.Services;

public interface IBuddyNotificationService
{
    Task<IEnumerable<string>> GetUserConnectionIds(int userId);
    Task AddUserConnection(int userId, string connectionId);
    Task RemoveUserConnection(int userId, string connectionId);
}