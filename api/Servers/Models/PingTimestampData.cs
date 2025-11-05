namespace api.ServerStats.Models;

// Helper class for raw SQL query results
public class PingTimestampData
{
    public DateTime Timestamp { get; set; }
    public int Ping { get; set; }
}
