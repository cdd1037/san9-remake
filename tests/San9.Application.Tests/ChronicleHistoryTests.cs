using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

internal static class ChronicleTestContent
{
    internal static ChroniclePresentationDefinition Definition() => new(
        new[] { "rule", "rule-ranked", "adviser", "adviser-appointed", "succession" }.ToDictionary(r => r, r => new DecisionMessageDefinition("chronicle." + r,
            new() { ["person"] = "ruler", ["actor"] = "actor", ["place"] = "place", ["name"] = "name" })),
        []);

    internal static TextCatalog AddTo(TextCatalog texts)
    {
        var messages = new Dictionary<string, MessageDefinition>(texts.Messages);
        foreach (string role in new[] { "adviser", "succession" }) messages["chronicle." + role] = new([new MessageLiteral(role)]);
        messages["chronicle.rule"] = new([Person("person"), new MessageLiteral(" at "), new MessageValue(new("property", "settlement.name", Arguments: [new("binding", "place")]))]);
        messages["chronicle.rule-ranked"] = new([new MessageValue(new("binding", "name")), new MessageLiteral(" "), Person("person")]);
        messages["chronicle.adviser-appointed"] = new([Person("person"), new MessageLiteral(" appointed "), Person("actor")]);
        return texts with { Messages = messages };
    }
    private static MessageNode Person(string slot) => new MessageValue(new("property", "person.name", Arguments: [new("binding", slot)]));
}

