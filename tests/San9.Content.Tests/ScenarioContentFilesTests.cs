using System.Text.Json;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;

namespace San9.Content.Tests;

public sealed class ScenarioContentFilesTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "san9-split-" + Guid.NewGuid().ToString("N"));

    private static ScenarioContent Fixture() => new(24, [new("fixture", "hash")], new("../textures"),
        new("source", "first", "spring", 200, 200, 13056, 6656,
            [], [], [], [], [], [], [], [], [], [], []),
        new(23, "first", 190, 1, 1, 0, [], [], [], []),
        new(3, new() { ["name"] = new("简体", "繁體") })
        {
            Messages = new() { ["message"] = new([new MessageValue(new("number", Number: 0))], false, []) }
        }, new() { ["portrait"] = "portrait/0" });

    [Fact]
    public async Task SplitFilesPreserveFullModelAndRequiredNulls()
    {
        var content = Fixture();
        await ScenarioContentFiles.WriteAsync(_root, content);
        var restored = await ScenarioContentFiles.LoadAsync(_root);
        Assert.Equal(JsonSerializer.Serialize(content, PackageFiles.JsonOptions),
            JsonSerializer.Serialize(restored, PackageFiles.JsonOptions));
        Assert.Equal(6, Directory.GetFiles(_root).Length);
        string messages = await File.ReadAllTextAsync(Path.Combine(_root, "messages.json"));
        Assert.DoesNotContain("\n", messages);
        Assert.DoesNotContain("\"format\"", messages);
        Assert.DoesNotContain("\"binding\"", messages);
        Assert.Contains("\"number\":0", messages);
        Assert.Contains("\"semanticsConfirmed\":false", messages);
        string force = JsonSerializer.Serialize(new ForceInitialState(1, "name", 0, 0, 0, 0), ScenarioContentFiles.DataJsonOptions);
        Assert.Contains("\"leaderId\":null", force);
        Assert.NotNull(JsonSerializer.Deserialize<ForceInitialState>(force, ScenarioContentFiles.DataJsonOptions));
        Assert.Throws<JsonException>(() => JsonSerializer.Deserialize<ForceInitialState>(
            force.Replace("\"leaderId\":null,", ""), ScenarioContentFiles.DataJsonOptions));
        Assert.Contains("\"binding\": null", JsonSerializer.Serialize(new MessageExpression("number"), PackageFiles.JsonOptions));
        using var text = JsonDocument.Parse(await File.ReadAllTextAsync(Path.Combine(_root, "text.json")));
        Assert.False(text.RootElement.TryGetProperty("messages", out _));
    }

    [Fact]
    public async Task MissingDataFailsLoading()
    {
        await ScenarioContentFiles.WriteAsync(_root, Fixture());
        File.Delete(Path.Combine(_root, "messages.json"));
        await Assert.ThrowsAsync<FileNotFoundException>(() => ScenarioContentFiles.LoadAsync(_root));
    }

    [Theory]
    [InlineData(23)]
    [InlineData(25)]
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
        var before = Directory.GetFiles(_root).ToDictionary(path => path, File.ReadAllText);
        content.Text.Entries.Add("invalid", new(null!, "source"));
        await Assert.ThrowsAsync<JsonException>(() => ScenarioContentFiles.WriteAsync(_root, content));
        foreach (var (path, value) in before) Assert.Equal(value, await File.ReadAllTextAsync(path));
        Assert.Empty(Directory.GetFiles(_root, "*.pending"));
    }

    public void Dispose()
    {
        if (Directory.Exists(_root)) Directory.Delete(_root, true);
    }
}
