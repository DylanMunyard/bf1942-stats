namespace junie_des_1942stats.Neo4j.Services;

public class Neo4jConfiguration
{
    public const string SectionName = "Neo4j";
    
    public string Uri { get; set; } = "bolt://localhost:7687";
    public string Username { get; set; } = "neo4j";
    public string Password { get; set; } = "password";
    public string Database { get; set; } = "neo4j";
    public bool Enabled { get; set; } = false;
    
    // Connection settings
    public int MaxConnectionPoolSize { get; set; } = 100;
    public TimeSpan ConnectionTimeout { get; set; } = TimeSpan.FromSeconds(30);
    public TimeSpan MaxIdleTime { get; set; } = TimeSpan.FromMinutes(10);
}