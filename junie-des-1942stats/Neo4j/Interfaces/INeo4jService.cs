using junie_des_1942stats.Neo4j.Models;

namespace junie_des_1942stats.Neo4j.Interfaces;

public interface INeo4jService
{
    // Connection management
    Task TestConnectionAsync();
    Task InitializeConstraintsAsync();
    
    // Data synchronization
    Task SyncLastMonthDataAsync();
    Task ClearAllDataAsync();
    
    // Node operations
    Task CreatePlayerNodeAsync(PlayerNode player);
    Task CreateServerNodeAsync(ServerNode server);
    Task CreateMapNodeAsync(MapNode map);
    Task CreateSessionNodeAsync(SessionNode session);
    Task CreateGeographicRegionNodeAsync(GeographicRegionNode region);
    Task CreateTimeSlotNodeAsync(TimeSlotNode timeSlot);
    
    // Relationship operations
    Task CreatePlayedWithRelationshipAsync(string player1, string player2, PlayedWithRelationship relationship);
    Task CreateFrequentsRelationshipAsync(string playerName, string serverGuid, FrequentsRelationship relationship);
    Task CreatePrefersMapRelationshipAsync(string playerName, string mapName, PrefersMapRelationship relationship);
    Task CreatePerformsBestInRelationshipAsync(string playerName, TimeSlotNode timeSlot, PerformsBestInRelationship relationship);
    
    // Analytics queries
    Task<List<PlayerCommunityResult>> GetServerCommunitiesAsync();
    Task<List<PlayerSimilarityResult>> GetSimilarPlayersAsync();
    Task<List<GeographicBattleResult>> GetCrossBorderBattlesAsync();
    Task<List<MapMetaResult>> GetMapCompetitivenessAsync();
    Task<Dictionary<string, object>> GetPlayerNetworkStatsAsync(string playerName);
    Task<List<string>> GetPlayerRecommendationsAsync(string playerName);
}