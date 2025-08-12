using System.Text.Json.Serialization;

namespace junie_des_1942stats.Neo4j.Models;

public class PlayerNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("first_seen")]
    public DateTime FirstSeen { get; set; }
    
    [JsonPropertyName("last_seen")]
    public DateTime LastSeen { get; set; }
    
    [JsonPropertyName("total_playtime_minutes")]
    public int TotalPlayTimeMinutes { get; set; }
    
    [JsonPropertyName("ai_bot")]
    public bool AiBot { get; set; }
    
    [JsonPropertyName("avg_kd_ratio")]
    public double AvgKdRatio { get; set; }
}

public class ServerNode
{
    [JsonPropertyName("guid")]
    public string Guid { get; set; } = "";
    
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("ip")]
    public string Ip { get; set; } = "";
    
    [JsonPropertyName("port")]
    public int Port { get; set; }
    
    [JsonPropertyName("game_id")]
    public string GameId { get; set; } = "";
    
    [JsonPropertyName("max_players")]
    public int? MaxPlayers { get; set; }
    
    [JsonPropertyName("country")]
    public string? Country { get; set; }
    
    [JsonPropertyName("region")]
    public string? Region { get; set; }
    
    [JsonPropertyName("city")]
    public string? City { get; set; }
    
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

public class MapNode
{
    [JsonPropertyName("name")]
    public string Name { get; set; } = "";
    
    [JsonPropertyName("game_type")]
    public string GameType { get; set; } = "";
}

public class SessionNode
{
    [JsonPropertyName("session_id")]
    public int SessionId { get; set; }
    
    [JsonPropertyName("start_time")]
    public DateTime StartTime { get; set; }
    
    [JsonPropertyName("last_seen_time")]
    public DateTime LastSeenTime { get; set; }
    
    [JsonPropertyName("total_score")]
    public int TotalScore { get; set; }
    
    [JsonPropertyName("total_kills")]
    public int TotalKills { get; set; }
    
    [JsonPropertyName("total_deaths")]
    public int TotalDeaths { get; set; }
    
    [JsonPropertyName("duration_minutes")]
    public int DurationMinutes { get; set; }
    
    [JsonPropertyName("kd_ratio")]
    public double KdRatio { get; set; }
}

public class GeographicRegionNode
{
    [JsonPropertyName("country")]
    public string Country { get; set; } = "";
    
    [JsonPropertyName("region")]
    public string? Region { get; set; }
    
    [JsonPropertyName("timezone")]
    public string? Timezone { get; set; }
}

public class TimeSlotNode
{
    [JsonPropertyName("hour")]
    public int Hour { get; set; }
    
    [JsonPropertyName("day_of_week")]
    public int DayOfWeek { get; set; }
    
    [JsonPropertyName("date")]
    public DateTime Date { get; set; }
}

// Relationship models
public class PlayedWithRelationship
{
    [JsonPropertyName("sessions_together")]
    public int SessionsTogether { get; set; }
    
    [JsonPropertyName("first_played_together")]
    public DateTime FirstPlayedTogether { get; set; }
    
    [JsonPropertyName("last_played_together")]
    public DateTime LastPlayedTogether { get; set; }
    
    [JsonPropertyName("same_team_sessions")]
    public int SameTeamSessions { get; set; }
    
    [JsonPropertyName("opposing_team_sessions")]
    public int OpposingTeamSessions { get; set; }
}

public class FrequentsRelationship
{
    [JsonPropertyName("session_count")]
    public int SessionCount { get; set; }
    
    [JsonPropertyName("total_playtime_minutes")]
    public int TotalPlayTimeMinutes { get; set; }
    
    [JsonPropertyName("first_played")]
    public DateTime FirstPlayed { get; set; }
    
    [JsonPropertyName("last_played")]
    public DateTime LastPlayed { get; set; }
    
    [JsonPropertyName("avg_score")]
    public double AvgScore { get; set; }
    
    [JsonPropertyName("best_score")]
    public int BestScore { get; set; }
}

public class PrefersMapRelationship
{
    [JsonPropertyName("times_played")]
    public int TimesPlayed { get; set; }
    
    [JsonPropertyName("avg_performance")]
    public double AvgPerformance { get; set; }
    
    [JsonPropertyName("best_score")]
    public int BestScore { get; set; }
}

public class PerformsBestInRelationship
{
    [JsonPropertyName("session_count")]
    public int SessionCount { get; set; }
    
    [JsonPropertyName("avg_kd_ratio")]
    public double AvgKdRatio { get; set; }
    
    [JsonPropertyName("avg_score")]
    public double AvgScore { get; set; }
}

// Query result models
public class PlayerCommunityResult
{
    public string ServerName { get; set; } = "";
    public List<string> RegularPlayers { get; set; } = [];
}

public class PlayerSimilarityResult
{
    public string Player1 { get; set; } = "";
    public string Player2 { get; set; } = "";
    public double KdRatioDifference { get; set; }
    public bool HasPlayedTogether { get; set; }
}

public class GeographicBattleResult
{
    public string Country1 { get; set; } = "";
    public string Country2 { get; set; } = "";
    public int CrossBorderBattles { get; set; }
}

public class MapMetaResult
{
    public string MapName { get; set; } = "";
    public int BalancedMatches { get; set; }
    public double CompetitiveScore { get; set; }
}