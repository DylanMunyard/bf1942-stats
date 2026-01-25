using NodaTime;

namespace api.Data.Entities;

public class AdminAuditLog
{
    public long Id { get; set; }
    public required string Action { get; set; }      // "delete_round"
    public required string TargetType { get; set; }  // "Round"
    public required string TargetId { get; set; }    // RoundId
    public string? Details { get; set; }             // JSON with counts
    public required string AdminEmail { get; set; }
    public Instant Timestamp { get; set; }
}
