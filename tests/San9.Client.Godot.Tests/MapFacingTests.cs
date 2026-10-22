using System.Collections.Immutable;
using San9.Application;
using San9.Application.Persistence;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class MapFacingTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DailyFacingContinuesThroughStrategyIdleDaysSeasonChangesAndLoading(bool water)
    {
        var from = new WorldPosition(7, 7);
        var to = new WorldPosition(12, 7);
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "facing", 190, 1, 1, 1,
            [new(1, "player", 0, 100)], [], [], [])
        {
            Units = [new(10, 1, from, 100) { FormationId = 10000, Morale = 100,
                Order = new(MilitaryOrderKind.Move, new(7, 17), null) }],
            Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(),
                [new(0, water ? null : 1, water ? 1 : null) { IsWater = water }],
                [new(10000, 1, 50, 1000, 5, new(1, 1), water ? TravelMode.Water : TravelMode.Land)], []),
            Information = new([], [], [], [new CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }], [])
                { Formations = [new(10000, "formation")] }
        };
        var before = GameSession.Start(definition, 1);
        var afterDefinition = definition with { Units = [definition.Units[0] with { Position = to, Facing = WorldDirection.East }] };
        var after = GameSession.Start(afterDefinition, 1);
        var catalog = UnitVisualFixture.TrailCatalog;
        if (water) catalog = catalog with { Bodies = catalog.Bodies.Select(b => b with
        {
            Flags = Enumerable.Range(0, 8).Select(d => new[] { new MapUnitFlagPlacement(2 + d, -84 + d, 2),
                new MapUnitFlagPlacement(-16 + d, -93 + d, -1) }).ToArray()
        }).ToArray() };
        var map = new MapPackage(16, "fixture", "facing", "spring", 4, 4, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 16).ToArray(), [], [], [], [], [], [], [], [], [], [], [],
            new(UnitVisualFixture.Clips.Values.ToArray(), []), []) { Units = catalog };
        var terrain = new TerrainRenderer(map, [], []);
        DomainEvent[] events = [new UnitMovedEvent(before.Snapshot.Clock, 10, from, to, 5, NavigationStatus.Moving)];
        var day = new TurnDayPresentation(before.View, after.View, events, new MapDayPlan(map, before.View, events));
        var assets = new Dictionary<string, San9.Content.Images.ImageAsset>();
        var initial = new MapScene(map, terrain, "spring", before.View, assets, () => 0);
        var baseline = initial.Sample(Assert.Single(initial.Units));
        var execution = new MapScene(map, terrain, "spring", before.View, assets, () => 0, initial, day);
        void AssertWaitingPose()
        {
            var waiting = execution.Sample(Assert.Single(execution.Units));
            Assert.Equal((baseline.AnchorX, baseline.AnchorY, baseline.Direction, UnitVisualAction.Idle),
                (waiting.AnchorX, waiting.AnchorY, waiting.Direction, waiting.Action));
            Assert.Equal(baseline.Animations.Select(a => (a.ClipId, a.X, a.Y)),
                waiting.Animations.Select(a => (a.ClipId, a.X, a.Y)));
        }
        AssertWaitingPose();
        day.Advance(.35, paused: true);
        execution = new MapScene(map, terrain, "spring", before.View, assets, () => 0, execution, day);
        AssertWaitingPose();
        day.Advance(.01);
        var started = execution.Sample(Assert.Single(execution.Units));
        Assert.True(started.AnchorX > baseline.AnchorX);
        Assert.Equal(UnitVisualAction.Moving, started.Action);
        Assert.Contains(started.Animations, a => a.ClipId == "influence");
        day.Advance(2);
        var final = execution.Sample(Assert.Single(execution.Units));
        Assert.Equal(2, final.Direction);
        var strategy = new MapScene(map, terrain, "spring", after.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, execution);
        var idleDay = new TurnDayPresentation(after.View, after.View, [], new MapDayPlan(map, after.View, []));
        var idle = new MapScene(map, terrain, "spring", after.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, strategy, idleDay);
        idleDay.Advance(2);
        var winter = new MapScene(map, terrain, "winter", after.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, idle);
        var restored = SaveService.Decode(SaveService.Encode(after), afterDefinition);
        var loaded = new MapScene(map, terrain, "winter", restored.View, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0);
        foreach (var scene in new[] { strategy, idle, winter, loaded })
        {
            var sample = scene.Sample(Assert.Single(scene.Units));
            Assert.Equal(final.Direction, sample.Direction);
            Assert.Equal(UnitVisualAction.Idle, sample.Action);
            Assert.Contains(sample.Animations, a => a.IsBody && a.ClipId == "body-2");
            Assert.Contains(sample.Animations, a => a.ClipId == "unit-flag-2-cloth-0");
            if (water)
            {
                var flags = sample.Animations.Where(a => a.ClipId == "unit-flag-2-cloth-0").ToArray();
                Assert.Equal(2, flags.Length);
                Assert.Equal((sample.AnchorX + 4, sample.AnchorY - 82), (flags[0].X, flags[0].Y));
                Assert.Equal((sample.AnchorX - 14, sample.AnchorY - 91), (flags[1].X, flags[1].Y));
            }
        }
    }
}
