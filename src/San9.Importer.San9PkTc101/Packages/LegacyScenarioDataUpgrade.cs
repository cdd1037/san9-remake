using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Packages;

/// <summary>Prepares shared scenario data from an explicitly selected legacy baseline.</summary>
public static class LegacyScenarioDataUpgrade
{
    public sealed record World(string Package, ScenarioInitialState Scenario, TextCatalog Text);
    public sealed record Prepared(string SourceRoot, SharedScenarioData Shared, IReadOnlyList<World> Worlds);

    /// <summary>Publishes the prepared worlds into a separate staged content directory.</summary>
    public static async Task PublishWorldsAsync(Prepared prepared, string outputRoot, CancellationToken token = default)
    {
        ArgumentNullException.ThrowIfNull(prepared);
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(prepared.SourceRoot));
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputRoot));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (string.Equals(source, output, comparison) ||
            output.StartsWith(source + Path.DirectorySeparatorChar, comparison) ||
            source.StartsWith(output + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Source and output must be separate directories.");
        await ScenarioContentFiles.WriteWorldsAsync(output, prepared.Shared,
            prepared.Worlds.Select(world => (world.Package, world.Scenario, world.Text)).ToArray(), token);
        using var publication = new ContentPublication();
        foreach (var world in prepared.Worlds)
        {
            var manifest = await ScenarioContentFiles.ReadManifestAsync(PackageFiles.Resolve(output, world.Package), token);
            await publication.StageAsync(PackageFiles.Resolve(output, manifest.Text),
                new StaticTextContent(world.Text.SchemaVersion, world.Text.Entries)
                { MessagePeople = world.Text.MessagePeople, MessageTags = world.Text.MessageTags },
                ScenarioContentFiles.DataJsonOptions, token);
        }
        publication.Commit(token);
    }

    public static async Task<Prepared> PrepareAsync(string root, string baselineId,
        IReadOnlyDictionary<string, bool> showOfficerAges, CancellationToken token = default)
    {
        root = Path.GetFullPath(root);
        var catalog = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(root, "catalog.json"), System.Text.Encoding.UTF8, token))?.AsObject()
            ?? throw new InvalidDataException("Missing legacy content catalog.");
        if (catalog["schemaVersion"]?.GetValue<int>() != 4)
            throw new InvalidDataException("Expected legacy content catalog schema 4.");
        var scenarios = catalog["scenarios"]?.AsArray() ?? throw new InvalidDataException("Missing legacy scenarios.");
        var worlds = new List<World>();
        var messagesByPath = new Dictionary<string, Dictionary<string, MessageDefinition>>(StringComparer.Ordinal);
        foreach (var entry in scenarios)
        {
            token.ThrowIfCancellationRequested();
            string id = entry?["id"]?.GetValue<string>() ?? throw new InvalidDataException("Missing scenario ID.");
            string package = entry?["package"]?.GetValue<string>() ?? throw new InvalidDataException("Missing scenario package.");
            if (!showOfficerAges.TryGetValue(id, out bool showAge))
                throw new InvalidDataException($"Missing explicit age-display decision for scenario {id}.");
            string directory = PackageFiles.Resolve(root, package);
            var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(directory, "content.json"), System.Text.Encoding.UTF8, token))?.AsObject()
                ?? throw new InvalidDataException("Missing scenario manifest.");
            if (manifest["schemaVersion"]?.GetValue<int>() != 243)
                throw new InvalidDataException($"Unsupported legacy scenario package: {id}.");
            string statePath = PackageFiles.Resolve(root, manifest["scenario"]?.GetValue<string>() ?? "");
            string textPath = PackageFiles.Resolve(root, manifest["text"]?.GetValue<string>() ?? "");
            string messagePath = manifest["sharedMessages"]?.GetValue<string>() ?? throw new InvalidDataException("Missing shared message library.");
            if (!messagesByPath.TryGetValue(messagePath, out var messages))
            {
                messages = await MessageLibraryFiles.LoadAsync(PackageFiles.Resolve(root, messagePath), token);
                messagesByPath.Add(messagePath, messages);
            }
            var state = JsonNode.Parse(await File.ReadAllTextAsync(statePath, System.Text.Encoding.UTF8, token))?.AsObject()
                ?? throw new InvalidDataException("Missing scenario state.");
            var rawText = JsonNode.Parse(await File.ReadAllTextAsync(textPath, System.Text.Encoding.UTF8, token))?.AsObject()
                ?? throw new InvalidDataException("Missing scenario text.");
            var entries = rawText["entries"]?.Deserialize<Dictionary<string, string>>(PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Missing text entries.");
            Dictionary<string, string> remakeEntries = new(StringComparer.Ordinal);
            CommandText.AddRemakeEntries(remakeEntries);
            foreach (var (key, value) in remakeEntries) entries.TryAdd(key, value);
            // This role was added after schema 243; the legacy package has no corresponding source text.
            entries.TryAdd(CommandText.Key("policy.general"), "一般");
            string illustrationPath = PackageFiles.Resolve(root, manifest["illustrations"]?.GetValue<string>() ?? "");
            var illustrations = JsonSerializer.Deserialize<Dictionary<string, string>>(
                await File.ReadAllTextAsync(illustrationPath, System.Text.Encoding.UTF8, token), PackageFiles.JsonOptions)
                ?? throw new InvalidDataException("Missing legacy illustrations.");
            var people = rawText["messagePeople"]?.Deserialize<Dictionary<int, MessagePersonProfile>>(PackageFiles.JsonOptions) ?? [];
            foreach (var (personId, profile) in people.ToArray())
            {
                if (profile.PortraitKey is null) continue;
                if (!illustrations.TryGetValue($"information-person-{personId}", out string? portrait))
                    throw new InvalidDataException($"Missing legacy portrait for officer {personId}.");
                people[personId] = profile with { PortraitKey = portrait };
            }
            var text = new TextCatalog(TextCatalog.CurrentSchemaVersion, entries)
            {
                Messages = messages,
                MessagePeople = people,
                MessageTags = rawText["messageTags"]?.Deserialize<string[]>(PackageFiles.JsonOptions) ?? []
            };
            var scenario = LegacyScenarioStateUpgrade.Convert(state, rawText, showAge);
            var parts = package.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries);
            scenario = LegacyHistoricalPeerageEvidence.Apply(scenario, text,
                parts.Length >= 2 && parts[0] == "scenarios" && parts[1] == "historical");
            scenario = HistoricalPeerageContent.Apply(scenario,
                parts.Length >= 2 && parts[0] == "scenarios" && parts[1] == "historical");
            scenario = CuratedFormationEvidence.Apply(scenario, text);
            scenario = scenario with { AppearanceEvidence = scenario.Officers.Where(p => p.State.IsServing && p.State.OwnerId is not null)
                .Select(p => (Person: p.State, Force: scenario.Forces.SingleOrDefault(f => f.Id == p.State.OwnerId)))
                .Where(p => p.Force?.LeaderId is not null)
                .Select(p => new ScenarioAppearanceEvidence(p.Person.Id, p.Force!.LeaderId!.Value,
                    p.Person.UnitId is int unit ? scenario.Units.SingleOrDefault(u => u.Id == unit)?.ServicePlaceId : p.Person.SettlementId,
                    scenario.ScenarioId, scenario.Year)).ToArray() };
            if (scenario.ScenarioId != id || worlds.Any(w => w.Scenario.ScenarioId == id))
                throw new InvalidDataException($"Duplicate or mismatched scenario: {id}.");
            worlds.Add(new(package, scenario, text));
        }
        var baseline = worlds.SingleOrDefault(w => w.Scenario.ScenarioId == baselineId)
            ?? throw new InvalidDataException("Missing baseline scenario.");
        var shared = SharedScenarioData.From(baseline.Scenario, "shared/definitions.json",
            "shared/officer-defaults.json", "shared/maps/china/geography.json", baseline.Text);
        foreach (var world in worlds) _ = shared.Describe(world.Scenario, world.Text);
        return new(root, shared, worlds);
    }
}
