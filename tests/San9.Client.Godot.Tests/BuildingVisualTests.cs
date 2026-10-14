using San9.Tests;
using San9.Content.Maps;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class BuildingVisualTests
{
    [Fact]
    public void DormantWorldProjectionDrivesBuildingsAndMinimapWithoutRemovingDefinitions()
    {
        var definition = new San9.Content.Scenarios.ScenarioInitialState(
            San9.Content.Scenarios.ScenarioInitialState.CurrentSchemaVersion, "dormant", 190, 1, 1, 5,
            [new(5, "active", 5, 0), new(6, "reserved", 6, 0) { IsDormant = true }],
            [new(0, "city", 6, 89, 64, 1000, 1000, 1000, 60, []) { IsDormant = true, Appearance = 2 }], [], []) { Information = new([], [], [], [new San9.Core.Domain.CorpsState(5, 5, null, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(6, 6, null, new(0, 0)) { IsRoot = true }], []) };
        var date = new San9.Core.Simulation.GameClock(190, 1, 1);
        var snapshot = new San9.Core.Simulation.WorldSnapshot(date, new(date, 17))
        {
            Forces = [new(5, 0), new(6, 0) { IsDormant = true }],
            Settlements = [new(0, 6, 1000, 1000, 1000, 60, 0, 0, 100, 0, 100) { IsDormant = true }]
        };
        var map = Map(); var hash = snapshot;
        BuildingVisuals Render(San9.Application.Presentation.StrategySnapshot view) => BuildingVisuals.Resolve(map,
            view.Settlements.Select(s => new BuildingVisualState(s.Id, s.Durability, s.Appearance, s.Commerce,
                s.Soldiers, s.CityMarkerColor ?? 50, -1)).ToArray());
        var hidden = new San9.Application.Presentation.StrategySnapshot(definition, snapshot);
        Assert.Empty(Render(hidden).Buildings); Assert.Empty(Render(hidden).Animations);
        Assert.DoesNotContain(hidden.Settlements, s => s.CityMarkerColor is not null);
        var activated = new San9.Application.Presentation.StrategySnapshot(definition, snapshot with
        {
            Forces = snapshot.Forces.Select(f => f with { IsDormant = false }).ToArray(),
            Settlements = snapshot.Settlements.Select(s => s with { IsDormant = false }).ToArray()
        });
        Assert.Single(Render(activated).Buildings);
        Assert.Single(activated.Settlements, s => s.CityMarkerColor is not null);
        Assert.Single(map.Buildings); Assert.Single(definition.Settlements);
        StateAssert.Equal(hash, snapshot);
    }

    [Fact]
    public void BuildingPartsInterleaveWithBodiesAtTheirOwnDepth()
    {
        var map = Map();
        var parts = new MapCompositionPart[] {
            new(false,false,100,0,-20) { DepthOffset=10 },
            new(false,false,101,0,20) { DepthOffset=10 },
            new(true,false,102,0,0),
            new(false,false,103,0,0) { Ground=true },
            new(false,true,104,0,0) { DepthOffset=5 }
        };
        map = map with { Compositions = map.Compositions.Select(c => c with { Parts=parts }).ToArray() };
        var building = Assert.Single(BuildingVisuals.Resolve(map,[new(0,1000,2,0,0,5,-1)]).Buildings);
        double ground = MapGeometry.Center(89,64).Y;
        var depths = building.Sprites.Select(s => s.Depth).ToArray();
        Assert.True(depths[0] < ground+30000 && depths[1] > ground+30000);
        Assert.Equal(ground+10000,depths[2]);
        Assert.Equal(1,depths[3]);
        Assert.Equal(ground+29995,depths[4]);
    }

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
