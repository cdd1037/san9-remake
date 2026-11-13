using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Images;
using San9.Content.Interface;
using San9.Content.Packages;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Rebuilds added skin roles from already converted TC101 library images.</summary>
public static class LegacyTextureSkinUpgrade
{
    public sealed record Prepared(JsonObject Manifest, IReadOnlyDictionary<string, byte[]> Images);

    /// <summary>Creates a validated texture package in a new output directory.</summary>
    public static async Task UpgradeAsync(string sourceRoot, string outputRoot, CancellationToken token = default)
    {
        sourceRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourceRoot));
        outputRoot = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(sourceRoot, outputRoot, comparison) ||
            outputRoot.StartsWith(sourceRoot + Path.DirectorySeparatorChar, comparison) ||
            sourceRoot.StartsWith(outputRoot + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Source and output must be separate directories.");
        if (Directory.Exists(outputRoot) || File.Exists(outputRoot)) throw new IOException("Output already exists.");
        var source = JsonNode.Parse(await File.ReadAllBytesAsync(Path.Combine(sourceRoot, "textures.json"), token))?.AsObject()
            ?? throw new InvalidDataException("Missing texture manifest.");
        var prepared = Prepare(source, sourceRoot);
        prepared.Manifest["schemaVersion"] = TexturePackage.CurrentSchemaVersion;
        var package = prepared.Manifest.Deserialize<TexturePackage>(PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("Missing upgraded texture package.");
        package.Validate();
        string parent = Path.GetDirectoryName(outputRoot) ?? throw new ArgumentException("Output requires a parent directory.");
        Directory.CreateDirectory(parent);
        string staging = Path.Combine(parent, ".legacy-textures-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(staging);
        try
        {
            foreach (var file in package.Assets.Select(a => a.File).Distinct())
            {
                token.ThrowIfCancellationRequested();
                string target = PackageFiles.Resolve(staging, file.Path);
                Directory.CreateDirectory(Path.GetDirectoryName(target)!);
                if (prepared.Images.TryGetValue(file.Path, out var bytes))
                    await File.WriteAllBytesAsync(target, bytes, token);
                else
                    File.Copy(PackageFiles.Resolve(sourceRoot, file.Path), target);
            }
            await ImageAssetFiles.VerifyAsync(staging, package.Assets, token);
            await File.WriteAllBytesAsync(Path.Combine(staging, "textures.json"),
                JsonSerializer.SerializeToUtf8Bytes(package, PackageFiles.JsonOptions), token);
            token.ThrowIfCancellationRequested();
            Directory.Move(staging, outputRoot);
        }
        finally
        {
            if (Directory.Exists(staging)) Directory.Delete(staging, recursive: true);
        }
    }

    public static Prepared Prepare(JsonObject source, string textureRoot)
    {
        ArgumentNullException.ThrowIfNull(source);
        var document = (JsonObject)source.DeepClone();
        var skin = document["skin"]?.AsObject() ?? throw new InvalidDataException("Missing skin.");
        if (document["schemaVersion"]?.GetValue<int>() != 5 || skin["schemaVersion"]?.GetValue<int>() != 62)
            throw new InvalidDataException("Expected texture schema 5 and skin schema 62.");
        var archives = document["library"]?["archives"]?.AsArray() ?? throw new InvalidDataException("Missing texture library.");
        var assets = skin["assets"]?.AsArray() ?? throw new InvalidDataException("Missing skin assets.");
        var roles = skin["roles"]?.AsObject() ?? throw new InvalidDataException("Missing skin roles.");
        var generated = new Dictionary<string, byte[]>(StringComparer.Ordinal);

        ImageAsset Find(string archiveId, int imageIndex, int paletteIndex)
        {
            var archive = archives.SingleOrDefault(a => a?["id"]?.GetValue<string>() == archiveId)
                ?? throw new InvalidDataException($"Missing texture archive: {archiveId}");
            var variants = archive["variants"]?.AsArray() ?? throw new InvalidDataException("Missing texture variants.");
            var variant = variants.SingleOrDefault(v => v?["imageIndex"]?.GetValue<int>() == imageIndex &&
                v?["paletteIndex"]?.GetValue<int>() == paletteIndex)
                ?? throw new InvalidDataException($"Missing texture variant: {archiveId}/{imageIndex}/{paletteIndex}");
            return variant["image"]?.Deserialize<ImageAsset>(PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Missing texture image.");
        }

        void Add(string role, ImageAsset original, int frame, int frames)
        {
            if (roles.ContainsKey(role) || original.Width % frames != 0)
                throw new InvalidDataException($"Invalid legacy skin role: {role}");
            var region = original with { X = original.X + frame * (original.Width / frames), Width = original.Width / frames };
            var image = ImageAssetFiles.Region(ImageAssetFiles.LoadFile(textureRoot, region.File), region);
            string path = $"images/legacy-skin/{role}.png";
            if (File.Exists(PackageFiles.Resolve(textureRoot, path)))
                throw new InvalidDataException($"Generated image collides with input: {path}");
            generated.Add(path, image.Encode());
            string id = "ui-" + role;
            assets.Add(JsonSerializer.SerializeToNode(new ImageAsset(id, path, image.Width, image.Height, image.Channels), PackageFiles.JsonOptions));
            roles.Add(role, id);
        }

        string[] parts = ["top-left", "top", "top-right", "left", "center", "right", "bottom-left", "bottom", "bottom-right"];
        for (int i = 0; i < parts.Length; i++) Add("map-object-picker-" + parts[i], Find("g_common", 475 + i, 65), 0, 1);
        string[] rowParts = ["left", "middle", "right"];
        for (int i = 0; i < rowParts.Length; i++)
        {
            var image = Find("g_common", 484 + i, 65);
            for (int frame = 0; frame < 4; frame++)
                Add("map-object-row-" + rowParts[i] + (frame == 0 ? "" : $"-{frame}"), image, frame, 4);
        }
        Add("icon-commanded", Find("g_marker", 33, 4), 0, 1);
        skin["schemaVersion"] = InterfaceSkin.CurrentSchemaVersion;
        return new(LegacyImageTableMigration.PrepareTextureV5(document), generated);
    }
}
