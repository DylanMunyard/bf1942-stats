namespace junie_des_1942stats.Services;

public interface IPlayerEventPublisher
{
    Task PublishPlayerOnlineEvent(string playerName, string serverGuid, string serverName, string mapName, string gameType, int sessionId);
    Task PublishServerMapChangeEvent(string serverGuid, string serverName, string oldMapName, string newMapName, string gameType, string? joinLink);
}