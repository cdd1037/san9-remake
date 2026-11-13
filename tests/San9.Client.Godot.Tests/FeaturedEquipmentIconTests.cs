using San9.Client.Godot;
using San9.Content.Scenarios;

namespace San9.Client.Godot.Tests;

public sealed class FeaturedEquipmentIconTests
{
    [Fact]
    public void AtlasCellsCoverAuthoredItemsWithStableCoordinates()
    {
        var ids = FeaturedEquipmentIcons.CellIds;
        Assert.Equal(40, ids.Length);
        Assert.Equal(ids.Length, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(FeaturedEquipmentPreset.Definitions().Select(item => item.Id).Order(StringComparer.Ordinal),
            ids.Order(StringComparer.Ordinal));
        Assert.Equal("featured-xuanwen-ji", ids[0]);
        Assert.Equal("featured-huwen-fu", ids[23]);
        Assert.Equal("featured-guwen-jing", ids[39]);
    }
}
