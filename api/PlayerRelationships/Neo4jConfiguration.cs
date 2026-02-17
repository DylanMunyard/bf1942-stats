namespace api.PlayerRelationships;

public class Neo4jConfiguration
{
    public string Uri { get; set; } = "bolt://localhost:7687";
    public string Username { get; set; } = "neo4j";
    public string Password { get; set; } = "bf1942stats";
    public string Database { get; set; } = "neo4j";
}
