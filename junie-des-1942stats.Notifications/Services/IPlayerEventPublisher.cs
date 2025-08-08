namespace junie_des_1942stats.Notifications.Services;

public interface IPlayerEventPublisher
{
    Task PublishPlayerOnlineEvent(string playerName, string serverGuid, string serverName, string mapName, string gameType, int sessionId);
    Task PublishMapChangeEvent(string playerName, string serverGuid, string serverName, string oldMapName, string newMapName, int sessionId);
}