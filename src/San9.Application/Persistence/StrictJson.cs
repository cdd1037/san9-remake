using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.Json.Serialization.Metadata;
using System.Reflection;
using San9.Core.Domain;
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
            {
                bool omitsNull = property.AttributeProvider?.GetCustomAttributes(typeof(JsonIgnoreAttribute), true)
                    .OfType<JsonIgnoreAttribute>().Any(a => a.Condition == JsonIgnoreCondition.WhenWritingNull) == true;
                if (property.Get is not null && !omitsNull && !IsLegacyOptional(type.Type, property)) property.IsRequired = true;
            }
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

    private static bool IsLegacyOptional(Type type, JsonPropertyInfo property)
    {
        string? name = (property.AttributeProvider as MemberInfo)?.Name;
        return type == typeof(EquipmentInstance) && name is nameof(EquipmentInstance.LegacyTreasureId) or nameof(EquipmentInstance.IsLost) ||
            type == typeof(ManualOfficerAppearance) && name == nameof(ManualOfficerAppearance.KeepPendingIfForceUnavailable) ||
            type == typeof(San9.Content.Scenarios.InformationDefinitions) && name == nameof(San9.Content.Scenarios.InformationDefinitions.Equipment) ||
            type == typeof(San9.Content.Scenarios.InformationCatalog) && name == nameof(San9.Content.Scenarios.InformationCatalog.Equipment) ||
            type == typeof(WorldSnapshot) && name is nameof(WorldSnapshot.Equipment) or nameof(WorldSnapshot.EquipmentInitialized) ||
            type == typeof(OfficerState) && name == nameof(OfficerState.FormationProficiencies) ||
            type == typeof(NewGameSettings) && name == nameof(NewGameSettings.MixedTroopsEnabled) ||
            type == typeof(ForceState) && name is nameof(ForceState.CompletedResearch) or nameof(ForceState.ActiveResearch) or nameof(ForceState.EquipmentProduction) ||
            type == typeof(EquipmentDefinition) && name is nameof(EquipmentDefinition.Name) or nameof(EquipmentDefinition.ResearchProjectId) or nameof(EquipmentDefinition.ProductionCost) or nameof(EquipmentDefinition.ProductionDays) or nameof(EquipmentDefinition.Grade) or nameof(EquipmentDefinition.MinimumAbilities) ||
            type == typeof(UnitState) && name is nameof(UnitState.Detachments) or nameof(UnitState.NavalTrainingId) or nameof(UnitState.SiegeEquipmentId) ||
            type == typeof(DeploymentPreset) && name is nameof(DeploymentPreset.NavalTrainingId) or nameof(DeploymentPreset.SiegeEquipmentId) ||
            type == typeof(DeploymentPresetMember) && name == nameof(DeploymentPresetMember.Detachment);
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

        public override T ReadAsPropertyName(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            string? key = reader.GetString();
            if (!Enum.TryParse<T>(key, out var value) || !IsValid(value) || value.ToString() != key) throw new JsonException("Unknown enum dictionary key.");
            return value;
        }

        public override void WriteAsPropertyName(Utf8JsonWriter writer, T value, JsonSerializerOptions options)
        {
            if (!IsValid(value)) throw new JsonException("Unknown enum dictionary key.");
            writer.WritePropertyName(value.ToString());
        }
    }
}
