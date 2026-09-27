using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class ScenarioContentFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-split-" + Guid.NewGuid().ToString("N"));

    private static ScenarioContent Fixture() => new(ScenarioContent.CurrentSchemaVersion, [new("fixture", "hash")], new("../textures"),
        new("source", "first", "spring", 200, 200, 13056, 6656,
            [], [], [], [], [], [], [], [], [], [], []),
        new(ScenarioInitialState.CurrentSchemaVersion, "first", 190, 1, 1, 0, [], [], [], []),
        new(TextCatalog.CurrentSchemaVersion, new() { ["name"] = "简体" })
        {
            MessagePeople = new() { [9001] = new("name", "name", "portrait.custom") },
            Messages = new() { ["message"] = new([new MessageLiteral("正文"), new MessageValue(new("constant", Number: 0))]), ["empty"] = new([]) }
        }, new() { ["portrait"] = "portrait/0" });

    [Fact]
    public async Task SplitFilesPreserveFullModelAndRequiredNulls()
    {
        var content = Fixture();
        await ScenarioContentFiles.WriteAsync(_root, content);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal(JsonSerializer.Serialize(content, PackageFiles.JsonOptions),
            JsonSerializer.Serialize(restored, PackageFiles.JsonOptions));
        Assert.Equal(7, Directory.GetFiles(_root, "*", SearchOption.AllDirectories).Length);
        string messages = await File.ReadAllTextAsync(Path.Combine(_root, "messages/general.json"));
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
        using var text = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "text.json")));
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
        File.Delete(Path.Combine(_root, "messages/general.json"));
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
        File.Move(Path.Combine(_root, "map.json"), Path.Combine(_root, "old-map.json"));
        var manifest = await PackageFiles.ReadAsync<ScenarioContentManifest>(Path.Combine(_root, "content.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"),
            JsonSerializer.Serialize(manifest with { Map = "old-map.json" }, PackageFiles.JsonOptions));
        await File.WriteAllTextAsync(Path.Combine(_root, "notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(File.Exists(Path.Combine(_root, "old-map.json")));
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
        Assert.True(File.Exists(Path.Combine(_root, "messages/battle.json")));
        Assert.True(File.Exists(Path.Combine(_root, "messages/help.json")));
        await File.WriteAllTextAsync(Path.Combine(_root, "messages/notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(File.Exists(Path.Combine(_root, "messages/battle.json")));
        Assert.False(File.Exists(Path.Combine(_root, "messages/help.json")));
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
        await File.WriteAllTextAsync(Path.Combine(_root, "messages/notes.json"), "{}");
        await ScenarioContentFiles.WriteAsync(_root, content, messageGroups: new Dictionary<string, string>
        { ["messages.8677"] = "help" });
        Assert.False(File.Exists(Path.Combine(_root, "messages/dialogue.json")));
        Assert.True(File.Exists(Path.Combine(_root, "messages/notes.json")));
        using var help = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "messages/help.json")));
        Assert.Equal("messages.8677", Assert.Single(help.RootElement.EnumerateObject()).Name);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal("messages.8677", Assert.IsType<MessageReference>(Assert.Single(restored.Text.Messages["messages.5592"].Nodes)).Key);
        Assert.Equal("帮助", MessageMarkup.Static(restored.Text.Messages["messages.8677"].Nodes));
    }

    [Theory]
    [InlineData("missing", "help")]
    [InlineData("message", "../outside")]
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
        File.Move(Path.Combine(_root, "messages/general.json"), Path.Combine(_root, "messages.json"));
        await File.WriteAllTextAsync(Path.Combine(_root, "content.json"),
            """{"schemaVersion":140,"map":"map.json","scenario":"scenario.json","text":"text.json","messages":"messages.json","illustrations":"illustrations.json"}""");
        await Assert.ThrowsAsync<InvalidDataException>(() => ScenarioContentFiles.LoadAsync(_root));
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        Assert.False(File.Exists(Path.Combine(_root, "messages.json")));
        Assert.Equal(2, (await ScenarioContentFiles.LoadAsync(_root)).Text.Messages.Count);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
