namespace api.Data.Entities;

/// <summary>
/// Key-value store for app-level data (e.g. site_notice for the notice banner).
/// </summary>
public class AppData
{
    public required string Id { get; set; }
    public required string Value { get; set; }
    public DateTime UpdatedAt { get; set; }
}
