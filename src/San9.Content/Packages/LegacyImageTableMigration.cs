using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Images;

namespace San9.Content.Packages;

/// <summary>Converts inline image files in a legacy manifest to a document-local file table.</summary>
public static class LegacyImageTableMigration
{
    /// <summary>Prepares schema 5 image and portrait data for further migration.</summary>
    public static JsonObject PrepareTextureV5(JsonObject source)
    {
        if (source["schemaVersion"]?.GetValue<int>() != 5)
            throw new InvalidDataException("Expected texture schema 5.");
        var document = Convert(source);
        foreach (var entry in document["portraits"]?.AsArray() ?? throw new InvalidDataException("Missing portraits."))
        {
            var portrait = entry?.AsObject() ?? throw new InvalidDataException("Missing portrait.");
            var bust = new PortraitAnchor(portrait["anchorX"]?.GetValue<double>() ?? throw new InvalidDataException("Missing portrait anchor."),
                portrait["anchorY"]?.GetValue<double>() ?? throw new InvalidDataException("Missing portrait anchor."));
            bust.Validate(PortraitView.Bust);
            var face = PortraitGeometry.FaceFromBust(bust);
            face.Validate(PortraitView.Face);
            portrait.Remove("anchorX");
            portrait.Remove("anchorY");
            portrait["bustAnchor"] = JsonSerializer.SerializeToNode(bust, PackageFiles.JsonOptions);
            portrait["faceAnchor"] = JsonSerializer.SerializeToNode(face, PackageFiles.JsonOptions);
        }
        // A legacy timestamp does not establish the original source file's digest.
        document["sources"] = new JsonArray();
        var units = document["map"]?["units"]?.AsObject() ?? throw new InvalidDataException("Missing map unit catalog.");
        if (!units.ContainsKey("formationAppearance")) units["formationAppearance"] = new JsonArray();
        // Later steps must migrate the skin and other version-specific resources.
        // Keep the old schema so this intermediate cannot be loaded as a finished package.
        return document;
    }

    public static JsonObject Convert(JsonObject source)
    {
        ArgumentNullException.ThrowIfNull(source);
        var result = (JsonObject)source.DeepClone();
        if (result.ContainsKey("files")) throw new InvalidDataException("The manifest already has a file table.");
        var files = new List<ImageFile>();
        var indices = new Dictionary<string, int>(StringComparer.Ordinal);
        Visit(result);
        result["files"] = JsonSerializer.SerializeToNode(files, PackageFiles.JsonOptions);
        return result;

        void Visit(JsonNode? node)
        {
            if (node is JsonArray array)
            {
                foreach (var child in array) Visit(child);
                return;
            }
            if (node is not JsonObject obj) return;
            if (obj["file"] is JsonObject file && obj.ContainsKey("id") && obj.ContainsKey("x") &&
                obj.ContainsKey("y") && obj.ContainsKey("width") && obj.ContainsKey("height"))
            {
                var image = obj.Deserialize<ImageAsset>(PackageFiles.JsonOptions)
                    ?? throw new InvalidDataException("Missing legacy image definition.");
                image.Validate();
                _ = PackageFiles.Resolve(".", image.File.Path);
                if (indices.TryGetValue(image.File.Path, out int index))
                {
                    if (files[index] != image.File) throw new InvalidDataException($"Conflicting physical image metadata: {image.File.Path}");
                }
                else
                {
                    index = files.Count;
                    indices.Add(image.File.Path, index);
                    files.Add(image.File);
                }
                obj["file"] = index;
            }
            foreach (var pair in obj.ToArray()) Visit(pair.Value);
        }
    }
}