public sealed class ChronicleHistoryTests
{
    [Theory]
    [InlineData(PrisonerExecutionPath.CapturedRulerDisposition, "execution", new[] { 1, 2, 3 })]
    [InlineData(PrisonerExecutionPath.StrategicCommand, "execution-local", new[] { 2 })]
    public void ExecutionUsesItsOccurrenceAfterAllegianceAndLeadershipChange(PrisonerExecutionPath path, string role, int[] owners)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var before = session.View;
        var clock = before.World.Clock;
        // Officer 2 belongs to force 1 at the operation's start, then joins and succeeds force 2.
        var facts = ChronicleHistory.Read(before, before,
            [new ForceLeaderChangedEvent(clock, 2, 21, 2),
             new PrisonerExecutedEvent(clock, 3, 2, new(2, true, 31, path)),
             new ForceEliminatedEvent(clock, 2, false)]);
        var execution = facts.Where(f => f.Role is "execution" or "execution-local").ToArray();
        Assert.Equal(owners, execution.Select(f => f.ForceId));
        Assert.All(execution, f =>
        {
            Assert.Equal(role, f.Role);
            Assert.Equal(2, f.Values["actor"].Entity!.Id);
            Assert.Equal(31, f.Values["other"].Entity!.Id);
        });

    }

    [Theory]
    [InlineData(AttackRequestEndReason.TargetOwnerChanged, "request-owner-changed")]
    [InlineData(AttackRequestEndReason.TargetDestroyed, "request-place-destroyed")]
    public void RequestInvalidationDoesNotUseLaterEliminationOrReconstruction(AttackRequestEndReason reason, string role)
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var before = session.View;
        var clock = before.World.Clock;
        var request = new AttackRequestState(2, 1, 3, 3, 10);
        var after = new StrategySnapshot(definition, before.World with
        {
            Forces = before.World.Forces.Select(f => f.Id == 2 ? f with { IsEliminated = true, LeaderId = null } : f).ToImmutableArray(),
            Settlements = before.World.Settlements.Select(s => s.Id == 3 ? s with { OwnerId = 1, IsDestroyed = false } : s).ToImmutableArray()
        });
        var facts = ChronicleHistory.Read(before, after,
            [new AttackRequestEndedEvent(clock, request, reason, null), new ForceEliminatedEvent(clock, 2, false)]);
        Assert.Equal(new[] { role, role + "-peer" }, facts.Where(f => f.Role.StartsWith("request-")).Select(f => f.Role));
    }

    [Theory]
    [InlineData(AttackRequestEndReason.RequesterForceEliminated, 1)]
    [InlineData(AttackRequestEndReason.RequestedForceEliminated, 2)]
    [InlineData(AttackRequestEndReason.TargetForceEliminated, 3)]
    public void RequestEliminationNamesTheActualLostForceForBothArchives(AttackRequestEndReason reason, int lost)
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var view = session.View;
        var facts = ChronicleHistory.Read(view, view,
            [new AttackRequestEndedEvent(view.World.Clock, new(2, 1, 3, 3, 10), reason, null)]);
        Assert.Equal(new[] { 1, 2 }, facts.Select(f => f.ForceId));
        Assert.All(facts, f => Assert.Equal(view.World.Forces.Single(force => force.Id == lost).LeaderId, f.Values["name"].Entity!.Id));
    }

    [Fact]
    public void CommittedHistorySurvivesBackgroundPublicationPlayerLoadAndRecordingReplay()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var texts = ChronicleTestContent.AddTo(MissionScenario.Texts(session.View));
        var content = ChronicleTestContent.Definition();
        var initial = ChroniclePresentation.Read(session, texts, content).Lines;
        session.BeginRecording();
        var id = Guid.NewGuid();
        var request = session.Request(new CommandOperation(new AppointAdviserCommand(id, 1, session.Snapshot.Simulation.NextSequence, 2)), id: id);
        var pending = session.StartOperation(request);
        Assert.True(SpinWait.SpinUntil(() => pending.IsCompleted, TimeSpan.FromSeconds(20)));
        Assert.Equal(SessionDisposition.Accepted, pending.Complete().Disposition);
        Assert.Contains(session.History, f => f.ForceId == 1 && f.Role == "adviser-appointed");
        var beforeRetry = session.History;
        Assert.Equal(SessionDisposition.AlreadyProcessed, session.Apply(request).Disposition);
        Assert.Equal(beforeRetry, session.History);
        session.AdvanceXun();
        var saved = ChroniclePresentation.Read(session, texts, content);
        var loaded = SaveService.Load(SaveService.Encode(session), definition);
        Assert.Equal(saved.Lines, ChroniclePresentation.Read(loaded, texts, content).Lines);
        Assert.Equal(initial[0], saved.Lines[0]);
        StateAssert.Equal(session.Capture() with { World = loaded.Snapshot }, loaded.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition), verifyEvents: true);
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }

    [Fact]
    public void SuccessionKeepsTheForceHistoryAndUsesTheCurrentRulerInItsTitle()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var before = session.View;
        var clock = before.World.Clock;
        var afterWorld = before.World with
        {
            Forces = before.World.Forces.Select(f => f.Id == 1 ? f with { LeaderId = 2 } : f).ToImmutableArray(),
            Corps = before.World.Corps.Select(c => c.OwnerId == 1 && c.IsRoot ? c with { LeaderId = 2 } : c).ToImmutableArray(),
            Officers = before.World.Officers.Select(p => p.Id == 1 ? p with { Status = OfficerStatus.Dead, OwnerId = null, SettlementId = null }
                : p.Id == 2 ? p with { Status = OfficerStatus.Ruler } : p).ToImmutableArray()
        };
        var after = new StrategySnapshot(definition, afterWorld);
        var history = ChronicleHistory.Read(before, after, [new ForceLeaderChangedEvent(clock, 1, 1, 2)]);
        var restored = GameSession.Restore(definition, session.Capture() with { World = afterWorld, History = session.History.AddRange(history) });
        var texts = ChronicleTestContent.AddTo(MissionScenario.Texts(before));
        var page = ChroniclePresentation.Read(restored, texts, ChronicleTestContent.Definition());
        Assert.Equal(texts.Resolve(before.Officers.Single(p => p.Id == 2).NameKey), page.RulerName);
        Assert.Contains("ruler", page.Lines[0].BbCode);
        Assert.Contains(restored.History, f => f.ForceId == 1 && f.Role == "succession" && f.Values["other"].Entity!.Id == 2);
        Assert.Equal(before.World.Forces.Count(f => !f.IsEliminated && !f.IsDormant), history.Count);
        var once = ChronicleHistory.Append(session.History, history);
        Assert.Equal(once.Length, ChronicleHistory.Append(once, history).Length);
    }
    [Fact]
    public void AllianceFactsRetainTheirOwnContextAndDistinguishMemberDepartureFromBetrayal()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var before = session.View;
        var clock = before.World.Clock;
        var alliance = new AllianceState(3, 1, [1, 2], 12);
        var expiry = ChronicleHistory.Read(before, before,
            [new AllianceFormedEvent(clock, alliance), new AllianceEndedEvent(clock, alliance, AllianceEndReason.Expired, null)]);
        Assert.Contains(expiry, f => f.Role == "alliance-expired" && f.Values["actor"].Entity!.Id == before.World.Forces.Single(f => f.Id == 3).LeaderId);
        Assert.All(expiry.Where(f => f.Role == "alliance-expired"), f => Assert.Null(f.ForceColorCode));
        var memberBetrayal = ChronicleHistory.Read(before, before,
            [new AllianceEndedEvent(clock, alliance, AllianceEndReason.Betrayal, 2)]);
        Assert.DoesNotContain(memberBetrayal, f => f.Role == "alliance-betrayed");
        Assert.Contains(memberBetrayal, f => f.Role == "alliance-empty");
        Assert.All(memberBetrayal.Where(f => f.Role == "alliance-member-left"), f =>
            Assert.Equal(before.World.Forces.Single(f => f.Id == 2).LeaderId, f.Values["name"].Entity!.Id));
        var eliminated = ChronicleHistory.Read(before, before,
            [new AllianceMemberRemovedEvent(clock, 2, alliance, AllianceRemovalReason.ForceEliminated)]);
        Assert.All(eliminated, f => Assert.Equal("alliance-member-left", f.Role));
        var expelled = ChronicleHistory.Read(before, before,
            [new AllianceMemberRemovedEvent(clock, 2, alliance, AllianceRemovalReason.Betrayal)]);
        Assert.All(expelled, f => Assert.Equal("alliance-member-expelled", f.Role));
    }

    [Fact]
    public void ConstructionKeepsItsOccurrenceEvenWhenItsFacilityIsDestroyedOrRecapturedLater()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var before = session.View;
        var clock = before.World.Clock;
        // The end snapshot need not contain the completed object at all.
        var facts = ChronicleHistory.Read(before, before,
            [new FacilityConstructionChangedEvent(clock, 1000, 10, "completed", 100, new(1, 6, 1, new(12, 7))),
             new FacilityDemolishedEvent(clock, 1000, 1, 1, 6)]);
        Assert.Equal(new[] { "construction", "demolition" }, facts.Select(f => f.Role));
        Assert.All(facts, f =>
        {
            Assert.Equal(1, f.ForceId);
            Assert.Equal(1, f.Values["place"].Entity!.Id);
            Assert.Equal(6, f.Values["facility-name"].Number);
        });
    }

    [Fact]
    public void TerritoryTransferCapturesItsRulerBeforeSubsequentSuccessionAndKeepsTheAttackersColor()
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var before = new StrategySnapshot(definition, session.Snapshot with
        { Forces = session.Snapshot.Forces.Select(f => f with { ColorCode = f.Id + 5 }).ToImmutableArray() });
        var clock = before.World.Clock;
        var ruler = before.World.Forces.Single(f => f.Id == 2).LeaderId;
        var facts = ChronicleHistory.Read(before, before,
            [new SettlementTransferredEvent(clock, 1, 1, 2), new ForceLeaderChangedEvent(clock, 2, ruler, 22)]);
        Assert.Equal("city-surrendered", facts[0].Role);
        Assert.Equal(ruler, facts[0].Values["ruler"].Entity!.Id);
        var captured = ChronicleHistory.Read(before, before,
            [new SettlementCapturedEvent(clock, 1, 1, new(20, false), false)
                { Source = new(20, false, 2, 23, new(12, 7), 2, false) }]);
        Assert.Contains(captured, f => f.Role == "city-lost");
        Assert.All(captured, f => Assert.Equal(7, f.ForceColorCode));
    }
}
