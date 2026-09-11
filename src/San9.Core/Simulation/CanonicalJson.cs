using System.Buffers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace San9.Core.Simulation;

/// <summary>Versioned canonical encoding for integer-only authoritative data.</summary>
public static class CanonicalJson
{
    public const int Version = 1;
    public static JsonSerializerOptions Options { get; } = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public static byte[] Encode<T>(T value, JsonSerializerOptions? options = null) =>
        Encode(JsonSerializer.SerializeToElement(value, options ?? Options));

    public static byte[] Encode(JsonElement value)
    {
        var buffer = new ArrayBufferWriter<byte>();
        using var writer = new Utf8JsonWriter(buffer);
        Write(writer, value);
        writer.Flush();
        return buffer.WrittenSpan.ToArray();
    }

    public static string Hash<T>(T value) => Convert.ToHexString(SHA256.HashData(Encode(value)));
    public static string Text<T>(T value) => Encoding.UTF8.GetString(Encode(value));

    private static void Write(Utf8JsonWriter writer, JsonElement value)
    {
        switch (value.ValueKind)
        {
            case JsonValueKind.Object:
                writer.WriteStartObject();
                var names = new HashSet<string>(StringComparer.Ordinal);
                foreach (var property in value.EnumerateObject().OrderBy(p => p.Name, StringComparer.Ordinal))
                {
                    if (!names.Add(property.Name)) throw new JsonException("Duplicate object property.");
                    writer.WritePropertyName(property.Name);
                    Write(writer, property.Value);
                }
                writer.WriteEndObject();
                break;
            case JsonValueKind.Array:
                writer.WriteStartArray();
                foreach (var item in value.EnumerateArray()) Write(writer, item);
                writer.WriteEndArray();
                break;
            case JsonValueKind.Number:
                if (value.TryGetInt64(out long signed)) writer.WriteNumberValue(signed);
                else if (value.TryGetUInt64(out ulong unsigned)) writer.WriteNumberValue(unsigned);
                else throw new JsonException("Authoritative numbers must be 64-bit integers.");
                break;
            case JsonValueKind.String: writer.WriteStringValue(value.GetString()); break;
            case JsonValueKind.True: writer.WriteBooleanValue(true); break;
            case JsonValueKind.False: writer.WriteBooleanValue(false); break;
            case JsonValueKind.Null: writer.WriteNullValue(); break;
            default: throw new JsonException("Undefined authoritative value.");
        }
    }
}
