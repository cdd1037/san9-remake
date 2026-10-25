using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CapturePresentationTests
{
    [Fact]
    public void ForceLossNeutralizationUsesOccurrenceStateWithoutPlayingAnOccupationScene()
    {
        var (definition, before, texts, capture) = Fixture();
        var neutral = before.World.Settlements.Single(s => s.Id == 2) with
            { OwnerId = null, CorpsId = null, Soldiers = 0, Wounded = 0, Morale = 0 };
        DomainEvent[] facts = [new SettlementNeutralizedEvent(before.World.Clock, 2, 2) { Result = neutral },
            new ForceEliminatedEvent(before.World.Clock, 2, false)];
        // Later events have already changed the authoritative snapshot again.
        var after = before.ForWorld(before.World with { Settlements = before.World.Settlements.Select(s =>
            s.Id == 2 ? s with { OwnerId = 1, Soldiers = 999 } : s).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(before, after, facts, new(definition, before.World, texts), texts,
            new([], null) { Capture = capture });
        var change = Assert.Single(plan.MapChanges);
        Assert.Equal(0, change.FactIndex);
        Assert.Empty(plan.Messages);
        Assert.Empty(plan.Records);
        var frame = new BattleMapFrame(before, plan.MapChanges, plan.Values);
        Assert.Null(frame.Settlements[2].OwnerId);
        Assert.Equal(0u, frame.Settlements[2].Soldiers);
        Assert.Equal(0, frame.Settlements[2].Morale);
        var identity = new PresentationIdentity(before, after);
        identity.Apply(facts[0]);
        Assert.Null(identity.View.Settlements.Single(s => s.Id == 2).OwnerId);
        var restored = System.Text.Json.JsonSerializer.Deserialize<DomainEvent>(
            System.Text.Json.JsonSerializer.Serialize(facts[0]));
        Assert.Equal(neutral, Assert.IsType<SettlementNeutralizedEvent>(restored).Result);
    }

    [Fact]
    public void AuthoredCaptureChoicesUseDeterministicPresentationRandomness()
    {
        var (definition, before, texts, content) = Fixture();
        texts.Messages["won"] = new([new MessageRandomChoice(
            [[new MessageLiteral("first")], [new MessageLiteral("second")]])]);
        var context = new WorldMessageContext(definition, before.World, texts);
        var fact = Fact(before, 1, 2);

        var first = Assert.Single(CapturePresentation.Create(before, context, texts, content, fact));
        var repeated = Assert.Single(CapturePresentation.Create(before, context, texts, content, fact));

        Assert.Contains(first.Message.BbCode, new[] { "first", "second" });
        Assert.Equal(first.Message.BbCode, repeated.Message.BbCode);
    }

    [Fact]
    public void MapResultsAndClampedDeltasFollowFactsInsteadOfAnimationTimes()
    {
        var (_, before, _, _) = Fixture();
        var place = before.Settlements.Single(s => s.Id == 2);
        BattleMapChangeCue[] changes = [new(8, 1, null, place with { Soldiers = 100, Morale = 20 }),
            new(1.5, 3, null, place with { Soldiers = 400, Morale = 95 })];
        BattleValueCue[] values = [new(9, 2, true, -50, 0, 0) { FactIndex = 2 },
            new(8, 2, true, 0, 20, 0) { FactIndex = 4 },
            new(1.5, 2, true, -30, -10, 0) { FactIndex = 5 }];

        var frame = new BattleMapFrame(before, changes, values);

        Assert.Equal(370u, frame.Settlements[2].Soldiers);
        Assert.Equal(90, frame.Settlements[2].Morale);
    }

    [Fact]
    public void CaptureAndAbsorptionKeepOccurrenceStateInsteadOfLeakingTheFinalWorld()
    {
        var (definition, before, texts, capture) = Fixture();
        var captured = before.World.Settlements.Single(s => s.Id == 2) with
            { OwnerId = 1, Soldiers = 100, Morale = 20, Durability = 30 };
        var absorbed = captured with { Soldiers = 400, Morale = 65 };
        var source = Fact(before, 1, 2).Source;
        DomainEvent[] facts = [Fact(before, 1, 2) with { Result = captured },
            new UnitAbsorbedEvent(before.World.Clock, 10, 2) { Source = source, Result = absorbed },
            new BattleDamageEvent(before.World.Clock, 20, null, 2, 50, 0, 50, 0, 0)];
        var after = new StrategySnapshot(definition, before.World with
            { Settlements = before.World.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 1, OwnerId = 2 } : s).ToImmutableArray() });
        var plan = BattlePresentationPlan.Create(before, after, facts, new(definition, before.World, texts), texts,
            new([], null) { Capture = capture });
        var dialogue = Assert.Single(plan.Messages);
        Assert.All(plan.MapChanges, c => Assert.Equal(dialogue.Id, c.AfterDialogueId));
        Assert.Equal(dialogue.Id, Assert.Single(plan.Values).AfterDialogueId);
        var frame = new BattleMapFrame(before, plan.MapChanges, plan.Values);
        var place = frame.Settlements[2];
        Assert.Equal(1, place.OwnerId);
        Assert.Equal(before.Forces.Single(f => f.Id == 1).ColorCode, place.CityMarkerColor);
        Assert.Equal(350u, place.Soldiers);
        Assert.Equal(65, place.Morale);
        Assert.Equal(30, place.Durability);
        Assert.Contains(10, frame.RemovedUnits);
        Assert.Equal(400u, absorbed.Soldiers);
    }

    [Theory]
    [InlineData(true, 2, false, "victory", "city")]
    [InlineData(false, 1, false, "loss", "city")]
    [InlineData(true, null, false, null, null)]
    [InlineData(true, 2, true, null, "fort")]
    [InlineData(false, 1, true, null, "fort")]
    public void CaptureSelectsTheAuthoredSceneAndSingleShotMusic(bool winnerIsPlayer, int? previous,
        bool fort, string? music, string? illustration)
    {
        var (definition, before, texts, content) = Fixture(fort);
        var fact = Fact(before, winnerIsPlayer ? 1 : 2, previous);
        var message = Assert.Single(CapturePresentation.Create(before, new(definition, before.World, texts), texts, content, fact));
        Assert.Equal(winnerIsPlayer ? "won" : "lost", message.Message.Key);
        Assert.Equal(music, message.BackgroundMusicRole);
        Assert.Equal(illustration, message.IllustrationRole);
        Assert.False(message.BackgroundMusicLoop);
        Assert.Equal(music is null ? "capture" : null, message.SoundRole);
        Assert.Equal(fact.Clock, message.Message.Clock);
    }

    [Fact]
    public void DestructionAndUninvolvedCapturesKeepTheirExistingMapReports()
    {
        var (definition, before, texts, content) = Fixture();
        var context = new WorldMessageContext(definition, before.World, texts);
        Assert.Empty(CapturePresentation.Create(before, context, texts, content, Fact(before, 1, 2) with { Destroyed = true }));
        Assert.Empty(CapturePresentation.Create(before, context, texts, content, Fact(before, 2, 3)));
    }

    [Fact]
    public void BattleOwnsTheSceneOnceAndPublicationKeepsTheLeaderAtTheCapture()
    {
        var (definition, before, texts, capture) = Fixture();
        var context = new WorldMessageContext(definition, before.World, texts);
        var content = CommandPresentation.Empty with { Capture = capture };
        // The winner is absorbed and a later succession must not rename the earlier capture result.
        var after = new StrategySnapshot(definition, before.World with
        {
            Units = [], Officers = [.. before.World.Officers.Select(p => p.UnitId is null ? p : p with { UnitId = null, SettlementId = p.OwnerId })],
            Forces = [.. before.World.Forces.Select(f => f.Id == 1 ? f with { LeaderId = 2 } : f)]
        });
        DomainEvent[] facts = [Fact(before, 1, 2), new ForceLeaderChangedEvent(before.World.Clock, 1, 1, 2)];
        var plan = BattlePresentationPlan.Create(before, after, facts, context, texts, content);
        var cue = Assert.Single(plan.Messages);
        Assert.Equal(BattleMessageChannel.Dialogue, cue.Channel);
        Assert.Equal("ruler", cue.Message.Message.BbCode);
        Assert.NotEmpty(plan.PresentedFeedback);
        Assert.Empty(ExecutionPublication.Read(before, after, context, texts, content, facts, presentedFeedback: plan.PresentedFeedback).Messages);
        var fallback = Assert.Single(ExecutionPublication.Read(before, after, context, texts, content, facts).Messages);
        Assert.Equal(cue.Message, fallback);
        DomainEvent[] earlierSuccession = [facts[1], facts[0]];
        var changed = BattlePresentationPlan.Create(before, after, earlierSuccession, context, texts, content);
        Assert.Equal("actor", Assert.Single(changed.Messages).Message.Message.BbCode);
    }

    [Fact]
    public void CrossForceReceiptUsesTwoScenesAndWaitsForTheFinalSceneBeforePublishingOwnership()
    {
        var (definition, before, texts, capture) = Fixture();
        var fact = Fact(before, 3, 2) with { Attacker = Fact(before, 1, 2).Source,
            Result = before.World.Settlements.Single(s => s.Id == 2) with { OwnerId = 3 } };
        var after = before.ForWorld(before.World with
        {
            Forces = before.World.Forces.Select(f => f.Id == 3 ? f with { LeaderId = 22 } : f).ToArray()
        });
        var content = CommandPresentation.Empty with { Capture = capture };
        var context = new WorldMessageContext(definition, before.World, texts);
        var plan = BattlePresentationPlan.Create(before, after, [fact], context, texts, content);
        Assert.Equal(new[] { "won", "won-transfer" }, plan.Messages.Select(m => m.Message.Message.Key));
        Assert.Equal("ruler", plan.Messages[0].Message.Message.BbCode);
        Assert.Equal("third-ruler", plan.Messages[1].Message.Message.BbCode);
        Assert.Equal("city", plan.Messages[0].Message.IllustrationRole);
        Assert.Null(plan.Messages[1].Message.IllustrationRole);
        Assert.Null(plan.Messages[1].Message.BackgroundMusicRole);
        Assert.Null(plan.Messages[1].Message.SoundRole);
        Assert.Equal(plan.Messages[0].Id, plan.Messages[1].AfterDialogueId);
        Assert.Equal(plan.Messages[1].Id, Assert.Single(plan.MapChanges).AfterDialogueId);
        Assert.Empty(ExecutionPublication.Read(before, after, context, texts, content, [fact],
            presentedFeedback: plan.PresentedFeedback).Messages);
        Assert.Equal(2, ExecutionPublication.Read(before, after, context, texts, content, [fact]).Messages.Count);
    }

    [Theory]
    [InlineData(1, "won", "won-transfer")]
    [InlineData(2, "lost", "lost-transfer")]
    [InlineData(3, "won", "won-transfer")]
    [InlineData(null, null, null)]
    public void CrossForceReceiptIncludesTheAttackerDefenderAndRecipientPlayerPerspectives(int? player,
        string? primary, string? transfer)
    {
        var (definition, basis, texts, content) = Fixture();
        var before = basis.ForWorld(basis.World with { Simulation = basis.World.Simulation with
            { Controls = player is int owner ? [new("player", owner, ControllerKind.Player)] : [] } });
        var fact = Fact(before, 3, 2) with { Attacker = Fact(before, 1, 2).Source };
        var messages = CapturePresentation.Create(before, new(definition, before.World, texts), texts, content, fact);
        Assert.Equal(primary is null ? [] : new[] { primary, transfer! }, messages.Select(m => m.Message.Key));
    }

    [Fact]
    public async Task RealCaptureBindingsResolveMusicPicturesAndTextFromTheConvertedPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await San9.Content.Packages.ClientContentFiles.LoadAsync(root);
        int selected = package.Scenario.PlayerForceId ?? package.Scenario.Forces.First(f => !f.IsDormant &&
            !f.IsBarbarian && f.LeaderId is not null).Id;
        var session = GameSession.Start(package.Scenario, 42, playerForceIds: [selected]);
        var before = session.View;
        int player = before.RequirePlayerForce();
        int enemy = before.World.Forces.First(f => f.Id != player && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null).Id;
        int recipient = before.World.Forces.First(f => f.Id != player && f.Id != enemy && !f.IsDormant &&
            !f.IsEliminated && f.LeaderId is not null).Id;
        var capture = package.Presentation.Capture!;
        foreach (var place in before.Settlements.Where(s => s.FacilityTypeId is not null).DistinctBy(s => s.FacilityTypeId))
        foreach (bool won in new[] { true, false })
        {
            int winner = won ? player : enemy;
            var source = new BattleObjectContext(100000, false, winner,
                before.World.Forces.Single(f => f.Id == winner).LeaderId, new(0, 0), place.Id, false);
            var fact = new SettlementCapturedEvent(before.World.Clock, place.Id, won ? enemy : player, new(source.Id, false), false) { Source = source, Attacker = source, Result = before.World.Settlements.Single(s => s.Id == place.Id) };
            var message = Assert.Single(CapturePresentation.Create(before, session.Messages(package.Text), package.Text, capture, fact));
            Assert.NotEmpty(message.Message.BbCode);
            Assert.True(package.Skin.Roles.ContainsKey(message.IllustrationRole!));
            Assert.Equal(place.Kind == SettlementKind.City ? won ? capture.VictoryMusic : capture.LossMusic : null, message.BackgroundMusicRole);
            var receivingSource = source with { Id = 100001, ForceId = recipient,
                CommanderId = before.World.Forces.Single(f => f.Id == recipient).LeaderId };
            var split = fact with { Source = receivingSource, Winner = new(receivingSource.Id, false) };
            var messages = CapturePresentation.Create(before, session.Messages(package.Text), package.Text, capture, split);
            Assert.Equal(2, messages.Count);
            Assert.Equal(won ? "messages.7388" : "messages.7390", messages[1].Message.Key);
            Assert.NotEmpty(messages[1].Message.BbCode);
            Assert.Null(messages[1].IllustrationRole);
            Assert.Null(messages[1].BackgroundMusicRole);
            var records = ProgressMessages.Read(before, before, session.Messages(package.Text), package.Text,
                package.Presentation.Progress!, [split]);
            Assert.Equal(won ? new[] { "messages.10095", "messages.10096" } : new[] { "messages.10093", "messages.10094" },
                records.Select(r => r.Message.Key));
        }
    }

    private static SettlementCapturedEvent Fact(StrategySnapshot before, int winner, int? previous) =>
        new(before.World.Clock, 2, previous, new(10000, false), false)
        { Source = new(10000, false, winner, winner == 1 ? 3 : 23, new(0, 0), 1, false),
            Attacker = new(10000, false, winner, winner == 1 ? 3 : 23, new(0, 0), 1, false), Result = before.World.Settlements.Single(s => s.Id == 2) };

    private static (ScenarioInitialState Definition, StrategySnapshot Before, TextCatalog Texts, CapturePresentationDefinition Capture) Fixture(bool fort = false)
    {
        var basis = MissionScenario.Create();
        var definition = basis with { Military = basis.Military! with
        {
            Places = [.. basis.Military.Places.Select(p => p with { FacilityTypeId = fort ? 9001 : 9000 })],
            Facilities = [new(9000, 0, 100, new(1, 1), 0, false, true, true, 10000, null, null, null, []),
                new(9001, 0, 100, new(1, 1), 0, false, true, true, 10000, null, null, null, [])]
        } };
        var before = new StrategySnapshot(definition, GameSession.Start(basis, 1).Snapshot);
        var texts = MissionScenario.Texts(before);
        texts.Messages["won"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        texts.Messages["won-transfer"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        texts.Messages["lost-transfer"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        texts.Messages["lost"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        var capture = new CapturePresentationDefinition(new()
        {
            ["won"] = new("won", new() { ["person.0"] = "ruler" }),
            ["lost"] = new("lost", new() { ["person.0"] = "actor" }),
            ["won-transfer"] = new("won-transfer", new() { ["person.0"] = "ruler" }),
            ["lost-transfer"] = new("lost-transfer", new() { ["person.0"] = "ruler" })
        }, new() { [9000] = new("city", true), [9001] = new("fort", false) }, "victory", "loss", "capture");
        return (definition, before, texts, capture);
    }
}
