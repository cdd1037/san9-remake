using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using San9.Core.Simulation;

namespace San9.Application.Persistence;

public static class StrictJson
{
    public static JsonSerializerOptions Options { get; } = CreateOptions();

    private static JsonSerializerOptions CreateOptions()
    {
        var resolver = new DefaultJsonTypeInfoResolver();
        resolver.Modifiers.Add(type =>
        {
            if (type.Kind != JsonTypeInfoKind.Object) return;
            for (int i = type.Properties.Count - 1; i >= 0; i--)
                if (type.Properties[i].Get is null && type.Properties[i].Set is null) type.Properties.RemoveAt(i);
            foreach (var property in type.Properties)
                if (property.Get is not null) property.IsRequired = true;
        });
        var options = new JsonSerializerOptions(CanonicalJson.Options)
        {
            TypeInfoResolver = resolver, UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
            RespectNullableAnnotations = true, RespectRequiredConstructorParameters = true,
            AllowOutOfOrderMetadataProperties = true
        };
        options.Converters.Add(new DefinedEnumConverterFactory());
        return options;
    }

    public static T Deserialize<T>(ReadOnlyMemory<byte> bytes)
    {
        using var document = JsonDocument.Parse(bytes);
        return Deserialize<T>(document.RootElement);
    }

    public static T Deserialize<T>(Stream stream)
    {
        using var document = JsonDocument.Parse(stream);
        return Deserialize<T>(document.RootElement);
    }

    public static T Deserialize<T>(JsonElement element)
    {
        Validate(element);
        return JsonSerializer.Deserialize<T>(element, Options) ?? throw new JsonException("Null document.");
    }

    private static void Validate(JsonElement value)
    {
        if (value.ValueKind == JsonValueKind.Object)
        {
            var names = new HashSet<string>(StringComparer.Ordinal);
            foreach (var property in value.EnumerateObject())
            {
                if (!names.Add(property.Name)) throw new JsonException("Duplicate object property.");
                Validate(property.Value);
            }
        }
        else if (value.ValueKind == JsonValueKind.Array)
        {
            foreach (var item in value.EnumerateArray())
            {
                if (item.ValueKind == JsonValueKind.Null) throw new JsonException("Null array element.");
                Validate(item);
            }
        }
    }

    private sealed class DefinedEnumConverterFactory : JsonConverterFactory
    {
        public override bool CanConvert(Type typeToConvert) => typeToConvert.IsEnum;
        public override JsonConverter CreateConverter(Type typeToConvert, JsonSerializerOptions options) =>
            (JsonConverter)Activator.CreateInstance(typeof(DefinedEnumConverter<>).MakeGenericType(typeToConvert))!;
    }

    private sealed class DefinedEnumConverter<T> : JsonConverter<T> where T : struct, Enum
    {
        private static readonly bool IsFlags = typeof(T).IsDefined(typeof(FlagsAttribute), false);
        private static readonly int KnownBits = Enum.GetValues<T>().Aggregate(0, (bits, value) =>
            bits | Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        private static bool IsValid(T value) => IsFlags
            ? (Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture) & ~KnownBits) == 0
            : Enum.IsDefined(value);

        public override T Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.Number || !reader.TryGetInt32(out int number)) throw new JsonException("Expected integer enum.");
            var value = (T)Enum.ToObject(typeof(T), number);
            return IsValid(value) ? value : throw new JsonException("Unknown enum value.");
        }
        public override void Write(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (!IsValid(value)) throw new JsonException("Unknown enum value.");
            writer.WriteNumberValue(Convert.ToInt32(value, System.Globalization.CultureInfo.InvariantCulture));
        }
    }
}
