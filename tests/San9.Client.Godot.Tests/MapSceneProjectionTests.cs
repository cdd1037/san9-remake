using San9.Application;
using San9.Application.Presentation;
using San9.Content.Maps;
using San9.Content.Scenarios;

namespace San9.Client.Godot.Tests;

public sealed class MapSceneProjectionTests
{
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
