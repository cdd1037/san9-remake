using San9.Content.Packages;
using San9.Importer.San9PkTc101.Text;

namespace San9.Importer.San9PkTc101.Packages;

public static class ClientContentImporter
{
    public static async Task<ClientContentPackage> ImportAsync(string gameDirectory, string scenarioFile,
        string textureRoot, string output, Action<string>? progress = null, bool messageReports = false, CancellationToken token = default)
    {
        textureRoot = Path.GetFullPath(textureRoot); output = Path.GetFullPath(output);
        if (textureRoot == output) throw new InvalidDataException("Scenario and texture package directories must differ.");
        var source = await San9ImportSource.OpenAsync(gameDirectory, token);
        using var operation = ImportOperation.Acquire(output);
        var textures = await source.LoadTexturesAsync(textureRoot, token);
        var compiled = ClientContentCompiler.Compile(source, scenarioFile, textureRoot, textures, output, messageReports, token);
        using var publication = new ContentPublication();
        await StageAsync(output, compiled, publication, messageReports, progress, token);
        publication.Commit(token);
        return compiled.Package with { ContentRoot = output };
    }

    internal sealed record SharedContent(string Root, SharedMessageLibrary Messages, SharedMapDefinition Map, SharedScenarioData Scenario);

    internal static async Task StageAsync(string output, CompiledScenario compiled, ContentPublication publication,
        bool messageReports, Action<string>? progress, CancellationToken token, SharedContent? shared = null)
    {
        var content = compiled.Content;
        var messages = content.Text.Messages;
        var scenario = content.Scenario;
        await ScenarioContentFiles.WriteAsync(output, content, token, PublicationMessageCatalog.Groups,
            resourceRoot: shared?.Root ?? output, sharedMessages: shared?.Messages, sharedMap: shared?.Map,
            sharedScenario: shared?.Scenario, publication: publication);
        publication.Retire(Path.Combine(output, "message-id-map.json"));
        if (messageReports)
        {
            var adaptedKeys = compiled.Adaptations.Select(a => a.Key).ToHashSet(StringComparer.Ordinal);
            var messageAudit = GameplayMessageCompiler.Audit(messages)
                .Select(a => new
                {
                    a.Key,
                    SourceKey = PublicationMessageCatalog.Groups.ContainsKey(a.Key) ? a.Key : null,
                    SourceConfirmed = !adaptedKeys.Contains(a.Key) && a.SourceConfirmed,
                    a.RuntimeReady,
                    a.RequiredArguments
                }).ToArray();
            await publication.StageAsync(Path.Combine(output, "message-adaptations.json"), compiled.Adaptations.Select(a => new
                { a.Key, SourceKey = a.Key, a.SourceSha256, a.Decision, a.DiscardedHex }), PackageFiles.JsonOptions, token);
            await publication.StageAsync(Path.Combine(output, "message-readiness.json"), messageAudit, PackageFiles.JsonOptions, token);
        }
        else
        {
            publication.Retire(Path.Combine(output, "message-adaptations.json"));
            publication.Retire(Path.Combine(output, "message-readiness.json"));
        }
        progress?.Invoke($"Compiled messages: {messages.Count} runtime-ready." +
            (messageReports ? " See message-readiness.json for arguments." : ""));
        progress?.Invoke($"Scenario: {scenario.Settlements.Length} settlements; references independent textures at {content.Textures.Path}.");
    }
}
