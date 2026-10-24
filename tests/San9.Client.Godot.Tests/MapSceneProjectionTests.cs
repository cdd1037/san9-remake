using San9.Application;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapSceneProjectionTests
{
    [Fact]
    public void SharedBaseProjectionIsIsolatedByBothInputsAndPresentationFramesDoNotMutateIt()
    {
        var (state, map) = Fixture();
        var cache = new MapSceneProjectionCache();
        var baseline = cache.Get(state, map);
        Assert.Same(baseline, cache.Get(state, map));
        Assert.NotSame(baseline, cache.Get(state, map with { Height = 2 }));
        Assert.NotSame(baseline, cache.Get(state.ForWorld(state.World with { }), map));
        var frame = new BattleMapFrame(state, [], [new(0, 1, true, -10, -5, -50)]);
        var projected = baseline.ForDay(state, map, frame, state);
        var expected = MapSceneProjection.Create(state, map, frame, state);
        Assert.Equal(expected.BuildingStates.OrderBy(s => s.Id), projected.BuildingStates.OrderBy(s => s.Id));
        Assert.Equal(expected.Buildings.Select(b => (b.Id, b.CompositionId)), projected.Buildings.Select(b => (b.Id, b.CompositionId)));
        Assert.Equal(expected.Animations, projected.Animations);
        Assert.Equal(expected.CombatAnimations, projected.CombatAnimations);
        Assert.Same(baseline.Units, projected.Units);
        Assert.Equal(100u, baseline.BuildingStates.Single(s => s.Id == 1).Soldiers);
        Assert.Same(baseline, new MapDayPlan(map, state, [], baseline).Projection);
    }

    [Fact]
    public void ReusedObjectPlanPreservesStableDepthOrderAndOverlayPlacement()
    {
        var (state, map) = Fixture();
        var scene = new MapScene(map, new TerrainRenderer(map, [], []), "spring", state,
            new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0);
        var region = new MapSceneRegion(scene, new(0, 0, 100, 100),
            [new(1, 0, 0, 0, 1, [new("tie-a", 0, 0) { Depth = 30000 },
                new("early", 0, 0) { Depth = 1 }, new("tie-b", 0, 0) { Depth = 30000 }])],
            [], [new("bridge", 0, 0)], [], [new("cloud", 0, 0)], []);
        for (int pass = 0; pass < 2; pass++)
        {
            var order = new List<string>();
            region.Objects(0, (asset, _, _, _, _, _) => order.Add(asset),
                () => order.Add("ground"), (-8, () => order.Add("overlay")));
            Assert.Equal(["bridge", "ground", "early", "tie-a", "tie-b", "overlay", "cloud"], order);
        }
    }

    [Fact]
    public void NumericFeedbackRetainsGeometryAndUnitProjection()
    {
        var (state, map) = Fixture();
        var before = new BattleMapFrame(state, [], []);
        var projection = MapSceneProjection.Create(state, map, before, state);
        var frame = new BattleMapFrame(state, [], [new(1, 1, true, -10, -5, -1)]);
        var updated = projection.WithFrame(state, map, before, frame, state);

        Assert.Equal(90u, updated.BuildingStates.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(55, updated.BuildingStates.Single(s => s.Id == 1).Morale);
        Assert.Same(projection.Buildings, updated.Buildings);
        Assert.Same(projection.Animations, updated.Animations);
        Assert.Same(projection.CombatAnimations, updated.CombatAnimations);
        Assert.Same(projection.Units, updated.Units);
        Assert.Equal(100u, state.Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DamageThresholdAndDestructionUpdateOnlyTheAffectedBuilding(bool destroyed)
    {
        var (state, map) = Fixture();
        var before = new BattleMapFrame(state, [], []);
        var projection = MapSceneProjection.Create(state, map, before, state);
        var frame = destroyed
            ? new BattleMapFrame(state, [new(1, 0, null, state.Settlements.Single(s => s.Id == 1) with { IsDestroyed = true })], [])
            : new BattleMapFrame(state, [], [new(1, 1, true, 0, 0, -50)]);
        var updated = projection.WithFrame(state, map, before, frame, state);
        var expected = MapSceneProjection.Create(state, map, frame, state);

        Assert.Same(projection.Buildings.Single(b => b.Id == 2), updated.Buildings.Single(b => b.Id == 2));
        Assert.Equal(expected.Buildings.Select(b => (b.Id, b.CompositionId)), updated.Buildings.Select(b => (b.Id, b.CompositionId)));
        Assert.All(updated.Buildings, building =>
            Assert.Equal(expected.Buildings.Single(b => b.Id == building.Id).Sprites, building.Sprites));
        Assert.Equal(expected.Animations, updated.Animations);
        Assert.Equal(expected.CombatAnimations, updated.CombatAnimations);
        Assert.Same(projection.Units, updated.Units);
    }

    private static (StrategySnapshot State, MapPackage Map) Fixture()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "feedback", 190, 1, 1, 5,
            [new(5, "player", 0, 100)],
            [new(1, "city-1", 5, 0, 0, 100, 100, 100, 60, []),
                new(2, "city-2", 5, 1, 0, 100, 100, 100, 60, [])], [], [])
            { Information = new([], [], [], [new(5, 5, null, new(0, 0)) { IsRoot = true }], []) };
        var state = GameSession.Start(definition, 1).View;
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "feedback", "spring", 2, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 2).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 1, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([], []), []) { BuildingTypes = [new(0, 100)] };
        return (state, map);
    }
}
