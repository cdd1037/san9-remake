using San9.Tests;
using San9.Application;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Client.Godot.Tests;

public sealed class MapDayPlanTests
{
    [Theory]
    [InlineData(false, 1.6)]
    [InlineData(true, .4)]
    public void AttackWaitUsesTheBodyAtTheMovementDestination(bool startsOnWater, double expectedDuration)
    {
        var from = new WorldPosition(startsOnWater ? 7 : 2, 2);
        var to = new WorldPosition(startsOnWater ? 2 : 7, 2);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "shore", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(1, "target", 5, to.X / 5, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, from, 100) { FormationId = 10000, NavalFormationId = 20000 }],
            Military = new(2, 1, [0, 1], [new(0, 1, null), new(1, null, 1) { IsWater = true }],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land), new(20000, 1, 50, 1000, 5, new(1, 1), TravelMode.Water)],
                [new(1, to, new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "land"), new(20000, "ship")] }
        };
        var session = GameSession.Start(definition, 1);
        var catalog = new MapUnitCatalog(
            [new("land", Enumerable.Repeat("land", 8).ToArray(), 0, 0)
                { MeleeAttack = new([new(Enumerable.Repeat("land", 8).ToArray())], MapProjectileKind.None, 0) },
             new("ship", Enumerable.Repeat("ship", 8).ToArray(), 0, 0)
                { MeleeAttack = new([new(Enumerable.Repeat("ship", 8).ToArray())], MapProjectileKind.None, 0) }],
            [new(10000, [new(1, [new("land", 0, 0)], new("land", 0, 0))], 0),
             new(20000, [new(1, [new("ship", 0, 0)], new("ship", 0, 0))], 0)], [], -1);
        var map = new MapPackage(16, "fixture", "shore", "spring", 2, 1, 13056, 6656,
            [new(0, 1, 0, 0), new(1, 1, 0, 0)], [], [], [], [], [], [new(1, to.X / 5, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(), [], [], [], [],
            new([new("land", ["l0", "l1"], 200, []),
                 new("ship", ["s0", "s1", "s2", "s3", "s4", "s5", "s6", "s7"], 200, [])], []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        DomainEvent[] events = [new UnitMovedEvent(session.Snapshot.Clock, 10, from, to, 1, NavigationStatus.Arrived),
            new BattleDamageEvent(session.Snapshot.Clock, 10, null, 1, 10, 0, 10, 0, 0)];
        var plan = new MapDayPlan(map, session.View, events);
        var attack = Assert.Single(plan.Attacks);
        Assert.Equal(expectedDuration, plan.AttackDuration(attack), 8);
        var day = new TurnDayPresentation(session.View, session.View, events, plan) { CanPresentAttack = _ => true };
        day.Advance(.9 + expectedDuration / 2);
        Assert.True(day.IsAttackActive(attack));
        Assert.Equal(.9, day.Elapsed, 8);
        day.Advance(expectedDuration / 2);
        Assert.False(day.IsCombatBlocking);
        day.Advance(.1);
        Assert.True(day.IsComplete);
        Assert.Equal(1 + expectedDuration, day.VisualElapsed, 8);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void MeleeWithoutProjectilesWaitsOnlyForAnAdmittedBodyAction(bool visible)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "melee", 190, 1, 1, 5,
            [new(5, "player", 5, 100)], [new(1, "place", 5, 0, 0, 100, 100, 100, 60, [])], [], [])
        {
            Units = [new(10, 5, new(2, 2), 100) { FormationId = 10000 }],
            Military = new(1, 1, [0], [new(0, 1, null)],
                [new(10000, 1, 50, 1000, 5, new(1, 1), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]),
            Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation")] }
        };
        var session = GameSession.Start(definition, 1);
        var clips = UnitVisualFixture.Clips.Values.Append(new("melee", ["a", "b", "c", "d"], 200, [])).ToArray();
        var catalog = UnitVisualFixture.Catalog with
        {
            Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
                { MeleeAttack = new([new(Enumerable.Repeat("melee", 8).ToArray())], MapProjectileKind.None, 0) }).ToArray()
        };
        var map = new MapPackage(16, "fixture", "melee", "spring", 1, 1, 13056, 6656,
            [new(0, 1, 0, 0)], [], [], [], [], [], [new(1, 0, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(clips, []), []) { BuildingTypes = [new(0, 100)], Units = catalog };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 10, null, 1, 10, 0, 10, 0, 0)];
        var plan = new MapDayPlan(map, session.View, events);
        var attack = Assert.Single(plan.Attacks);
        Assert.Equal(MapProjectileKind.None, attack.Projectile);
        var day = new TurnDayPresentation(session.View, session.View, events, plan) { CanPresentAttack = _ => visible };
        day.Advance(1);
        Assert.Equal(visible, day.IsCombatBlocking);
        Assert.Equal(visible ? .9 : 1, day.Elapsed, 8);
        day.Advance(1);
        Assert.True(day.IsComplete);
        Assert.Equal(visible ? 1.8 : 1, day.VisualElapsed, 8);
    }

    [Fact]
    public void VisibleAttackHoldsDailyProgressWhileVisualLifetimesContinueAndIndependentViewsStayIndependent()
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "plan", 190, 1, 1, 5,
            [new(5, "player", 5, 100)],
            [new(1, "source", 5, 0, 0, 100, 100, 100, 60, []),
             new(2, "target", 5, 10, 0, 100, 100, 100, 60, [])], [], [])
        { Information = new([], [], [], [new CorpsState(5, 5, null, new(100, 100)) { IsRoot = true }], []) };
        var session = GameSession.Start(definition, 1);
        var map = new MapPackage(16, "fixture", "plan", "spring", 11, 1, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 11).ToArray(), [], [], [], [], [],
            [new(1, 0, 0, 0), new(2, 10, 0, 0)],
            Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([new("impact", ["a", "b"], 2000, [])], []), [])
        {
            BuildingTypes = [new(0, 100)],
            Units = MapUnitCatalog.Empty with
            {
                Projectiles = new()
                {
                [MapProjectileKind.Arrow] = new(["arrow"], 64, false, 0, 0) { Impact = new("impact", 0, 0) }
                }
            }
        };
        DomainEvent[] events = [new BattleDamageEvent(session.Snapshot.Clock, 1, null, 2, 10, 0, 10, 0, 0, true)];
        var hash = session.Snapshot;
        var plan = new MapDayPlan(map, session.View, events);
        var tacticPlan = new MapDayPlan(map, session.View, [((BattleDamageEvent)events[0]) with { TacticId = 99 }]);
        Assert.Empty(tacticPlan.Attacks);
        var attack = Assert.Single(plan.Attacks);
        Assert.True(plan.AttackDuration(attack) > 4);
        bool visible = true;
        var first = new TurnDayPresentation(session.View, session.View, events, plan) { CanPresentAttack = _ => visible };
        var preview = new TurnDayPresentation(session.View, session.View, events, plan);
        first.Advance(1);
        Assert.Equal(.9, first.Elapsed, 8);
        Assert.Equal(1, first.VisualElapsed, 8);
        Assert.True(first.IsCombatBlocking);
        visible = false;
        first.Advance(10, paused: true);
        Assert.Equal(.9, first.Elapsed, 8);
        Assert.False(first.IsCombatBlocking);
        first.Advance(.1);
        Assert.True(first.IsComplete);
        Assert.Equal(0, preview.Elapsed);
        preview.Advance(1);
        Assert.True(preview.IsComplete);
        Assert.False(preview.IsAttackPresented(attack));
        Assert.Equal(1, preview.VisualElapsed);
        var offscreen = new TurnDayPresentation(session.View, session.View, events, plan) { CanPresentAttack = _ => visible };
        offscreen.Advance(.9);
        visible = true;
        offscreen.Advance(.1);
        Assert.True(offscreen.IsComplete);
        Assert.False(offscreen.IsAttackPresented(attack));
        Assert.False(MapDayPlan.IsAttackVisible(attack, attack.FromX + 1000, attack.FromY, 100, 100));
        Assert.True(MapDayPlan.IsAttackVisible(attack, attack.FromX, attack.FromY, 100, 100));
        var replay = new TurnDayPresentation(session.View, session.View, events, plan) { CanPresentAttack = _ => true };
        replay.Advance(100);
        Assert.True(replay.IsComplete);
        Assert.Equal(1 + plan.AttackDuration(attack), replay.VisualElapsed, 8);
        StateAssert.Equal(hash, session.Snapshot);
    }
}
