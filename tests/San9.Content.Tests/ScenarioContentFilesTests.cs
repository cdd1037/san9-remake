using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Tests;

namespace San9.Content.Tests;

public sealed class ScenarioContentFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-split-" + Guid.NewGuid().ToString("N"));

    [Fact]
    public async Task CommandTextPublicationPreservesOtherDataAndDoesNotLoadWorldDependencies()
    {
        var original = Fixture();
        original.Text.Entries.Add("ui.commands.retired-role", "retired");
        await ScenarioContentFiles.WriteAsync(_root, original);
        string textPath = PublishedPath("text.json");
        string manifestPath = Path.Combine(_root, "content.json");
        var manifestBytes = await File.ReadAllBytesAsync(manifestPath);
        var presentationBytes = await File.ReadAllBytesAsync(PublishedPath("presentation.json"));
        File.Delete(PublishedPath("scenario.json"));
        var entries = CommandTextFixture.Entries();
        Assert.Equal(1, await ScenarioContentFiles.WriteCommandTextAsync([_root, _root], entries));
        var updated = await PackageFiles.ReadAsync<StaticTextContent>(textPath);
        Assert.Equal("简体", updated.Entries["name"]);
        Assert.False(updated.Entries.ContainsKey("ui.commands.retired-role"));
        Assert.Equal(original.Text.MessagePeople, updated.MessagePeople);
        Assert.Equal(manifestBytes, await File.ReadAllBytesAsync(manifestPath));
        Assert.Equal(presentationBytes, await File.ReadAllBytesAsync(PublishedPath("presentation.json")));
        Assert.Equal(0, await ScenarioContentFiles.WriteCommandTextAsync([_root], entries));
    }

    [Fact]
    public async Task CommandTextPublicationDoesNotPublishAPartialBatchOrInvalidRoles()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        string path = PublishedPath("text.json");
        byte[] original = await File.ReadAllBytesAsync(path);
        var entries = CommandTextFixture.Entries();
        await Assert.ThrowsAsync<DirectoryNotFoundException>(() => ScenarioContentFiles.WriteCommandTextAsync(
            [_root, Path.Combine(_root,"missing")], entries));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        entries[CommandText.Key("result.reward")] = "invalid {2}";
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteCommandTextAsync([_root], entries));
        Assert.Equal(original, await File.ReadAllBytesAsync(path));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.pending", SearchOption.AllDirectories));
    }

    private string PublishedPath(string name)
    {
        var manifest = JsonSerializer.Deserialize<ScenarioContentManifest>(File.ReadAllText(Path.Combine(_root, "content.json")), PackageFiles.JsonOptions)!;
        var paths = new[] { manifest.Map, manifest.Scenario, manifest.Text, manifest.Illustrations, manifest.Presentation, manifest.Definitions, manifest.OfficerDefaults, manifest.Geography }.Concat(manifest.Messages);
        return PackageFiles.Resolve(_root, paths.Single(p => p == name || p.EndsWith("/" + name, StringComparison.Ordinal)));
    }

    private static ScenarioContent Fixture() => new(ScenarioContent.CurrentSchemaVersion, new("../textures"),
        new(new(MapDefinition.CurrentSchemaVersion, "source", 200, 200, 13056, 6656,
            [], [], [], [], [], [], [], [], []), []),
        new(ScenarioInitialState.CurrentSchemaVersion, "first", 190, 1, 1, 0, [], [], [], []),
        new(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "简体" })
        {
            MessagePeople = new() { [9001] = new("name", "name", "portrait.custom") },
            Messages = new() { ["message"] = new([new MessageLiteral("正文"), new MessageValue(new("constant", Number: 0))]), ["empty"] = new([]) }
        }, new() { ["portrait"] = "portrait/0" });

    [Fact]
    public async Task CatalogPublicationKeepsLiveSharedInputsAndFirstWorldWhenLaterScenarioFails()
    {
        var content = Fixture();
        var shared = SharedScenarioData.From(content.Scenario, "shared/definitions.json", "shared/officers.json", "shared/geography.json", content.Text);
        string first = Path.Combine(_root, "first");
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        await ScenarioContentFiles.WriteAsync(first, content, resourceRoot: _root, sharedScenario: shared);
        byte[] definitions = await File.ReadAllBytesAsync(Path.Combine(_root, shared.DefinitionsPath));
        byte[] world = await File.ReadAllBytesAsync(Path.Combine(first, "scenario.json"));
        var changed = content with { Scenario = content.Scenario with { DefaultLoyalty = 77 } };
        var replacement = SharedScenarioData.From(changed.Scenario, shared.DefinitionsPath, shared.OfficersPath, shared.GeographyPath, changed.Text);
        using (var batch = new ContentPublication())
        {
            await ScenarioDataFiles.WriteSharedAsync(_root, replacement, publication: batch);
            await ScenarioContentFiles.WriteAsync(first, changed, resourceRoot: _root, sharedScenario: replacement, publication: batch);
            await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(Path.Combine(_root, "second"),
                changed with { SchemaVersion = 0 }, resourceRoot: _root, sharedScenario: replacement, publication: batch));
            Assert.Equal(definitions, await File.ReadAllBytesAsync(Path.Combine(_root, shared.DefinitionsPath)));
        }
        Assert.Equal(definitions, await File.ReadAllBytesAsync(Path.Combine(_root, shared.DefinitionsPath)));
        Assert.Equal(world, await File.ReadAllBytesAsync(Path.Combine(first, "scenario.json")));
        Assert.Empty(Directory.EnumerateFiles(_root, "*.pending", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CatalogPublicationCommitsSharedMessagesMapAndAllWorldsTogether()
    {
        var content = Fixture();
        var shared = SharedScenarioData.From(content.Scenario, "shared/definitions.json", "shared/officers.json", "shared/geography.json", content.Text);
        using var batch = new ContentPublication();
        await ScenarioDataFiles.WriteSharedAsync(_root, shared, publication: batch);
        var messages = await MessageLibraryFiles.WriteAsync(_root, "shared/messages/library.json", content.Text.Messages, publication: batch);
        var map = await MapDefinitionFiles.WriteAsync(_root, "shared/maps/china.json", content.Map.Definition, publication: batch);
        foreach (string id in new[] { "first", "second" })
            await ScenarioContentFiles.WriteAsync(Path.Combine(_root, id), content with { Scenario = content.Scenario with { ScenarioId = id } },
                resourceRoot: _root, sharedMessages: messages, sharedMap: map, sharedScenario: shared, publication: batch);
        Assert.False(File.Exists(Path.Combine(_root, shared.DefinitionsPath)));
        Assert.False(File.Exists(Path.Combine(_root, "first/content.json")));
        batch.Commit();
        foreach (string id in new[] { "first", "second" })
        {
            var loaded = await ScenarioContentFiles.LoadAsync(Path.Combine(_root, id));
            Assert.Equal(id, loaded.Scenario.ScenarioId);
            Assert.Equal(content.Text.Messages.Count, loaded.Text.Messages.Count);
        }
    }

    [Fact]
    public async Task GlobalLoyaltyDefaultRoundTripsThroughSharedRulesAndScenarioOverrides()
    {
        var content = Fixture();
        var defaults = content.Scenario with { DefaultLoyalty = 63 };
        var shared = SharedScenarioData.From(defaults, "shared/definitions.json", "shared/officers.json", "shared/geography.json", content.Text);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        await ScenarioContentFiles.WriteAsync(_root, content with { Scenario = defaults with { DefaultLoyalty = 71 } }, sharedScenario: shared);
        var loaded = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal(71, loaded.Scenario.DefaultLoyalty);
        using var rules = JsonDocument.Parse(await File.ReadAllTextAsync(PublishedPath("definitions.json")));
        Assert.Equal(63, rules.RootElement.GetProperty("defaultLoyalty").GetInt32());
        using var world = JsonDocument.Parse(await File.ReadAllTextAsync(PublishedPath("scenario.json")));
        Assert.Equal(71, world.RootElement.GetProperty("definitionOverrides").GetProperty("defaultLoyalty").GetProperty("value").GetInt32());
    }

    [Theory]
    [InlineData("shared-prologue")]
    [InlineData(null)]
    public async Task OpeningMovieIsAuthoredIndependentlyOfScenarioIdentityAndLoadsBeforeWorld(string? role)
    {
        var content = Fixture() with
        {
            Presentation = San9.Content.Interface.CommandPresentation.Empty with { OpeningMovie = role }
        };
        await ScenarioContentFiles.WriteAsync(_root, content);
        if (role is null)
        {
            string path = PublishedPath("presentation.json");
            var authored = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!.AsObject();
            authored.Remove("openingMovie");
            await File.WriteAllTextAsync(path, authored.ToJsonString(), System.Text.Encoding.UTF8);
        }
        Assert.Equal(role, (await ScenarioContentFiles.LoadAsync(_root)).Presentation.OpeningMovie);
        var manifest = await ScenarioContentFiles.ReadManifestAsync(_root);
        foreach (string name in new[] { "scenario.json", "map.json", "definitions.json", "text.json" })
            File.Delete(PublishedPath(name));
        var presentation = await ScenarioContentFiles.LoadPresentationAsync(_root, manifest);
        Assert.Equal(role, presentation.OpeningMovie);
    }

    [Fact]
    public async Task ManifestPreviewRoundTripsAndRefreshesWithoutReadingOrChangingWorldData()
    {
        var preview = new San9.Content.Interface.ScenarioPreviewEntry("custom", "custom-category", 281, 1, "标题", "简介", []);
        var content = Fixture() with { Preview = preview };
        await ScenarioContentFiles.WriteAsync(_root, content);
        Assert.Equal(preview.Title, (await ScenarioContentFiles.LoadPreviewAsync(_root)).Title);
        Assert.Equal(preview.Title, (await ScenarioContentFiles.LoadAsync(_root)).Preview!.Title);
        string scenarioPath = PublishedPath("scenario.json");
        await File.WriteAllTextAsync(scenarioPath, "invalid world", System.Text.Encoding.UTF8);
        await File.WriteAllTextAsync(Path.Combine(_root, "preview.json"), "obsolete preview", System.Text.Encoding.UTF8);

        await ScenarioContentFiles.WritePreviewAsync(_root, preview with { Title = "新标题" });

        Assert.Equal("新标题", (await ScenarioContentFiles.LoadPreviewAsync(_root)).Title);
        Assert.Equal("invalid world", await File.ReadAllTextAsync(scenarioPath));
        Assert.False(File.Exists(Path.Combine(_root, "preview.json")));
    }

    [Fact]
    public async Task SelectionContentReadsOnlyRelevantStateAndDisplayDependencies()
    {
        var content = Fixture();
        var shared = await MessageLibraryFiles.WriteAsync(_root, "shared/messages/library.json", content.Text.Messages);
        await ScenarioContentFiles.WriteAsync(_root, content, sharedMessages: shared);
        var full = await ScenarioContentFiles.LoadAsync(_root);
        string scenarioPath = PublishedPath("scenario.json");
        var authored = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(scenarioPath))!;
        var definitions = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(PublishedPath("definitions.json")))!;
        definitions["originalAi"] = "unused by force selection";
        await File.WriteAllTextAsync(PublishedPath("definitions.json"), definitions.ToJsonString(), System.Text.Encoding.UTF8);
        foreach (string name in new[] { "map.json", "presentation.json", "shared/messages/library.json" })
            await File.WriteAllTextAsync(Path.Combine(_root, name), "invalid unused content", System.Text.Encoding.UTF8);

        var initial = await ScenarioContentFiles.LoadSelectionAsync(_root);

        Assert.Equal(JsonSerializer.Serialize(ScenarioSelectionState.From(full.Scenario), ScenarioContentFiles.DataJsonOptions), JsonSerializer.Serialize(initial.Scenario, ScenarioContentFiles.DataJsonOptions));
        Assert.Equal(full.Text.Entries, initial.Text.Entries);
        Assert.Equal(full.Text.MessagePeople, initial.Text.MessagePeople);
        Assert.Equal(full.IllustrationRoles, initial.IllustrationRoles);
        Assert.Empty(initial.Text.Messages);
        await Assert.ThrowsAsync<JsonException>(() => ScenarioContentFiles.LoadAsync(_root));
        authored["schemaVersion"] = ScenarioDocument.CurrentSchemaVersion + 1;
        await File.WriteAllTextAsync(scenarioPath, authored.ToJsonString(), System.Text.Encoding.UTF8);
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadSelectionAsync(_root));
    }

    [Fact]
    public async Task SharedMessagesAllowPrivateAdditionsAndExplicitOverridesWithoutChangingOtherScenarios()
    {
        var content = Fixture();
        content.Text.Messages.Add("shared.entry", new([new MessageReference("shared.answer", false)]));
        content.Text.Messages.Add("shared.answer", new([new MessageLiteral("公共") ]));
        var library = await MessageLibraryFiles.WriteAsync(_root, "shared/messages/library.json", content.Text.Messages);
        string first = Path.Combine(_root, "first"), second = Path.Combine(_root, "second");
        await ScenarioContentFiles.WriteAsync(first, content, resourceRoot: _root, sharedMessages: library);
        content.Text.Messages["shared.answer"] = new([new MessageReference("private.answer", false)]);
        content.Text.Messages.Add("private.answer", new([new MessageLiteral("私有") ]));
        content = content with { Text = content.Text with { MessageTags = ["custom-opening"] } };
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(second, content,
            resourceRoot: _root, sharedMessages: library));
        await ScenarioContentFiles.WriteAsync(second, content, resourceRoot: _root, sharedMessages: library,
            messageOverrides: ["shared.answer"]);
        var loaded = await Task.WhenAll(ScenarioContentFiles.LoadAsync(first), ScenarioContentFiles.LoadAsync(second));
        var a = loaded[0];
        var b = loaded[1];
        Assert.Equal("公共", a.Text.Resolve("shared.answer"));
        Assert.IsType<MessageReference>(Assert.Single(b.Text.Messages["shared.answer"].Nodes));
        Assert.Equal("私有", b.Text.Resolve("private.answer"));
        Assert.Equal(["custom-opening"], b.Text.MessageTags);
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(second, "content.json"));
        Assert.Equal("shared/messages/library.json", manifest.SharedMessages);
        Assert.Equal(["shared.answer"], manifest.MessageOverrides);
        await File.WriteAllTextAsync(Path.Combine(second, "content.json"), JsonSerializer.Serialize(
            manifest with { MessageOverrides = [] }, PackageFiles.JsonOptions));
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(second));
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("cycle")]
    [InlineData("unused-override")]
    public async Task InvalidPrivateCompositionIsRejected(string failure)
    {
        var content = Fixture();
        var library = await MessageLibraryFiles.WriteAsync(_root, "shared/messages/library.json", content.Text.Messages);
        content.Text.Messages["message"] = failure switch
        {
            "missing" => new([new MessageReference("missing", false)]),
            "cycle" => new([new MessageReference("message", false)]),
            _ => content.Text.Messages["message"]
        };
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(Path.Combine(_root, "scenario"), content,
            resourceRoot: _root, sharedMessages: library, messageOverrides: [failure == "unused-override" ? "missing" : "message"]));
    }

    [Fact]
    public async Task SharedLibraryRetiresOnlyOwnedGroupsAndRejectsCyclesBeforePublishing()
    {
        var messages = Fixture().Text.Messages;
        const string index = "shared/messages/library.json";
        await MessageLibraryFiles.WriteAsync(_root, index, messages);
        string root = Path.Combine(_root, "shared", "messages");
        await File.WriteAllTextAsync(Path.Combine(root, "notes.json"), "{}");
        await MessageLibraryFiles.WriteAsync(_root, index, messages, messages.Keys.ToDictionary(k => k, _ => "dialogue"));
        Assert.False(File.Exists(Path.Combine(root, "general.json")));
        Assert.True(File.Exists(Path.Combine(root, "notes.json")));
        string before = await File.ReadAllTextAsync(Path.Combine(_root, index));
        messages["message"] = new([new MessageReference("message", false)]);
        await Assert.ThrowsAsync<InvalidDataException>(() => MessageLibraryFiles.WriteAsync(_root, index, messages));
        Assert.Equal(before, await File.ReadAllTextAsync(Path.Combine(_root, index)));
        Assert.Equal(2, (await MessageLibraryFiles.LoadAsync(Path.Combine(_root, index))).Count);
        Assert.Empty(Directory.GetFiles(root, "*.pending"));
    }

    [Fact]
    public async Task ScenarioFilesStayInTheirAuthoredPackagesWithACommonResourceRoot()
    {
        string first = Path.Combine(_root, "scenarios", "history", "first");
        string second = Path.Combine(_root, "scenarios", "fiction", "second");
        var content = Fixture();
        await ScenarioContentFiles.WriteAsync(first, content, resourceRoot: _root);
        await ScenarioContentFiles.WriteAsync(second, content with
        { Scenario = content.Scenario with { ScenarioId = "second", Year = 281, PlayerForceId = null } }, resourceRoot: _root);
        var a = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(first, "content.json"));
        var b = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(second, "content.json"));
        Assert.NotEqual(a.Text, b.Text);
        Assert.NotEqual(a.Messages, b.Messages);
        Assert.NotEqual(a.Presentation, b.Presentation);
        Assert.Equal("scenarios/fiction/second/messages/text.json", b.Text);
        Assert.False(File.Exists(Path.Combine(second, "text.json")));
        Assert.Equal("scenarios/fiction/second/map.json", b.Map);
        Assert.False(Directory.Exists(Path.Combine(second, "data")));
        Assert.Equal("scenario.json", Path.GetFileName(b.Scenario));
        Directory.Delete(first, true);
        var restored = await ScenarioContentFiles.LoadAsync(second);
        Assert.Equal("second", restored.Scenario.ScenarioId);
        Assert.Equal(281, restored.Scenario.Year);
        Assert.Null(restored.Scenario.PlayerForceId);
        Assert.Equal(JsonSerializer.Serialize(content.Scenario.OriginalAi), JsonSerializer.Serialize(restored.Scenario.OriginalAi));
        Assert.Equal(content.Text.Entries, restored.Text.Entries);
    }

    [Fact]
    public async Task SharedGeographyKeepsPrivateBuildingsAndRetiresTheStandaloneDefinition()
    {
        var content = Fixture();
        var shared = await MapDefinitionFiles.WriteAsync(_root, "shared/maps/custom/map.json", content.Map.Definition);
        string first = Path.Combine(_root, "first"), second = Path.Combine(_root, "second");
        await ScenarioContentFiles.WriteAsync(first, content, resourceRoot: _root, sharedMap: shared);
        var secondContent = content with
        {
            Map = content.Map with { Buildings = [new(9001, 7, 8, 2)] },
            Scenario = content.Scenario with { ScenarioId = "second", Month = 7 }
        };
        await ScenarioContentFiles.WriteAsync(second, secondContent, resourceRoot: _root);
        Assert.True(File.Exists(Path.Combine(second, "map-definition.json")));
        await ScenarioContentFiles.WriteAsync(second, secondContent, resourceRoot: _root, sharedMap: shared);
        Assert.False(File.Exists(Path.Combine(second, "map-definition.json")));
        var a = await ScenarioContentFiles.LoadAsync(first);
        var b = await ScenarioContentFiles.LoadAsync(second);
        Assert.Empty(a.Map.Buildings);
        Assert.Equal([new San9.Content.Maps.MapBuildingPlacement(9001, 7, 8, 2)], b.Map.Buildings);
        Assert.Equal(JsonSerializer.Serialize(a.Map.Definition), JsonSerializer.Serialize(b.Map.Definition));
        var manifest = await PackageFiles.ReadAsync<ScenarioMapManifest>(Path.Combine(second, "map.json"));
        Assert.Equal("shared/maps/custom/map.json", manifest.Definition);
        using var document = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(second, "map.json")));
        Assert.Equal(new[] { "buildings", "definition" }, document.RootElement.EnumerateObject().Select(p => p.Name).Order());
        var different = content with { Map = content.Map with { Definition = content.Map.Definition with { SourceBuild = "another-map" } } };
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(first, different,
            resourceRoot: _root, sharedMap: shared));
        File.Delete(Path.Combine(_root, shared.Path));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScenarioContentFiles.LoadAsync(first));
    }

    [Theory]
    [InlineData("shared/maps/custom/map.json")]
    [InlineData("map-definition.json")]
    public async Task RewritingScenarioPreservesItsReferencedMapDefinition(string path)
    {
        var content = Fixture();
        var shared = await MapDefinitionFiles.WriteAsync(_root, path, content.Map.Definition);
        await ScenarioContentFiles.WriteAsync(_root, content, sharedMap: shared);
        await ScenarioContentFiles.WriteAsync(_root, content, sharedMap: shared);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal(JsonSerializer.Serialize(content.Map.Definition), JsonSerializer.Serialize(restored.Map.Definition));
    }

    [Fact]
    public async Task SplitFilesPreserveFullModelAndRequiredNulls()
    {
        var content = Fixture();
        content = content with { Map = content.Map with { Definition = content.Map.Definition with { LeftEdge = [null], RightEdge = [null] } } };
        await ScenarioContentFiles.WriteAsync(_root, content);
        using var manifestDocument = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "content.json")));
        Assert.False(manifestDocument.RootElement.TryGetProperty("sources", out _));
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal(JsonSerializer.Serialize(content, PackageFiles.JsonOptions),
            JsonSerializer.Serialize(restored, PackageFiles.JsonOptions));
        Assert.Equal(11, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
        string messages = await File.ReadAllTextAsync(PublishedPath("messages/general.json"));
        Assert.DoesNotContain("\"format\"", messages);
        Assert.DoesNotContain("\"binding\"", messages);
        Assert.DoesNotContain("semanticsConfirmed", messages);
        Assert.DoesNotContain("requiredBindings", messages);
        Assert.DoesNotContain("unavailableReasons", messages);
        Assert.DoesNotContain("sourceBbCode", messages);
        Assert.DoesNotContain("prefix", messages);
        Assert.DoesNotContain("suffix", messages);
        using var messageDocument = JsonDocument.Parse(messages);
        Assert.Equal(JsonValueKind.Array, messageDocument.RootElement.GetProperty("message").ValueKind);
        Assert.Equal("正文", messageDocument.RootElement.GetProperty("message")[0].GetString());
        Assert.Equal(0, messageDocument.RootElement.GetProperty("empty").GetArrayLength());
        string force = JsonSerializer.Serialize(new ForceInitialState(1, "name", 0, 0), ScenarioContentFiles.DataJsonOptions);
        Assert.Contains("\"leaderId\":null", force);
        Assert.NotNull(JsonSerializer.Deserialize<ForceInitialState>(force, ScenarioContentFiles.DataJsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ForceInitialState>(
            force.Replace("\"leaderId\":null,", ""), ScenarioContentFiles.DataJsonOptions));
        Assert.Contains("\"binding\": null", JsonSerializer.Serialize(new MessageExpression("number"), PackageFiles.JsonOptions));
        using var text = JsonDocument.Parse(await File.ReadAllTextAsync(PublishedPath("text.json")));
        Assert.False(text.RootElement.TryGetProperty("messages", out _));
        Assert.Equal("简体", text.RootElement.GetProperty("entries").GetProperty("name").GetString());
        Assert.DoesNotContain("sourceText", text.RootElement.GetRawText());
    }

    [Theory]
    [InlineData("{\"nodes\":[]}")]
    [InlineData("[null]")]
    [InlineData("[{\"kind\":\"unknown\"}]")]
    public void InvalidMessageArrayFailsDeserialization(string json)
    {
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<MessageDefinition>(json, ScenarioContentFiles.DataJsonOptions));
    }

    [Fact]
    public async Task MissingDataFailsLoading()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        File.Delete(PublishedPath("messages/general.json"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScenarioContentFiles.LoadAsync(_root));
    }

    [Theory]
    [InlineData(24)]
    [InlineData(25)]
    [InlineData(26)]
    [InlineData(27)]
    [InlineData(130)]
    [InlineData(131)]
    [InlineData(132)]
    [InlineData(133)]
    [InlineData(140)]
    [InlineData(143)]
    public async Task UnsupportedVersionRequestsReconversion(int version)
    {
        Directory.CreateDirectory(_root);
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"), $"{{\"schemaVersion\":{version}}}");
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
        Assert.Contains("reconvert", error.Message);
    }

    [Theory]
    [InlineData("../outside.json")]
    [InlineData("/outside.json")]
    public async Task RejectsDataOutsidePackage(string path)
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(_root, "content.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"),
            JsonSerializer.Serialize(manifest with { Map = path }, PackageFiles.JsonOptions));
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
    }

    [Fact]
    public async Task RewriteRemovesOnlyPreviouslyReferencedObsoleteFiles()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Directory.CreateDirectory(Path.Combine(_root, "data", "old"));
        File.Move(PublishedPath("map.json"), Path.Combine(_root, "data", "old", "map.json"));
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(_root, "content.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"),
            JsonSerializer.Serialize(manifest with { Map = "data/old/map.json" }, PackageFiles.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(Directory.Exists(Path.Combine(_root, "data")));
        Assert.True(File.Exists(Path.Combine(_root, "notes.json")));
        Assert.Empty(Directory.GetFiles(_root, "*.pending"));
    }

    [Fact]
    public async Task FailedStagingKeepsPublishedFilesAndRemovesTemporaryFiles()
    {
        var content = Fixture();
        await ScenarioContentFiles.WriteAsync(_root, content);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllText);
        content.Text.Messages.Add("invalid", new([new MessageLiteral(null!)]));
        await Assert.ThrowsAsync<JsonException>(() => ScenarioContentFiles.WriteAsync(_root, content));
        foreach (var (path, value) in before) Assert.Equal(value, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_root, "*.pending", SearchOption.AllDirectories));
    }

    [Fact]
    public async Task CategoriesPreserveCrossFileReferencesAndRetireRemovedCategories()
    {
        var content = Fixture();
        content.Text.Messages.Add("battle.attack", new([new MessageReference("help.attack", true)]));
        content.Text.Messages.Add("help.attack", new([new MessageLiteral("进攻")]));
        await ScenarioContentFiles.WriteAsync(_root, content);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal("help.attack", Assert.IsType<MessageReference>(Assert.Single(restored.Text.Messages["battle.attack"].Nodes)).Key);
        Assert.Equal("进攻", MessageMarkup.Static(restored.Text.Messages["help.attack"].Nodes));
        string battlePath = PublishedPath("messages/battle.json");
        string helpPath = PublishedPath("messages/help.json");
        Assert.True(File.Exists(battlePath));
        Assert.True(File.Exists(PublishedPath("messages/help.json")));
        Directory.CreateDirectory(Path.Combine(_root, "messages"));
        await File.WriteAllTextAsync(Path.Combine(_root, "messages/notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(File.Exists(battlePath));
        Assert.False(File.Exists(helpPath));
        Assert.True(File.Exists(Path.Combine(_root, "messages/notes.json")));
    }

    [Fact]
    public async Task ExplicitGroupsMoveRecordsWithoutRenamingReferencesAndRetireOldFiles()
    {
        var content = Fixture();
        content.Text.Messages.Add("messages.5592", new([new MessageReference("messages.8677", true)]));
        content.Text.Messages.Add("messages.8677", new([new MessageLiteral("帮助")]));
        await ScenarioContentFiles.WriteAsync(_root, content, messageGroups: new Dictionary<string, string>
        { ["messages.5592"] = "dialogue", ["messages.8677"] = "help" });
        string dialoguePath = PublishedPath("messages/dialogue.json");
        Directory.CreateDirectory(Path.Combine(_root, "messages"));
        await File.WriteAllTextAsync(Path.Combine(_root, "messages/notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, content, messageGroups: new Dictionary<string, string>
        { ["messages.8677"] = "help" });
        Assert.False(File.Exists(dialoguePath));
        Assert.True(File.Exists(Path.Combine(_root, "messages/notes.json")));
        using var help = JsonDocument.Parse(await File.ReadAllTextAsync(PublishedPath("messages/help.json")));
        Assert.Equal("messages.8677", Assert.Single(help.RootElement.EnumerateObject()).Name);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal("messages.8677", Assert.IsType<MessageReference>(Assert.Single(restored.Text.Messages["messages.5592"].Nodes)).Key);
        Assert.Equal("帮助", MessageMarkup.Static(restored.Text.Messages["messages.8677"].Nodes));
    }

    [Theory]
    [InlineData("missing", "help")]
    [InlineData("message", "../outside")]
    [InlineData("message", "text")]
    public async Task InvalidExplicitGroupsAreRejectedBeforeWriting(string key, string group)
    {
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.WriteAsync(_root, Fixture(),
            messageGroups: new Dictionary<string, string> { [key] = group }));
        Assert.False(Directory.Exists(_root));
    }

    [Fact]
    public async Task DuplicateKeysAcrossFilesAreRejected()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(_root, "content.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"), JsonSerializer.Serialize(
            manifest with { Messages = [.. manifest.Messages, manifest.Messages[0]] }, PackageFiles.JsonOptions));
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
        Assert.Contains("Duplicate message key", error.Message);
    }

    [Fact]
    public async Task ReconvertRetiresOldMonolithicMessagesButDoesNotLoadOldSchema()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        File.Move(PublishedPath("messages/general.json"), Path.Combine(_root, "messages.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"),
            """{"schemaVersion":140,"map":"map.json","scenario":"scenario.json","text":"text.json","messages":"messages.json","illustrations":"illustrations.json"}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(File.Exists(Path.Combine(_root, "messages.json")));
        Assert.Equal(2, (await ScenarioContentFiles.LoadAsync(_root)).Text.Messages.Count);
    }

    [Fact]
    public async Task WorldRebuildUpdatesSharedInputsAndWorldWithoutChangingPresentation()
    {
        var content = Fixture();
        var shared = SharedScenarioData.From(content.Scenario, "shared/definitions.json", "shared/officer-defaults.json", "shared/geography.json", content.Text);
        await ScenarioDataFiles.WriteSharedAsync(_root, shared);
        string package = Path.Combine(_root, "scenario");
        await ScenarioContentFiles.WriteAsync(package, content, resourceRoot: _root, sharedScenario: shared);
        string manifestPath = Path.Combine(package, "content.json"), worldPath = Path.Combine(package, "scenario.json");
        var manifest = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(manifestPath))!;
        manifest["schemaVersion"] = ScenarioContent.CurrentSchemaVersion - 1;
        await File.WriteAllTextAsync(manifestPath, manifest.ToJsonString(), System.Text.Encoding.UTF8);
        var resources = Directory.GetFiles(_root, "*", SearchOption.AllDirectories)
            .Where(path => path != worldPath && path != manifestPath).ToDictionary(path => path, File.ReadAllBytes);

        await ScenarioContentFiles.WriteWorldsAsync(_root, shared, [("scenario", content.Scenario with { Year = 191, PlayerForceId = null }, content.Text)]);

        var rebuilt = await ScenarioContentFiles.LoadAsync(package);
        Assert.Equal(191, rebuilt.Scenario.Year);
        Assert.Equal(ScenarioContent.CurrentSchemaVersion, rebuilt.SchemaVersion);
        foreach (var (path, bytes) in resources) Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal(resources.Count + 2, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
    }

    [Fact]
    public async Task FailedManifestReplacementRestoresPreviouslyPublishedFiles()
    {
        var content = Fixture();
        await ScenarioContentFiles.WriteAsync(_root, content);
        var before = Directory.GetFiles(_root, "*", SearchOption.AllDirectories).ToDictionary(path => path, File.ReadAllBytes);
        using (var locked = new FileStream(Path.Combine(_root, "content.json"), FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            if (!OperatingSystem.IsWindows()) return; // Unix permits replacement of an open file.
            var failure = await Record.ExceptionAsync(() => ScenarioContentFiles.WriteAsync(_root,
                content with { Scenario = content.Scenario with { ScenarioId = "replacement" } }));
            Assert.True(failure is IOException or UnauthorizedAccessException, failure?.ToString());
        }
        Assert.Equal(before.Keys.Order(), Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Order());
        foreach (var (path, bytes) in before) Assert.Equal(bytes, await File.ReadAllBytesAsync(path));
        Assert.Equal("first", (await ScenarioContentFiles.LoadAsync(_root)).Scenario.ScenarioId);
    }

    [Fact]
    public async Task NullCollectionElementIsAContentErrorWithItsLocation()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        var path = PublishedPath("scenario.json");
        var node = System.Text.Json.Nodes.JsonNode.Parse(await File.ReadAllTextAsync(path))!;
        node["officers"] = new System.Text.Json.Nodes.JsonArray((System.Text.Json.Nodes.JsonNode?)null);
        await File.WriteAllTextAsync(path, node.ToJsonString());
        var error = await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
        Assert.Contains("Officers[0]", error.Message);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
