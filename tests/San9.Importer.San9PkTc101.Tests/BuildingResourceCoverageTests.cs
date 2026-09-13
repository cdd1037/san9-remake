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
        // RE-0055: required visual states, including the absent initial-map states that G07 construction can reach.
        int[] bases = [0, 15, 19, 24, 29, 34, 39, 43, 48, 53, 58, 62, 66, 71, 76, 81, 86];
        for (int type = 0; type < bases.Length; type++)
            for (int style = 0; style < (type == 0 ? 3 : 1); style++)
                for (int stage = 1; stage <= (type is 0 or 2 or 3 or 4 or 5 or 7 or 9 or 12 or 13 or 16 ? 4 : 3); stage++)
                {
                    int id = bases[type] + style * 5 + stage;
                    Assert.True(entries.TryGetValue(id, out var entry), $"Missing composition {id} for type {type}, style {style}, stage {stage}.");
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
