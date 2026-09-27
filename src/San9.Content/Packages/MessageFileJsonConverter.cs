using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Content.Text;

namespace San9.Content.Packages;

/// <summary>Compact leaf values while keeping message and control-flow boundaries visible.</summary>
internal sealed class MessageFileJsonConverter : JsonConverter<Dictionary<string, MessageDefinition>>
{
    public override Dictionary<string, MessageDefinition> Read(ref Utf8JsonReader reader, Type type,
        JsonSerializerOptions options) => JsonSerializer.Deserialize<Dictionary<string, MessageDefinition>>(
            ref reader, ScenarioContentFiles.DataJsonOptions) ?? throw new JsonException("Missing messages.");

    public override void Write(Utf8JsonWriter writer, Dictionary<string, MessageDefinition> messages,
        JsonSerializerOptions options)
    {
        writer.WriteStartObject();
        foreach (var (key, message) in messages)
        {
            writer.WritePropertyName(key);
            WriteValue(writer, JsonSerializer.SerializeToElement(message, ScenarioContentFiles.DataJsonOptions));
        }
        writer.WriteEndObject();
    }

    private static void WriteValue(Utf8JsonWriter writer, JsonElement value)
    {
        if (CanInline(value))
        {
            writer.WriteRawValue(value.GetRawText());
            return;
        }
        if (value.ValueKind == JsonValueKind.Array)
        {
            writer.WriteStartArray();
            foreach (var item in value.EnumerateArray()) WriteValue(writer, item);
            writer.WriteEndArray();
        }
        else if (value.ValueKind == JsonValueKind.Object)
        {
            writer.WriteStartObject();
            foreach (var property in value.EnumerateObject())
            {
                writer.WritePropertyName(property.Name);
                WriteValue(writer, property.Value);
            }
            writer.WriteEndObject();
        }
        else value.WriteTo(writer);
    }

    private static bool CanInline(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Array)
        {
            var items = value.EnumerateArray().ToArray();
            if (items.All(item => item.ValueKind == JsonValueKind.String)) return true;
            return value.GetRawText().Length <= 160 &&
                items.All(item => item.ValueKind != JsonValueKind.Array && CanInline(item));
        }
        if (value.ValueKind == JsonValueKind.Object)
            return value.GetRawText().Length <= 160 &&
                !value.TryGetProperty("body", out _) && !value.TryGetProperty("choices", out _) &&
                value.EnumerateObject().All(property => CanInline(property.Value));
        return true;
    }
}
