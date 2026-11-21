using System.Text.Json;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class BuildingResourceCoverageTests
{
    [Fact]
    public void SourceLocationsCoverEverySupportedBuildingDamageStage()
    {
        using var stream = typeof(MapImporter).Assembly.GetManifestResourceStream("San9.Importer.San9PkTc101.MapSourceLocations.json")!;
        using var document = JsonDocument.Parse(stream);
        var entries = document.RootElement.GetProperty("compositions").EnumerateArray().ToDictionary(e => e.GetProperty("id").GetInt32());
        // RE-0055: the 91 original slots include eight empty compositions.
        var required = Enumerable.Range(0, 91).Except([0, 5, 10, 15, 19, 24, 29, 34]);
        foreach (int id in required)
        {
            Assert.True(entries.TryGetValue(id, out var entry), $"Missing building composition {id}.");
            Assert.True(entry.GetProperty("count").GetInt32() > 0);
            Assert.True(entry.GetProperty("offset").GetInt32() > 0);
            foreach (string layer in new[] { "flags", "soldiers" })
            {
                var location = entry.GetProperty(layer);
                Assert.True(location.GetProperty("count").GetInt32() >= 0);
                Assert.Equal(location.GetProperty("count").GetInt32() == 0, location.GetProperty("offset").GetInt32() == 0);
            }
        }
    }
}
