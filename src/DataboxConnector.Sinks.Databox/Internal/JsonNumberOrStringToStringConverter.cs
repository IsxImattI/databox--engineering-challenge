using System.Text.Json;
using System.Text.Json.Serialization;

namespace DataboxConnector.Sinks.Databox.Internal;

/// <summary>
/// JSON converter that reads either a string or a number and exposes it as a string.
/// </summary>
/// <remarks>
/// Databox's API returns identifier fields as numbers in some endpoints
/// (e.g. <c>{"id": 12345}</c>) and as strings in others. Modelling them as
/// <c>string</c> with this converter makes the C# side uniform regardless
/// of the wire format.
/// </remarks>
internal sealed class JsonNumberOrStringToStringConverter : JsonConverter<string?>
{
    public override string? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        return reader.TokenType switch
        {
            JsonTokenType.Null   => null,
            JsonTokenType.String => reader.GetString(),
            JsonTokenType.Number => reader.TryGetInt64(out var l)
                                        ? l.ToString(System.Globalization.CultureInfo.InvariantCulture)
                                        : reader.GetDouble().ToString(System.Globalization.CultureInfo.InvariantCulture),
            _ => throw new JsonException($"Cannot convert token {reader.TokenType} to string.")
        };
    }

    public override void Write(Utf8JsonWriter writer, string? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteStringValue(value);
    }
}