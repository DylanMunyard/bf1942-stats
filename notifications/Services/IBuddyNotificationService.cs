namespace notifications.Services;

public interface IBuddyNotificationService
{
    Task<IEnumerable<string>> GetUserConnectionIds(string userEmail);
    Task AddUserConnection(string userEmail, string connectionId);
    Task RemoveUserConnection(string userEmail, string connectionId);
}