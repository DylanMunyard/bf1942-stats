using System.Text.Json;
using System.Text.Json.Serialization;
using NodaTime;
using NodaTime.Text;

namespace api.Serialization;

/// <summary>
/// Treats empty or whitespace strings as null when deserializing nullable NodaTime Instant.
/// Allows the UI to send "" for optional date filter fields (e.g. startDate, endDate) instead of omitting them.
/// </summary>
public sealed class NullableInstantEmptyAsNullJsonConverter : JsonConverter<Instant?>
{
    private static readonly InstantPattern Pattern = InstantPattern.ExtendedIso;

    public override Instant? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;
            var result = Pattern.Parse(s);
            return result.Success ? result.Value : null;
        }
        throw new JsonException($"Unexpected token {reader.TokenType} when deserializing nullable Instant. Expected string or null.");
    }

    public override void Write(Utf8JsonWriter writer, Instant? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteStringValue(Pattern.Format(value.Value));
        else
            writer.WriteNullValue();
    }
}
