using System.Text.Json;
using System.Text.Json.Serialization;

namespace San9.Content.Text;

public sealed class MessageNodeJsonConverter : JsonConverter<MessageNode>
{
    public override MessageNode Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        if (reader.TokenType == JsonTokenType.String) return new MessageLiteral(reader.GetString()!);
        using var document = JsonDocument.ParseValue(ref reader);
        var node = document.RootElement;
        if (node.ValueKind != JsonValueKind.Object || !node.TryGetProperty("kind", out var kind) || kind.ValueKind != JsonValueKind.String)
            throw new JsonException("Message node must be a string or a control object with a kind.");
        Type type = kind.GetString() switch
        {
            "value" => typeof(MessageValue),
            "reference" => typeof(MessageReference),
            "conditional" => typeof(MessageConditional),
            "choice" => typeof(MessageChoice),
            "random-choice" => typeof(MessageRandomChoice),
            "return" => typeof(MessageReturn),
            "styled" => typeof(MessageStyled),
            _ => throw new JsonException("Unknown message node kind.")
        };
        return (MessageNode)node.Deserialize(type, options)!;
    }

    public override void Write(Utf8JsonWriter writer, MessageNode value, JsonSerializerOptions options)
    {
        if (value is MessageLiteral literal)
        {
            if (literal.BbCode is null) throw new JsonException("Message text cannot be null.");
            writer.WriteStringValue(literal.BbCode);
            return;
        }
        string kind = value switch
        {
            MessageValue => "value",
            MessageReference => "reference",
            MessageConditional => "conditional",
            MessageChoice => "choice",
            MessageRandomChoice => "random-choice",
            MessageReturn => "return",
            MessageStyled => "styled",
            _ => throw new JsonException("Unknown message node type.")
        };
        var node = JsonSerializer.SerializeToElement(value, value.GetType(), options);
        writer.WriteStartObject();
        writer.WriteString("kind", kind);
        foreach (var property in node.EnumerateObject()) property.WriteTo(writer);
        writer.WriteEndObject();
    }
}
