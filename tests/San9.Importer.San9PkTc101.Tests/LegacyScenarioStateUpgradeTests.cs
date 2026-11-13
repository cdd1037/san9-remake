using System.Text.Json.Nodes;
using San9.Importer.San9PkTc101.Packages;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class LegacyScenarioStateUpgradeTests
{
    [Fact]
    public async Task WorldPublicationRejectsOutputInsideSource()
    {
        string source = Path.Combine(Path.GetTempPath(), "san9-legacy-world-" + Guid.NewGuid().ToString("N"));
        var prepared = new LegacyScenarioDataUpgrade.Prepared(source, null!, []);
        await Assert.ThrowsAsync<ArgumentException>(() => LegacyScenarioDataUpgrade.PublishWorldsAsync(prepared, Path.Combine(source, "output")));
        Assert.False(Directory.Exists(source));
    }

    [Fact]
    public async Task RequiresExplicitAgeDisplayDecisionForEachScenario()
    {
        string root = Path.Combine(Path.GetTempPath(), "san9-legacy-scenario-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(root);
        try
        {
            await File.WriteAllTextAsync(Path.Combine(root, "catalog.json"),
                "{\"schemaVersion\":4,\"scenarios\":[{\"id\":\"0\",\"package\":\"scenario-0\"}]}",
                new System.Text.UTF8Encoding(true));
            var error = await Assert.ThrowsAsync<InvalidDataException>(() =>
                LegacyScenarioDataUpgrade.PrepareAsync(root, "0", new Dictionary<string, bool>()));
            Assert.Contains("age-display", error.Message);
        }
        finally { Directory.Delete(root, recursive: true); }
    }

    [Fact]
    public void RejectsUnsupportedVersionWithoutChangingInput()
    {
        var source = JsonNode.Parse("{\"schemaVersion\":150}")!.AsObject();
        Assert.Throws<InvalidDataException>(() => LegacyScenarioStateUpgrade.Convert(source, new JsonObject(), true));
        Assert.Equal(150, (int)source["schemaVersion"]!);
    }

    [Fact]
    public void RejectsMissingCatalogTextWithoutChangingInput()
    {
        var source = JsonNode.Parse("""
            {"schemaVersion":151,"information":{"ranks":[{"id":0,"nameKey":"missing","descriptionKey":null}]}}
            """)!.AsObject();
        var text = JsonNode.Parse("{\"entries\":{}}")!.AsObject();
        Assert.Throws<InvalidDataException>(() => LegacyScenarioStateUpgrade.Convert(source, text, true));
        Assert.Equal("missing", (string)source["information"]!["ranks"]![0]!["nameKey"]!);
    }
}
