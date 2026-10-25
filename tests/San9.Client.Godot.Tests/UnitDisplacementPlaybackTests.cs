using System.Collections.Immutable;
using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Maps;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Rendering;

namespace San9.Client.Godot.Tests;

public sealed class UnitDisplacementPlaybackTests
{
    [Fact]
    public void FormationCorrectionUsesItsOwnProgressAndDoesNotRetainMarchingQueues()
    {
        var basis = UnitVisualFixture.State with { Soldiers = 2000, Morale = 0 };
        UnitVisualState History(double time)
        {
            bool correction = time >= 225;
            double x = basis.CellX + (correction ? 2 + Math.Clamp((time - 225) / 100, 0, 1) * 3 : Math.Max(0, time) / 100);
            return basis with { CellX = x, IsMoving = time < 325, ReceivesPositionUpdates = !correction,
                Action = time < 325 ? UnitVisualAction.Moving : UnitVisualAction.Idle,
                PositionMode = correction ? UnitPositionMode.Formation : UnitPositionMode.Follow };
        }
        var playback = new UnitMotionPlayback();
        UnitVisual Sample(double time) => UnitVisualFixture.Sample(History(time), UnitVisualFixture.Catalog,
            time, history: History, playback: playback);
        _ = Sample(200);
        var start = Sample(225);
        var middle = Sample(275);
        var end = Sample(325);
        AssertWholeFormationMoves(start, middle);
        AssertWholeFormationMoves(start, end);
        var idle = Sample(700);
        AssertWholeFormationMoves(end, idle);
        var published = playback.Copy();
        published.Begin(History(700), UnitVisualFixture.Catalog, 700, 700, false);
        var next = UnitVisuals.Resolve(History(700), UnitVisualFixture.Catalog, UnitVisualFixture.Clips, published);
        AssertWholeFormationMoves(end, next);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void DisplacementsWaitForCaptureThenMoveEveryMemberTogetherWithoutMarchingDelays(int morale)
    {
        var basis = Scenario();
        var definition = basis with
        {
            Units = basis.Units.Select(u => u with { FormationId = 10000, Soldiers = 2000, Morale = morale }).Append(
                basis.Units[1] with { Id = 22, Position = new(52, 7), CommanderId = null,
                    FormationId = 10000, Soldiers = 2000, Morale = morale }).ToArray(),
            Military = basis.Military! with
            {
                Formations = [basis.Military.Formations[0] with { Id = 10000 }],
                Places = basis.Military.Places.Select(p => p with { FacilityTypeId = 0 }).ToImmutableArray(),
                Facilities = [new(0, 0, 100, new(1, 1), 0, false, true, true, 10000, null, null, null, [])]
            },
            Information = basis.Information with { Formations = [new(10000, "formation")] }
        };
        var session = GameSession.Start(definition, 42);
        var before = session.View;
        var result = before.World.Settlements.Single(s => s.Id == 2) with { OwnerId = 1 };
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new()
            { ["home"] = "home", ["enemy"] = "enemy", ["commander"] = "commander", ["other"] = "other" });
        texts.Messages["captured"] = new([new MessageLiteral("captured")]);
        var capture = new CapturePresentationDefinition(new() { ["won"] = new("captured", []), ["lost"] = new("captured", []) },
            new() { [0] = new("city", true) }, "victory", "loss", "capture");
        DomainEvent[] facts = [new UnitMovedEvent(before.World.Clock, 20, new(42, 7), new(52, 7), 10, NavigationStatus.Moving),
            new SettlementCapturedEvent(before.World.Clock, 2, 2, new(10, false), false) { Source = source, Attacker = source, Result = result },
            new UnitDisplacedEvent(before.World.Clock, 20, 2, new(52, 7), new(37, 7)),
            new UnitDisplacedEvent(before.World.Clock, 22, 2, new(52, 7), new(37, 7))];
        var after = before.ForWorld(before.World with
        {
            Settlements = before.World.Settlements.Select(s => s.Id == 2 ? result : s).ToImmutableArray(),
            Units = before.World.Units.Select(u => u.Id is 20 or 22 ? u with { Position = new(37, 7), Facing = WorldDirection.West } : u).ToImmutableArray()
        });
        var battle = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts), texts, new([], null) { Capture = capture });
        var relocations = battle.MapChanges.Where(c => c.Displacement is not null).ToArray();
        Assert.Equal(2, relocations.Length);
        Assert.Equal(relocations[0].Time, relocations[1].Time);
        Assert.Equal(.1, relocations[0].End - relocations[0].Time, 6);
        Assert.Equal(relocations[0].End, battle.Duration);
        Assert.NotNull(relocations[0].AfterDialogueId);
        var clips = UnitVisualFixture.Clips.Values.Concat(Enumerable.Range(0, 8).SelectMany(d => before.Forces.Select(f =>
            new MapAnimationClip($"unit-flag-{d}-cloth-{f.ColorCode}", ["cloth"], 100, [])))).DistinctBy(c => c.Id).ToArray();
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "displacement", "spring", 20, 3, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 60).ToArray(), [], [], [], [], [],
            [new(1, 0, 1, 0), new(2, 10, 1, 0)], Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(clips, []), [])
            { BuildingTypes = [new(0, 100)], Units = UnitVisualFixture.Catalog };
        var terrain = new TerrainRenderer(map, [], []);
        var day = new TurnDayPresentation(before, after, facts, new MapDayPlan(map, before, facts), battle: battle);
        var scene = new MapScene(map, terrain, "spring", before, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, day: day);
        UnitVisual Sample() => scene.Sample(scene.Units.Single(u => u.Id == 20));
        day.Advance(5);
        var inside = Sample();
        Assert.Equal(relocations[0].Time, day.Elapsed);
        Assert.Equal(2, day.MapFrame.Settlements[2].OwnerId);
        day.CompleteDialogue(Assert.Single(day.PendingDialogues).Id);
        Assert.Equal(1, day.MapFrame.Settlements[2].OwnerId);
        scene = scene.RefreshMapFrame();
        var start = Sample();
        Assert.Equal(inside.AnchorX, start.AnchorX);
        Assert.True(start.Animations.Count(a => a.IsBody) > 1);
        day.Advance(.025);
        var quarter = Sample();
        Assert.True(quarter.AnchorX < start.AnchorX);
        AssertWholeFormationMoves(start, quarter);
        day.Advance(.025);
        var moving = Sample();
        Assert.True(moving.AnchorX < inside.AnchorX);
        Assert.Equal(UnitVisualAction.Moving, moving.Action);
        Assert.False(day.IsComplete);
        AssertWholeFormationMoves(start, moving);
        AssertWholeFormationMoves(quarter, moving);
        day.Advance(1, paused: true);
        Assert.Equal(moving.AnchorX, Sample().AnchorX);
        Assert.Equal(moving.Animations.Where(a => a.IsBody).Select(a => (a.GroundX, a.GroundY)),
            Sample().Animations.Where(a => a.IsBody).Select(a => (a.GroundX, a.GroundY)));
        day.Advance(.06);
        Assert.True(day.IsComplete);
        var outside = Sample();
        Assert.True(outside.AnchorX < moving.AnchorX);
        AssertWholeFormationMoves(start, outside);
        var published = new MapScene(map, terrain, "spring", after, new Dictionary<string, San9.Content.Images.ImageAsset>(), () => 0, scene);
        var next = published.Sample(published.Units.Single(u => u.Id == 20));
        Assert.Equal(outside.AnchorX, next.AnchorX);
        Assert.Equal(outside.Animations.Where(a => a.IsBody).Select(a => (a.X, a.Y)),
            next.Animations.Where(a => a.IsBody).Select(a => (a.X, a.Y)));
        Assert.Equal(new WorldPosition(42, 7), session.Snapshot.Units.Single(u => u.Id == 20).Position);
    }

    private static void AssertWholeFormationMoves(UnitVisual from, UnitVisual to)
    {
        var beforeBodies = from.Animations.Where(a => a.IsBody).OrderBy(a => a.GroundY).ThenBy(a => a.GroundX).ToArray();
        var afterBodies = to.Animations.Where(a => a.IsBody).OrderBy(a => a.GroundY).ThenBy(a => a.GroundX).ToArray();
        Assert.Equal(beforeBodies.Length, afterBodies.Length);
        for (int i = 0; i < beforeBodies.Length; i++)
        {
            Assert.Equal(to.AnchorX - from.AnchorX, afterBodies[i].GroundX - beforeBodies[i].GroundX, 6);
            Assert.Equal(to.AnchorY - from.AnchorY, afterBodies[i].GroundY - beforeBodies[i].GroundY, 6);
        }
        Assert.Equal(to.AnchorX - from.AnchorX, to.LabelX - from.LabelX);
        Assert.Equal(to.AnchorY - from.AnchorY, to.LabelY - from.LabelY);
        var beforeFlags = from.Animations.Where(a => a.ClipId.Contains("unit-flag-")).ToArray();
        var afterFlags = to.Animations.Where(a => a.ClipId.Contains("unit-flag-")).ToArray();
        Assert.Equal(beforeFlags.Length, afterFlags.Length);
        for (int i = 0; i < beforeFlags.Length; i++)
        {
            Assert.Equal(to.AnchorX - from.AnchorX, afterFlags[i].X - beforeFlags[i].X);
            Assert.Equal(to.AnchorY - from.AnchorY, afterFlags[i].Y - beforeFlags[i].Y);
        }
    }

    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "aftermath", 190, 1, 1, 1,
        [new(1, "home", 0, 100), new(2, "enemy", 0, 100)],
        [new(1, "home", 1, 0, 1, 1000, 100, 100, 70, []),
            new(2, "enemy", 2, 10, 1, 1000, 100, 100, 70, [])], [], [])
    {
        Officers = [new("commander", new(3, 1, null, new(70, 70, 70, 70), false) { UnitId = 10 }),
            new("other", new(23, 2, null, new(70, 70, 70, 70), false) { UnitId = 20 })],
        Units = [new(10, 1, new(12, 7), 500) { FormationId = 7, CommanderId = 3 },
            new(20, 2, new(42, 7), 500) { FormationId = 7, CommanderId = 23 }],
        Military = new(20, 3, Enumerable.Repeat(0, 60).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 1, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(52, 7), new(1, 1))]),
        Information = new([], [], [], [new CorpsState(1, 1, null, new(0, 0)) { IsRoot = true },
            new CorpsState(2, 2, null, new(0, 0)) { IsRoot = true }], [])
    };

}
