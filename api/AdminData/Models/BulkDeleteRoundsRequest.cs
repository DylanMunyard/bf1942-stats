using System.Text.Json.Serialization;

namespace api.AdminData.Models;

public record BulkDeleteRoundsRequest(
    [property: JsonPropertyName("roundIds")] IReadOnlyList<string> RoundIds
);
