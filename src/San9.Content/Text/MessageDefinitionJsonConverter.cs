using System.Text.Json;
using System.Text.Json.Serialization;

namespace San9.Content.Text;

// A message has one ordered node sequence; its JSON value is that sequence directly.
public sealed class MessageDefinitionJsonConverter : JsonConverter<MessageDefinition>
{
    public override MessageDefinition Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        var nodes = JsonSerializer.Deserialize<MessageNode[]>(ref reader, options)
            ?? throw new JsonException("Message nodes must be an array.");
        if (nodes.Any(node => node is null)) throw new JsonException("Message nodes cannot contain null.");
        return new(nodes);
    }

    public override void Write(Utf8JsonWriter writer, MessageDefinition value, JsonSerializerOptions options)
    {
        if (value.Nodes is null || value.Nodes.Any(node => node is null))
            throw new JsonException("Message nodes must be an array of nodes.");
        JsonSerializer.Serialize(writer, value.Nodes, options);
    }
}
