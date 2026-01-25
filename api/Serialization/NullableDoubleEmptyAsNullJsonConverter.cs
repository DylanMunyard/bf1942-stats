using System.Text.Json;
using System.Text.Json.Serialization;

namespace api.Serialization;

/// <summary>
/// Treats empty or whitespace strings as null when deserializing nullable double.
/// Allows the UI to send "" for optional filter fields (e.g. minKdRatio) instead of omitting them.
/// </summary>
public sealed class NullableDoubleEmptyAsNullJsonConverter : JsonConverter<double?>
{
    public override double? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.Null)
            return null;
        if (reader.TokenType == JsonTokenType.String)
        {
            var s = reader.GetString();
            if (string.IsNullOrWhiteSpace(s))
                return null;
            return double.TryParse(s, out var v) ? v : null;
        }
        if (reader.TokenType == JsonTokenType.Number)
            return reader.TryGetDouble(out var n) ? n : null;
        throw new JsonException($"Unexpected token {reader.TokenType} when deserializing nullable double.");
    }

    public override void Write(Utf8JsonWriter writer, double? value, JsonSerializerOptions options)
    {
        if (value.HasValue)
            writer.WriteNumberValue(value.Value);
        else
            writer.WriteNullValue();
    }
}
