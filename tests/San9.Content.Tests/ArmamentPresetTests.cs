using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Content.Tests;

public sealed class ArmamentPresetTests
{
    [Fact]
    public void PresetUsesUniqueValidDefinitionsAndRestrictsRangedWeapons()
    {
        var items = ArmamentPreset.Basic();
        Assert.All(items, item => Assert.True(item.IsValid));
        Assert.Equal(items.Length, items.Select(item => item.Id).Distinct(StringComparer.Ordinal).Count());
        var bow = Assert.Single(items, item => item.Id == "bow");
        var crossbow = Assert.Single(items, item => item.Id == "crossbow");
        Assert.True(bow.Supports(2));
        Assert.False(bow.Supports(3));
        Assert.True(crossbow.Supports(3));
        Assert.False(crossbow.Supports(2));
        Assert.All(items.Where(item => item.Slot == ArmamentSlot.Armor), item => Assert.True(item.Supports(0)));
    }
}
