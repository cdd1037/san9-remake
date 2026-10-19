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
        var message = Assert.Single(CapturePresentation.Read(before, new(definition, before.World, texts), texts, content, fact));
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
        Assert.Empty(CapturePresentation.Read(before, context, texts, content, Fact(before, 1, 2) with { Destroyed = true }));
        Assert.Empty(CapturePresentation.Read(before, context, texts, content, Fact(before, 2, 3)));
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
        Assert.Contains(0, plan.PresentedFacts);
        Assert.Empty(ExecutionPublication.Read(before, after, context, texts, content, facts, presentedFacts: plan.PresentedFacts).Messages);
        var fallback = Assert.Single(ExecutionPublication.Read(before, after, context, texts, content, facts).Messages);
        Assert.Equal(cue.Message, fallback);
        DomainEvent[] earlierSuccession = [facts[1], facts[0]];
        var changed = BattlePresentationPlan.Create(before, after, earlierSuccession, context, texts, content);
        Assert.Equal("actor", Assert.Single(changed.Messages).Message.Message.BbCode);
    }

    [Fact]
    public async Task RealCaptureBindingsResolveMusicPicturesAndTextFromTheConvertedPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await San9.Content.Packages.ClientContentFiles.LoadAsync(root);
        var session = GameSession.Start(package.Scenario, 42);
        var before = session.View;
        int player = before.PlayerForceId;
        int enemy = before.World.Forces.First(f => f.Id != player && !f.IsDormant && !f.IsEliminated && f.LeaderId is not null).Id;
        var capture = package.Presentation.Capture!;
        foreach (var place in before.Settlements.Where(s => s.FacilityTypeId is not null).DistinctBy(s => s.FacilityTypeId))
        foreach (bool won in new[] { true, false })
        {
            int winner = won ? player : enemy;
            var source = new BattleObjectContext(100000, false, winner,
                before.World.Forces.Single(f => f.Id == winner).LeaderId, new(0, 0), place.Id, false);
            var fact = new SettlementCapturedEvent(before.World.Clock, place.Id, won ? enemy : player, new(source.Id, false), false) { Source = source };
            var message = Assert.Single(CapturePresentation.Read(before, session.Messages(package.Text), package.Text, capture, fact));
            Assert.NotEmpty(message.Message.BbCode);
            Assert.True(package.Skin.Roles.ContainsKey(message.IllustrationRole!));
            Assert.Equal(place.Kind == SettlementKind.City ? won ? capture.VictoryMusic : capture.LossMusic : null, message.BackgroundMusicRole);
        }
    }

    private static SettlementCapturedEvent Fact(StrategySnapshot before, int winner, int? previous) =>
        new(before.World.Clock, 2, previous, new(10000, false), false)
        { Source = new(10000, false, winner, winner == 1 ? 3 : 23, new(0, 0), 1, false) };

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
        texts.Messages["lost"] = new([new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.0")]))]);
        var capture = new CapturePresentationDefinition(new()
        {
            ["won"] = new("won", new() { ["person.0"] = "ruler" }),
            ["lost"] = new("lost", new() { ["person.0"] = "actor" })
        }, new() { [9000] = new("city", true), [9001] = new("fort", false) }, "victory", "loss", "capture");
        return (definition, before, texts, capture);
    }
}
