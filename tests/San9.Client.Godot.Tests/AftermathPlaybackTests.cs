using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Images;
using San9.Core.Simulation;
using San9.Content.Text;
using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Tests;
using San9.Content.Maps;

namespace San9.Client.Godot.Tests;

public sealed class AftermathPlaybackTests
{
    private static ScenarioInitialState Scenario() => new(ScenarioInitialState.CurrentSchemaVersion, "aftermath", 190, 1, 1, 1,
        [new(1, "home", 1, 100), new(2, "enemy", 2, 100)],
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

    [Fact]
    public void CaptureDialogueGatesOwnershipAbsorptionAndFollowingDamageTogether()
    {
        var basis = Scenario();
        var definition = basis with { Military = basis.Military! with
        {
            Places = [.. basis.Military.Places.Select(p => p with { FacilityTypeId = 9000 })],
            Facilities = [new(9000, 0, 100, new(1, 1), 0, false, true, true, 10000, null, null, null, [])]
        } };
        var session = GameSession.Start(definition, 42);
        var before = session.View;
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["home"] = "home", ["enemy"] = "enemy", ["commander"] = "commander", ["other"] = "other" });
        texts.Messages["captured"] = new([new MessageLiteral("captured")]);
        var capture = new CapturePresentationDefinition(new()
        {
            ["won"] = new("captured", []), ["lost"] = new("captured", []),
            ["won-transfer"] = new("captured", []), ["lost-transfer"] = new("captured", [])
        }, new() { [9000] = new("city", true) }, "victory", "loss", "capture");
        var result = before.World.Settlements.Single(s => s.Id == 2) with { OwnerId = 1, Soldiers = 300, Morale = 71 };
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        DomainEvent[] facts = [
            new BattleDamageEvent(before.World.Clock, 10, null, 2, 100, 0, 100, 0, 0),
            new SettlementCapturedEvent(before.World.Clock, 2, 2, new(10, false), false) { Source = source, Attacker = source, Result = result },
            new UnitAbsorbedEvent(before.World.Clock, 10, 2) { Source = source, Result = result with { Soldiers = 800 } },
            new BattleDamageEvent(before.World.Clock, 20, null, 2, 50, 0, 50, 0, 0)];
        var after = new StrategySnapshot(definition, before.World with
            { Settlements = before.World.Settlements.Select(s => s.Id == 2 ? s with { OwnerId = 2, Soldiers = 1 } : s).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts), texts,
            new([], null) { Capture = capture });
        var day = new TurnDayPresentation(before, after, facts, duration: 20, battle: plan);
        day.Advance(10);
        Assert.Single(day.PendingDialogues);
        Assert.Equal(2, day.MapFrame.Settlements[2].OwnerId);
        Assert.Equal(before.Settlements.Single(s => s.Id == 2).Soldiers, day.MapFrame.Settlements[2].Soldiers);
        Assert.DoesNotContain(10, day.MapFrame.RemovedUnits);
        day.CompleteDialogue(Assert.Single(day.PendingDialogues).Id);
        Assert.Equal(1, day.MapFrame.Settlements[2].OwnerId);
        Assert.Equal(750u, day.MapFrame.Settlements[2].Soldiers);
        Assert.Equal(71, day.MapFrame.Settlements[2].Morale);
        Assert.Contains(10, day.MapFrame.RemovedUnits);
        var frame = day.MapFrame;
        day.Advance(10, paused: true);
        Assert.Same(frame, day.MapFrame);
        Assert.False(day.IsComplete);
    }

    [Theory]
    [InlineData("unit")]
    [InlineData("morale")]
    [InlineData("capture")]
    [InlineData("destroy")]
    public void TerminalLossAndLifecyclePublishTogetherAfterTheAttackBatch(string outcome)
    {
        var basis = Scenario();
        var session = GameSession.Start(basis with
        {
            Units = basis.Units.Select(u => u with { Morale = 70 }).ToArray(),
            Information = basis.Information! with { Formations = [new(7, "formation")] }
        }, 42);
        var before = session.View;
        bool settlement = outcome is "capture" or "destroy";
        int targetId = settlement ? 2 : 20;
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        var target = new BattleObjectContext(targetId, settlement, 2, 23,
            settlement ? null : new WorldPosition(42, 7), 2, settlement);
        var result = before.World.Settlements.Single(s => s.Id == 2) with
        {
            OwnerId = outcome == "destroy" ? null : 1,
            Soldiers = outcome == "destroy" ? 0u : 300u,
            IsDestroyed = outcome == "destroy", Durability = outcome == "destroy" ? 0 : 100
        };
        DomainEvent terminal = outcome switch
        {
            "capture" => new SettlementCapturedEvent(before.World.Clock, 2, 2, new(10, false), false)
                { Source = source, Attacker = source, Result = result },
            "destroy" => new FacilityDestroyedEvent(before.World.Clock, 2, new(10, false))
                { Source = source, PreviousOwnerId = 2, Result = result },
            _ => new UnitTerminatedEvent(before.World.Clock, 20, new(10, false), 0)
                { Source = source, Target = target, Reason = outcome == "morale" ? UnitTerminationReason.MoraleCollapsed : UnitTerminationReason.Defeated }
        };
        uint soldiers = settlement ? before.Settlements.Single(s => s.Id == 2).Soldiers : before.World.Units.Single(u => u.Id == 20).Soldiers;
        DomainEvent[] facts = [
            new BattleDamageEvent(before.World.Clock, 10, settlement ? null : 20, settlement ? 2 : null, 10, 0, 10, 0, 0),
            new BattleDamageEvent(before.World.Clock, 10, settlement ? null : 20, settlement ? 2 : null,
                outcome == "morale" ? 0 : soldiers - 10, 0, 0, outcome == "destroy" ? 100 : 0, outcome == "morale" ? 100 : 0),
            terminal,
            new BattleDamageEvent(before.World.Clock, 10, null, 1, 1, 0, 1, 0, 0)];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion,
            new() { ["home"] = "home", ["enemy"] = "enemy", ["commander"] = "commander", ["other"] = "other" });
        var battle = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, new([], null));
        var catalog = UnitVisualFixture.Catalog with
        {
            Formations = [UnitVisualFixture.Catalog.Formations[0] with { FormationId = 7 }],
            Bodies = UnitVisualFixture.Catalog.Bodies.Select(b => b with
            {
                MeleeAttack = new([new(Enumerable.Repeat("attack", 8).ToArray()) { BlocksDayProgress = true }], MapProjectileKind.None, 0),
                RangedAttack = null, Flags = Enumerable.Range(0, 8).Select(_ => Array.Empty<MapUnitFlagPlacement>()).ToArray()
            }).ToArray()
        };
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "aftermath", "spring", 11, 2, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 22).ToArray(), [], [], [], [], [],
            [new(1, 0, 1, 0), new(2, 10, 1, 0)], Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new([.. UnitVisualFixture.Clips.Values, new("attack", ["body", "body"], 1000, [])], []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        var mapPlan = new MapDayPlan(map, before, facts);
        var day = new TurnDayPresentation(before, before, facts, mapPlan, battle: battle)
            { Viewport = () => new(-100000, -100000, 200000, 200000) };
        var scene = new MapScene(map, new San9.Rendering.TerrainRenderer(map, [], []), "spring", before,
            new Dictionary<string, ImageAsset>(), () => 0, day: day);
        day.Advance(1);
        Assert.True(day.IsCombatBlocking);
        scene = scene.RefreshMapFrame();
        Assert.Equal(soldiers - 10, settlement ? scene.Settlements[2].Soldiers : scene.Sample(scene.Units.Single(u => u.Id == 20)).Soldiers);
        if (!settlement) Assert.True(scene.Sample(scene.Units.Single(u => u.Id == 20)).Morale > 0);
        Assert.DoesNotContain(20, day.MapFrame.RemovedUnits);
        Assert.Equal(2, day.MapFrame.Settlements[2].OwnerId);
        Assert.False(day.MapFrame.Settlements[2].IsDestroyed);
        day.Advance(2);
        scene = scene.RefreshMapFrame();
        if (settlement)
        {
            Assert.Equal(result.Soldiers, scene.Settlements[2].Soldiers);
            Assert.Equal(result.OwnerId, scene.Settlements[2].OwnerId);
            Assert.Equal(result.IsDestroyed, scene.Settlements[2].IsDestroyed);
        }
        else Assert.DoesNotContain(scene.Units, u => u.Id == 20);
    }

    [Fact]
    public void ArrivalAndDestructionUpdateTheMapWhileUnrelatedPresentationContinues()
    {
        var session = GameSession.Start(Scenario(), 42);
        var before = session.View;
        var home = before.World.Settlements.Single(s => s.Id == 1);
        var result = home with { Soldiers = home.Soldiers + 500, Morale = 73 };
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        DomainEvent[] facts = [new UnitAbsorbedEvent(before.World.Clock, 10, 1) { Source = source, Result = result },
            new FacilityDestroyedEvent(before.World.Clock, 2, new(10, false)) { Source = source, PreviousOwnerId = 2,
                Result = before.World.Settlements.Single(s => s.Id == 2) with
                    { IsDestroyed = true, OwnerId = null, CorpsId = null, Soldiers = 0, Wounded = 0, Durability = 0, Morale = 0 } }];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, new() { ["home"] = "home", ["enemy"] = "enemy", ["commander"] = "commander", ["other"] = "other" });
        var plan = BattlePresentationPlan.Create(before, before, facts, session.Messages(texts), texts, new([], null));
        var day = new TurnDayPresentation(before, before, facts, duration: 20, battle: plan);
        var initial = day.MapFrame;
        day.Advance(.99);
        Assert.Same(initial, day.MapFrame);
        day.Advance(.01);
        Assert.Contains(10, day.MapFrame.RemovedUnits);
        Assert.Equal(result.Soldiers, day.MapFrame.Settlements[1].Soldiers);
        Assert.Equal(73, day.MapFrame.Settlements[1].Morale);
        var arrived = day.MapFrame;
        day.Advance(10, paused: true);
        Assert.Same(arrived, day.MapFrame);
        day.Advance(.5);
        Assert.True(day.MapFrame.Settlements[2].IsDestroyed);
        Assert.False(day.IsComplete);
        Assert.Equal(home.Soldiers, session.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        day.SkipVisual();
        Assert.True(day.MapFrame.Settlements[2].IsDestroyed);
    }

    [Theory]
    [InlineData(UnitTerminationReason.Defeated, true, true)]
    [InlineData(UnitTerminationReason.MoraleCollapsed, true, true)]
    [InlineData(UnitTerminationReason.Defeated, false, false)]
    [InlineData(UnitTerminationReason.ForceEliminated, true, false)]
    [InlineData(UnitTerminationReason.Surrendered, true, false)]
    public void RetiredBodiesLeaveSelectionAndSurvivePauseDailyPublicationAndTheNextDay(
        UnitTerminationReason reason, bool visible, bool retires)
    {
        var basis = Scenario();
        var session = GameSession.Start(basis with
        {
            Units = basis.Units.Select(u => u with { Morale = 70 }).ToArray(),
            Information = basis.Information! with { Formations = [new(7, "formation")] }
        }, 42);
        var before = session.View;
        var after = before.ForWorld(before.World with
        {
            Units = before.World.Units.Where(u => u.Id != 20).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.UnitId == 20 ? p with { UnitId = null, SettlementId = 2 } : p).ToImmutableArray()
        });
        var target = new BattleObjectContext(20, false, 2, 23, new(42, 7), 2, false);
        DomainEvent[] facts = [new UnitTerminatedEvent(before.World.Clock, 20,
            reason == UnitTerminationReason.MoraleCollapsed ? null : new(10, false), 0)
            { Reason = reason, Target = target, Source = null }];
        var texts = new TextCatalog(TextCatalog.CurrentSchemaVersion, []);
        var battle = BattlePresentationPlan.Create(before, after, facts, session.Messages(texts), texts, new([], null));
        var catalog = UnitRetirementPlaybackTests.Catalog with
        {
            Formations = [UnitRetirementPlaybackTests.Catalog.Formations[0] with { FormationId = 7 }],
            Bodies = UnitRetirementPlaybackTests.Catalog.Bodies.Select(b => b with
                { Flags = Enumerable.Range(0, 8).Select(_ => Array.Empty<MapUnitFlagPlacement>()).ToArray() }).ToArray()
        };
        var assets = UnitRetirementPlaybackTests.Assets;
        var map = new MapPackage(MapPackage.CurrentSchemaVersion, "fixture", "retirement", "spring", 11, 2, 13056, 6656,
            Enumerable.Repeat(new MapSquare(0, 1, 0, 0), 22).ToArray(), [], assets.Values.ToArray(), [], [], [],
            [new(1, 0, 1, 0), new(2, 10, 1, 0)], Enumerable.Range(1, 14).Select(id => new MapComposition(id, [])).ToArray(),
            [], [], [], [], new(UnitRetirementPlaybackTests.Clips.Values.ToArray(), []), [])
            { BuildingTypes = [new(0, 100)], Units = catalog };
        var terrain = new San9.Rendering.TerrainRenderer(map, [], []);
        var day = new TurnDayPresentation(before, after, facts, new MapDayPlan(map, before, facts), battle: battle)
            { Viewport = () => visible ? new(-100000, -100000, 200000, 200000) : null };
        double clock = 0;
        var scene = new MapScene(map, terrain, "spring", before, assets, () => clock, day: day);
        day.Advance(1);
        scene = scene.RefreshMapFrame();
        Assert.DoesNotContain(scene.Units, u => u.Id == 20);
        Assert.Equal(retires, scene.HasRetirements);
        if (!retires) { Assert.Empty(scene.RetiringSprites); return; }
        Assert.NotEmpty(scene.RetiringSprites);
        var targets = new List<MapObjectTarget?>();
        scene.Select(new(-100000, -100000, 200000, 200000), false)
            .Objects(0, (_, _, targetObject, _, _, _) => targets.Add(targetObject));
        Assert.DoesNotContain(new MapObjectTarget(20, true), targets);
        var first = Assert.Single(scene.RetiringSprites);
        day.Advance(.4, paused: true);
        Assert.True(Assert.Single(scene.RetiringSprites).Frame > first.Frame);
        var published = new MapScene(map, terrain, "spring", after, assets, () => clock, scene);
        Assert.Equal(scene.RetiringSprites.ToArray(), published.RetiringSprites.ToArray());
        var nextDay = new TurnDayPresentation(after, after, [], new MapDayPlan(map, after, []));
        var next = new MapScene(map, terrain, "spring", after, assets, () => clock, published, nextDay);
        nextDay.Advance(1);
        Assert.True(nextDay.IsComplete);
        Assert.NotEmpty(next.RetiringSprites);
        var stable = new MapScene(map, terrain, "spring", after, assets, () => clock, next);
        clock = 1000;
        Assert.Empty(stable.RetiringSprites);
        Assert.False(stable.PruneRetirements().HasRetirements);
    }
}
