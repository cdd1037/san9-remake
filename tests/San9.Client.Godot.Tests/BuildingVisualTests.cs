using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BuildingVisualTests
{
    private static MapPackage Map() => new(9,"fixture","fixture","spring",200,200,13056,6656,
        Enumerable.Repeat(new MapSquare(0,1,0,0),40000).ToArray(),[],[],[],[],[],[new(0,89,64,0)],
        Enumerable.Range(1,14).Select(id=>new MapComposition(id,[new(false,true,100,0,0)])
            { Guards=[new(1,2,3)],Flags=[new(4,5)] }).ToArray(),[],[],[],[],new([],[]),[])
        { BuildingTypes=[new(0,1000)] };

    [Fact]
    public void DevelopmentAndDurabilityResolveFromCurrentStateWithoutChangingCatalog()
    {
        var map=Map(); var state=new BuildingVisualState(0,1000,2,300,1000,5,10);
        var first=BuildingVisuals.Resolve(map,[state]);
        var developed=BuildingVisuals.Resolve(map,[state with { Development=301 }]);
        var damaged=BuildingVisuals.Resolve(map,[state with { Durability=199 }]);
        Assert.Equal(4,first.Buildings.Single().CompositionId);
        Assert.Equal(1,damaged.Buildings.Single().CompositionId);
        Assert.Equal("building-100-key",first.Buildings.Single().Sprites.Single().AssetId);
        Assert.Equal("building-101-key",developed.Buildings.Single().Sprites.Single().AssetId);
        Assert.Equal(100,map.Compositions[0].Parts[0].ResourceId);
    }

    [Fact]
    public void GarrisonAndOwnerSelectCurrentGuardsAndIndependentFlagLayers()
    {
        var map=Map(); var state=new BuildingVisualState(0,1000,2,600,1000,5,10);
        var first=BuildingVisuals.Resolve(map,[state]);
        var changed=BuildingVisuals.Resolve(map,[state with { Soldiers=0,Color=49,Glyph=185 }]);
        Assert.Contains(first.Animations,a=>a.ClipId=="soldier-3");
        Assert.DoesNotContain(changed.Animations,a=>a.ClipId.StartsWith("soldier-"));
        Assert.Equal(new[]{"flag-pole-back","flag-cloth-49","flag-glyph-1-185","flag-pole-front"},changed.Animations.Select(a=>a.ClipId));
        Assert.All(changed.Animations,a=>Assert.Equal(0,a.Phase));
    }

    [Fact]
    public void RemovedBuildingProducesNeitherSpritesNorAnimationsWhileKeepingItsCatalog()
    {
        var map = Map();
        var state = new BuildingVisualState(0, 0, 2, 0, 0, 50, -1) { IsDestroyed = true };
        var removed = BuildingVisuals.Resolve(map, [state]);
        Assert.Empty(removed.Buildings);
        Assert.Empty(removed.Animations);
        Assert.Single(map.Buildings);
        Assert.Single(BuildingVisuals.Resolve(map, [state with { IsDestroyed = false, Durability = 1000 }]).Buildings);
    }

    [Fact]
    public void RuntimePlacementCreatesANewIdentityUsingExistingTypeResources()
    {
        var map = Map();
        var state = new BuildingVisualState(8000, 1000, 2, 0, 100, 5, 10)
            { Placement = new(8000, 90, 64, 0) };
        var result = BuildingVisuals.Resolve(map, [state]);
        var building = Assert.Single(result.Buildings);
        Assert.Equal(8000, building.Id);
        Assert.Equal(90, building.X);
        Assert.All(result.Animations, a => Assert.Equal(8000, a.BuildingId));
        Assert.DoesNotContain(map.Buildings, b => b.Id == 8000);
    }
}
