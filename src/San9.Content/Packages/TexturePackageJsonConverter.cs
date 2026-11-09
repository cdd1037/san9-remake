using System.Text.Json;
using System.Text.Json.Serialization;
using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Packages;

public sealed class TexturePackageJsonConverter : JsonConverter<TexturePackage>
{
    private sealed record Manifest(int SchemaVersion, int ConversionVersion, string SourceBuild, TextureSourceStamp[] Sources,
        ImageFile[] Files, MapResources Map, InterfaceSkin Skin, TextureLibrary Library, PortraitDefinition[] Portraits);

    public override TexturePackage Read(ref Utf8JsonReader reader, Type type, JsonSerializerOptions options)
    {
        using var document = JsonDocument.ParseValue(ref reader);
        return ReadManifest(document.RootElement, options);
    }

    internal static TexturePackage ReadManifest(JsonElement document, JsonSerializerOptions options)
    {
        if (!document.TryGetProperty("schemaVersion", out var version) ||
            !version.TryGetInt32(out int number) || number != TexturePackage.CurrentSchemaVersion)
            throw new InvalidDataException("Unsupported texture package version; rebuild the package.");
        var table = ImageFileTable.Read(document, options);
        var manifest = document.Deserialize<Manifest>(table.Options(options))
            ?? throw new JsonException("Empty texture package.");
        ContentStructure.Validate(manifest);
        return new(manifest.SchemaVersion, manifest.ConversionVersion, manifest.SourceBuild, manifest.Sources,
            manifest.Map, manifest.Skin, manifest.Library, manifest.Portraits);
    }

    public override void Write(Utf8JsonWriter writer, TexturePackage package, JsonSerializerOptions options)
    {
        var table = ImageFileTable.FromAssets(package.Assets);
        JsonSerializer.Serialize(writer, new Manifest(package.SchemaVersion, package.ConversionVersion, package.SourceBuild,
            package.Sources, table.Files, package.Map, package.Skin, package.Library, package.Portraits), table.Options(options));
    }
}
