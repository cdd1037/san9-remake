using San9.Application;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Simulation;
using San9.Content.Text;
using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Tests;

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
            ["won"] = new("captured", []), ["lost"] = new("captured", [])
        }, new() { [9000] = new("city", true) }, "victory", "loss", "capture");
        var result = before.World.Settlements.Single(s => s.Id == 2) with { OwnerId = 1, Soldiers = 300, Morale = 71 };
        var source = new BattleObjectContext(10, false, 1, 3, new(12, 7), 1, false);
        DomainEvent[] facts = [
            new BattleDamageEvent(before.World.Clock, 10, null, 2, 100, 0, 100, 0, 0),
            new SettlementCapturedEvent(before.World.Clock, 2, 2, new(10, false), false) { Source = source, Result = result },
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
        Assert.Equal(before.Settlements.Single(s => s.Id == 2).Soldiers - 100, day.MapFrame.Settlements[2].Soldiers);
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
}
