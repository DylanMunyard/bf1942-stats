using System.Text.Json.Serialization;
using api.Serialization;
using NodaTime;

namespace api.AdminData.Models;

public record QuerySuspiciousSessionsRequest(
    string? ServerGuid = null,
    [property: JsonConverter(typeof(NullableInt32EmptyAsNullJsonConverter))] int? MinScore = null,
    [property: JsonConverter(typeof(NullableDoubleEmptyAsNullJsonConverter))] double? MinKdRatio = null,
    [property: JsonConverter(typeof(NullableInstantEmptyAsNullJsonConverter))] Instant? StartDate = null,
    [property: JsonConverter(typeof(NullableInstantEmptyAsNullJsonConverter))] Instant? EndDate = null,
    int Page = 1,
    int PageSize = 50
);
