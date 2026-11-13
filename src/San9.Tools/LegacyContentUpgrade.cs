using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using San9.Application.Diagnostics;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Tools;

internal static class LegacyContentUpgrade
{
    public static async Task RunAsync(string sourcePath, string outputPath, bool showOfficerAges)
    {
        string source = Path.TrimEndingDirectorySeparator(Path.GetFullPath(sourcePath));
        string output = Path.TrimEndingDirectorySeparator(Path.GetFullPath(outputPath));
        var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        if (source.Equals(output, comparison) || source.StartsWith(output + Path.DirectorySeparatorChar, comparison) ||
            output.StartsWith(source + Path.DirectorySeparatorChar, comparison))
            throw new ArgumentException("Source and output must be separate directories.");
        if (!Directory.Exists(source) || Directory.Exists(output) || File.Exists(output))
            throw new ArgumentException("Source must exist and output must not exist.");
        string parent = Path.GetDirectoryName(output) ?? throw new ArgumentException("Missing output parent.");
        Directory.CreateDirectory(parent);
        string stage = Path.Combine(parent, ".legacy-upgrade-" + Guid.NewGuid().ToString("N"));
        try
        {
            var catalog = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(source, "catalog.json"), Encoding.UTF8))!.AsObject();
            if (catalog["schemaVersion"]?.GetValue<int>() != 4) throw new InvalidDataException("Expected legacy catalog schema 4.");
            var entries = catalog["scenarios"]!.AsArray();
            var ages = entries.ToDictionary(e => e!["id"]!.GetValue<string>(), _ => showOfficerAges, StringComparer.Ordinal);
            string baseline = entries[0]!["id"]!.GetValue<string>();
            var prepared = await LegacyScenarioDataUpgrade.PrepareAsync(source, baseline, ages);
            string texturesPath = catalog["textures"]!.GetValue<string>();
            string oldTextures = PackageFiles.Resolve(source, texturesPath);
            CopyDirectory(source, stage, oldTextures);
            await LegacyTextureSkinUpgrade.UpgradeAsync(oldTextures, PackageFiles.Resolve(stage, texturesPath));
            var textures = await TexturePackageFiles.LoadAsync(PackageFiles.Resolve(stage, texturesPath));
            var previews = new List<ScenarioPreviewEntry>();
            string? messagesPath = null;
            foreach (var entry in entries)
            {
                string package = entry!["package"]!.GetValue<string>();
                var manifest = JsonNode.Parse(await File.ReadAllTextAsync(Path.Combine(PackageFiles.Resolve(source, package), "content.json"), Encoding.UTF8))!.AsObject();
                previews.Add(manifest["preview"]!.Deserialize<ScenarioPreviewEntry>(PackageFiles.JsonOptions)!);
                messagesPath ??= manifest["sharedMessages"]!.GetValue<string>();
                string presentationPath = PackageFiles.Resolve(stage, manifest["presentation"]!.GetValue<string>());
                var presentation = await PackageFiles.ReadAsync<CommandPresentation>(presentationPath);
                await WriteAsync(presentationPath, LegacyPresentationUpgrade.Prepare(presentation));
            }
            var messages = await MessageLibraryFiles.LoadAsync(PackageFiles.Resolve(source, messagesPath!));
            string openingPath = catalog["opening"]!.GetValue<string>();
            var oldOpening = JsonNode.Parse(await File.ReadAllTextAsync(PackageFiles.Resolve(source, openingPath), Encoding.UTF8))!.AsObject();
            var opening = LegacyOpeningUpgrade.Prepare(oldOpening, prepared.Worlds.Single(w => w.Scenario.ScenarioId == baseline).Scenario,
                textures, messages, previews, stage);
            await WriteAsync(PackageFiles.Resolve(stage, openingPath), opening.Opening);
            await WriteAsync(PackageFiles.Resolve(stage, "shared/officer-flags.json"), opening.Flags);
            catalog["schemaVersion"] = ContentCatalog.CurrentSchemaVersion;
            catalog["officerFlags"] = "shared/officer-flags.json";
            catalog["definitions"] = "shared/definitions.json";
            catalog["officerDefaults"] = "shared/officer-defaults.json";
            await File.WriteAllTextAsync(Path.Combine(stage, "catalog.json"), catalog.ToJsonString(PackageFiles.JsonOptions), Encoding.UTF8);
            await LegacyScenarioDataUpgrade.PublishWorldsAsync(prepared, stage);
            var unclassified = prepared.Worlds.Select(world => new
            {
                scenarioId = world.Scenario.ScenarioId,
                treasures = world.Scenario.Information.Treasures.Where(item => item.Category > 12)
                    .OrderBy(item => item.Id).Select(item => new { id = item.Id, category = item.Category }).ToArray()
            }).Where(world => world.treasures.Length > 0).ToArray();
            await WriteAsync(Path.Combine(stage, "legacy-equipment-upgrade-report.json"), new
            {
                description = "无法识别类别的旧宝物已按可用特征分类；无明确特征时归入宝物槽。请按剧本和物品 ID 检查。",
                unclassified,
                peerage = new { description = "旧包未提供政权爵制；可明确识别的历史政权使用对应重制规则，其余势力使用通用规则。具体名额、费用与授爵权限为游戏设定，不代表完整历史制度。",
                    scenarios = prepared.Worlds.Where(world => world.Scenario.Forces.Any(f => f.PersonalPeerageRegimeId == PersonalPeeragePreset.GenericId))
                        .Select(world => world.Scenario.ScenarioId).ToArray(),
                    regimes = prepared.Worlds.SelectMany(world => world.Scenario.Forces.Where(f => f.PersonalPeerageRegimeId is not null)
                        .Select(f => new { scenarioId = world.Scenario.ScenarioId, forceId = f.Id, regimeId = f.PersonalPeerageRegimeId })).ToArray(),
                    historicalEvidence = prepared.Worlds.SelectMany(world => world.Scenario.Officers
                        .SelectMany(o => o.PersonalPeerageEvidenceRecords.Select(record =>
                            new { scenarioId = world.Scenario.ScenarioId, officerId = o.State.Id, evidence = record }))).ToArray() },
                formations = new { description = "列出的个人初始阵形附依据类型和解释；其余人物使用通用重制默认值，不代表史实。",
                    evidence = prepared.Worlds.SelectMany(world => world.Scenario.Officers.Where(o => o.InitialFormationEvidence is not null)
                        .Select(o => new { scenarioId = world.Scenario.ScenarioId, officerId = o.State.Id,
                            records = o.InitialFormationEvidence!.Select(e => new { e.FormationId, e.Proficiency,
                                kind = e.Kind switch { FormationEvidenceKind.Historical => "史实", FormationEvidenceKind.Romance => "演义",
                                    FormationEvidenceKind.ExperienceInference => "人物经历推定", FormationEvidenceKind.CommonFoundation => "通用基础",
                                    _ => throw new InvalidDataException("Invalid formation evidence kind.") }, e.Source, e.Explanation }).ToArray() })).ToArray() }
            });
            Console.WriteLine($"Equipment upgrade report: {unclassified.Sum(world => world.treasures.Length)} unclassified scenario items.");
            await ContentVerification.VerifyAsync(stage, Console.WriteLine);
            foreach (var world in prepared.Worlds)
            {
                _ = San9.Application.GameSession.Start(world.Scenario, 42, settings: new() { MixedTroopsEnabled = true });
                Console.WriteLine($"Verified mixed troop startup: {world.Scenario.ScenarioId}.");
            }
            Directory.Move(stage, output);
        }
        finally { if (Directory.Exists(stage)) Directory.Delete(stage, true); }
    }

    private static async Task WriteAsync<T>(string path, T value) =>
        await File.WriteAllTextAsync(path, JsonSerializer.Serialize(value, PackageFiles.JsonOptions), Encoding.UTF8);

    private static void CopyDirectory(string source, string destination, string excluded)
    {
        if ((File.GetAttributes(source) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links are not supported in legacy content.");
        Directory.CreateDirectory(destination);
        foreach (string file in Directory.EnumerateFiles(source))
        {
            if ((File.GetAttributes(file) & FileAttributes.ReparsePoint) != 0) throw new InvalidDataException("Links are not supported in legacy content.");
            File.Copy(file, Path.Combine(destination, Path.GetFileName(file)));
        }
        foreach (string directory in Directory.EnumerateDirectories(source))
            if (!string.Equals(directory, excluded, OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal))
                CopyDirectory(directory, Path.Combine(destination, Path.GetFileName(directory)), excluded);
    }
}
