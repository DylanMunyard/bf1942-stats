using NodaTime;

namespace api.Data.Entities;

public class AdminPin
{
    public int Id { get; set; }
    public required string PinHash { get; set; }
    public Instant CreatedAt { get; set; }
    public Instant? LastUsedAt { get; set; }
}
