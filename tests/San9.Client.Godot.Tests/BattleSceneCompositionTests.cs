using San9.Content.Interface;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BattleSceneCompositionTests
{
    private static BattleSceneDefinition Scene() => new(
        [new([0], "spring", 0, 7, null, "land", new("left", 24, 104), new("right", 144, 104),
            new("left-edge", 144, 184), new("right-edge", 144, 144)),
         new([1], "spring", 0, 7, [3], "river", new("river-left", 24, 104), new("river-right", 144, 104), null, null)],
        [new("land", "river", new("bank", 72, 184))],
        new() { [0] = new([new("city-left", 0, 152)], [new("city-right", 144, 0)], true),
            [1] = new([new("field-left", 80, 168)], [new("field-right", 200, 120)], false) });

    [Fact]
    public void ContactUsesTransitionAndSeparationUsesBothEdges()
    {
        var land = new MapSquare(0, 0, 0, 0);
        var river = new MapSquare(1, 0, 0, 0) { TextureVariant = 3 };
        var contact = BattleSceneComposition.Compose(Scene(), land, river, "spring", null, null, true);
        Assert.Equal(new[] { "river-right", "left", "bank" }, contact.Select(layer => layer.Asset));
        var separated = BattleSceneComposition.Compose(Scene(), land, land, "spring", null, null, false);
        Assert.Equal(new[] { "right", "left", "right-edge", "left-edge" }, separated.Select(layer => layer.Asset));
    }

    [Fact]
    public void FacilitiesReplaceOnlyTheirOwnGroundAndKeepForegroundOrder()
    {
        var ground = new MapSquare(0, 0, 0, 0);
        var targetCity = BattleSceneComposition.Compose(Scene(), ground, ground, "spring", 1, 0, false);
        Assert.Equal(new[] { "city-right", "left", "right-edge", "left-edge", "field-left" }, targetCity.Select(layer => layer.Asset));
        var sourceCity = BattleSceneComposition.Compose(Scene(), ground, ground, "spring", 0, 1, true);
        Assert.Equal(new[] { "right", "field-right", "city-left" }, sourceCity.Select(layer => layer.Asset));
    }
}
