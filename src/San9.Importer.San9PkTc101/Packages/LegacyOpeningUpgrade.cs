using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Authoring;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Interface;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Prepares opening metadata and officer flags from a converted legacy package.</summary>
public static class LegacyOpeningUpgrade
{
    public sealed record Prepared(OpeningContent Opening, OfficerFlagCatalog Flags);

    public static Prepared Prepare(JsonObject source, ScenarioInitialState baseline, TexturePackage textures,
        IReadOnlyDictionary<string, MessageDefinition> messages, IReadOnlyCollection<ScenarioPreviewEntry> previews,
        string resourceRoot)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(baseline);
        ArgumentNullException.ThrowIfNull(textures);
        if (source.ContainsKey("schemaVersion")) throw new InvalidDataException("Expected legacy opening metadata.");
        var legacy = (JsonObject)source.DeepClone();
        var forces = legacy["selection"]?["forces"]?.AsObject() ?? throw new InvalidDataException("Missing force selection metadata.");
        string Asset(string archive, int image) => textures.Library.Archives.Single(a => a.Id == archive)
            .Variants.Single(v => v.ImageIndex == image).Image.Id;
        StartMenuSprite Start(int image, int x, int y, int columns = 1) => new(Asset("g_start", image), x, y, columns);
        StartMenuSprite Selection(int image, int x, int y) => new(Asset("g_start2", image), x, y);
        if (!messages.TryGetValue("messages.7512", out var prompt)) throw new InvalidDataException("Missing force selection prompt.");
        forces["joinForcePrompt"] = MessageMarkup.Static(prompt.Nodes);
        forces["createForces"] = JsonSerializer.SerializeToNode(Start(29, 0, 264, 4), PackageFiles.JsonOptions);
        forces["deployOfficers"] = JsonSerializer.SerializeToNode(Start(31, 0, 348, 4), PackageFiles.JsonOptions);
        forces["cancelDeployment"] = JsonSerializer.SerializeToNode(Start(32, 0, 390, 4), PackageFiles.JsonOptions);
        var newForces = new NewForcePresentation([Start(38, 448, 48), new(Asset("g_strtpk", 2), 0, 0), Start(21, 0, 640)],
            Enumerable.Range(0, 8).Select(i =>
            {
                int x = 532 * (i / 4), y = 140 * (i % 4);
                return new NewForceSlotPresentation(new(Asset("g_start2", 4), x + 74, y + 87, 4),
                    new(Asset("g_start2", 5), x + 74, y + 127, 4), new(Asset("g_start2", 6), x + 74, y + 167, 4),
                    Selection(7, x + 320, y + 82), new(x + 58, y + 78), new(x + 166, y + 102),
                    new(x + 166, y + 142), new(x + 353, y + 103));
            }).ToArray(), Start(25, 744, 672, 4), Start(26, 856, 672, 4));
        forces["newForces"] = JsonSerializer.SerializeToNode(newForces, PackageFiles.JsonOptions);
        forces["mapSkin"] = JsonSerializer.SerializeToNode(ForceSelectionSkinCompiler.Compile(textures.Library), PackageFiles.JsonOptions);
        var document = LegacyImageTableMigration.Convert(legacy);
        document["schemaVersion"] = OpeningContent.CurrentSchemaVersion;
        var opening = document.Deserialize<OpeningContent>(PackageFiles.JsonOptions)
            ?? throw new InvalidDataException("Missing opening metadata.");
        opening.Audio.Validate(resourceRoot);
        opening.Menu.Validate(textures.Assets);
        (opening.Selection with { Entries = previews.ToArray() }).Validate(textures.Assets);
        Dictionary<string, int> surnames = new(StringComparer.Ordinal);
        foreach (var person in baseline.Officers)
            if (person.FlagGlyph is >= 0 and < 186 && Rune.TryGetRuneAt(person.State.Surname, 0, out var initial))
                surnames.TryAdd(initial.ToString(), person.FlagGlyph);
        var flags = new OfficerFlagCatalog(OfficerFlagCatalog.CurrentSchemaVersion, surnames, Enumerable.Range(0, 186).ToArray());
        flags.ValidateReferences(textures.Map.Clips.Select(clip => clip.Id).ToHashSet(StringComparer.Ordinal));
        return new(opening, flags);
    }
}
