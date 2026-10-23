using San9.Content.Images;
using San9.Content.Interface;

namespace San9.Content.Tests;

public sealed class OpeningMapDefinitionTests
{
    [Fact]
    public void PickingUsesAuthoredAreasAndKeepsSparseCityIdentities()
    {
        var map = Map();
        map.Validate(new Dictionary<string, ImageAsset> { ["image"] = new("image", "image.png", 16, 4) });
        Assert.Equal(71, map.CityAt(1, 2));
        Assert.Equal(71, map.CityAt(3, 2));
        Assert.Null(map.CityAt(4, 2));
        Assert.Equal(900, map.CityAt(6, 2));
        Assert.Null(map.CityAt(-1, 2));
        Assert.Null(map.CityAt(1, 3));
    }

    [Fact]
    public void OverlappingAndOutOfBoundsAreasAreRejected()
    {
        var assets = new Dictionary<string, ImageAsset> { ["image"] = new("image", "image.png", 16, 4) };
        foreach (var run in new[] { new OpeningMapRun(3, 2, 2), new(7, 2, 2), new(1, 4, 1), new(1, 2, 0) })
        {
            var map = Map();
            map.Cities[900] = new(new(6, 2), [run]);
            Assert.Throws<InvalidDataException>(() => map.Validate(assets));
        }
    }
    private static OpeningMapDefinition Map() => new(8, 4, "image", "image", new(new("image", 0, 0), 71),
        new() { [71] = new(new(1, 2), [new(1, 2, 3)]), [900] = new(new(6, 2), [new(6, 2, 2)]) },
        new() { ["neutral"] = new("image", null, "ffffff") }, "neutral");
}
