using San9.Application;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class MapDayPlanTests
{
    [Fact]
    public void ProjectileImpactDurationIsPreparedBeforePlaybackAndSharedByIndependentClocks()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "plan", 190, 1, 1, 5,
            [new(5, "player", 5, 100)],
            [new(1, "source", 5, 0, 0, 100, 100, 100, 60, []),
             new(2, "target", 5, 10, 0, 100, 100, 100, 60, [])], [], [])
        { Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var map = new MapPackage(14, "fixture", "plan", "spring", 11, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 11).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 10, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([new("impact", ["a", "b"], 2000, [])], []), [])
        {
            BuildingTypes = [new(0, 100)],
            Units = MapUnitCatalog.Empty with { Projectiles = new() {
                [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) { Impact = new("impact", 0, 0) }
            } }
        };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 1, null, 2, 10, 0, 10, 0, 0, true)];
        string hash = session.Snapshot.StateHash;
        var plan = new MapDayPlan(map, session.View, events);
        Assert.True(plan.Duration > 4);
        var first = new TurnDayPresentation(session.View, session.View, events, plan);
        var preview = new TurnDayPresentation(session.View, session.View, events, plan);
        first.Advance(1);
        first.Advance(10, paused: true);
        Assert.Equal(0, preview.Elapsed);
        Assert.Equal(plan.Duration, first.Duration);
        Assert.Equal(first.Duration, new MapDayPlan(map, session.View, events).Duration);
        Assert.Equal(hash, session.Snapshot.StateHash);
    }
}
