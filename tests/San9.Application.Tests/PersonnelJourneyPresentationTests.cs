using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Packages;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PersonnelJourneyPresentationTests
{
    private static (TextCatalog Texts, CommandPresentation Content) Presentation(GameSession session)
    {
        string[] roles = ["journey-interrupted", "journey-withdrawal", "journey-interrupted-record", "journey-arrived", "journey-arrived-record"];
        var texts = MissionScenario.Texts(session.View) with
        {
            Messages = roles.ToDictionary(r => r, r => new MessageDefinition(r.StartsWith("journey-arrived", StringComparison.Ordinal)
                ? [new MessageValue(new("binding", "count")), new MessageLiteral(":"), new MessageValue(new("binding", "return-kind"))]
                : [new MessageValue(new("binding", "command-name"))]))
        };
        var content = CommandPresentation.Empty with { Personnel = new(roles.ToDictionary(r => r, r => new DecisionMessageDefinition(r,
            r.StartsWith("journey-arrived", StringComparison.Ordinal) ? new() { ["count"] = "count", ["return-kind"] = "return-kind" }
                : new() { ["command-name"] = "command-name" }, r == "journey-interrupted" ? "actor" : null))) };
        return (texts, content);
    }

    [Fact]
    public void ActualTargetReleasePublishesInterruptionBeforeReturnAndOnlyOnceAcrossPlayers()
    {
        var baseline = G01ScenarioTests.Fixture();
        var definition = baseline with { Officers = [.. baseline.Officers,
            new OfficerInitialState("captive", new OfficerState(7, 2, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Captive })],
            Information = baseline.Information with { Relations = [new(1, 2, 80)] } };
        var session = GameSession.Start(definition, 42);
        var id = Guid.NewGuid();
        Assert.True(session.Execute(new("player"), new RecruitOfficerCommand(id, 1, 0, 2, 7)).Submission.Accepted);
        var before = session.View;
        var released = session.Execute(new("player"), new ReleasePrisonerCommand(Guid.NewGuid(), 1, 1, 7));
        Assert.True(released.Submission.Accepted);
        var fact = Assert.Single(released.Events.OfType<PersonnelJourneyInterruptedEvent>());
        Assert.Equal((id, 2, 1), (fact.TaskId, fact.OfficerId, fact.DestinationId));
        var (texts, content) = Presentation(session);
        var output = ExecutionPublication.Read(before, session.View, session.Messages(texts, before.World), texts, content, released.Events);
        Assert.Equal(new[] { "journey-interrupted", "journey-withdrawal" }, output.Messages.Select(m => m.Message.Key));
        Assert.Equal(2, output.Messages[0].SpeakerId);
        Assert.Equal("登庸", Assert.Single(output.Records).Message.BbCode);
        var plan = BattlePresentationPlan.Create(before, session.View, released.Events, session.Messages(texts, before.World), texts, content);
        Assert.All(plan.Messages, m => Assert.Equal(BattleMessageChannel.Dialogue, m.Channel));
        Assert.Equal(output.Messages, plan.Messages.Select(m => m.Message));
        var remainder = ExecutionPublication.Read(before, session.View, session.Messages(texts, before.World), texts, content,
            released.Events, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(remainder.Messages); Assert.Empty(remainder.Records);
    }

    [Theory]
    [InlineData(false, true, "2:Ordered")]
    [InlineData(true, true, "2:Personnel")]
    [InlineData(true, false, null)]
    public void ArrivalGroupsMembersAndKeepsPostDateAdvanceMergesSilent(bool returning, bool notify, string? expected)
    {
        var session = GameSession.Start(G01ScenarioTests.Fixture(), 42);
        var (texts, content) = Presentation(session);
        var before = session.View; var clock = before.World.Clock;
        var occurrence = new PersonnelArrivalOccurrence(1, 1, 10, notify, 1);
        DomainEvent[] facts = returning
            ? [new PersonnelReturnedEvent(clock, 2, 1, occurrence), new PersonnelReturnedEvent(clock, 3, 1, occurrence)]
            : [new OfficerMovedEvent(clock, 2, 1, 1, occurrence), new OfficerMovedEvent(clock, 3, 1, 1, occurrence)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        if (expected is null) { Assert.Empty(output.Messages); Assert.Empty(output.Records); }
        else
        {
            Assert.Equal(expected, Assert.Single(output.Messages).Message.BbCode);
            Assert.Equal(expected, Assert.Single(output.Records).Message.BbCode);
        }
    }

    [Fact]
    public void InterruptedDiplomacyDoesNotAnnounceRefundBeforeArrival()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var draft = new MissionDraft(session, 1, "gift"); draft.SetActor(2); draft.SetTarget(2);
        Assert.True(draft.SetGift(new(GiftKind.Money, 1000, null)));
        var command = Assert.IsType<GiftCommand>(draft.CreateCommand());
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var before = session.View;
        var (texts, content) = Presentation(session);
        DomainEvent[] facts = [new TaskCancelledEvent(command.CommandId, before.World.Clock, "invalid-target-or-executor"),
            new PersonnelJourneyInterruptedEvent(before.World.Clock, command.CommandId, 1, 2, "diplomacy-gift", null, 1, 1)];
        var output = ExecutionPublication.Read(before, before, session.Messages(texts), texts, content, facts);
        Assert.Equal(2, output.Messages.Count);
        string record = Assert.Single(output.Records).Message.BbCode;
        Assert.Equal("赠与", record);
    }

    [Fact]
    public async Task AuthoredJourneyBindingsEvaluateAgainstTheOptionalRealPackage()
    {
        if (Environment.GetEnvironmentVariable("SAN9_PRESENTATION_CONTENT") is not { Length: > 0 } root) return;
        var package = await ClientContentFiles.LoadAsync(root);
        int force = package.Scenario.Forces.First(f => f.LeaderId is not null &&
            package.Scenario.Officers.Count(p => p.State.OwnerId == f.Id && p.State.IsServing) >= 4).Id;
        var session = GameSession.Start(package.Scenario with { PlayerForceId = force }, 42);
        var before = session.View; var clock = before.World.Clock;
        int ruler = before.World.Forces.Single(f => f.Id == force).LeaderId!.Value;
        var members = before.World.Officers.Where(p => p.OwnerId == force && p.IsServing && p.SettlementId is not null).Take(4).ToArray();
        int home = members[0].SettlementId!.Value;
        int? corps = before.World.Settlements.Single(s => s.Id == home).CorpsId;
        var context = session.Messages(package.Text);
        foreach (string kind in new[] { "personnel-move", "personnel-search", "personnel-recruit", "tactic-study",
            "strategy-alienate", "strategy-burn", "strategy-seize", "strategy-gossip", "diplomacy-gift",
            "diplomacy-attack-request", "diplomacy-prisoner-return", "diplomacy-surrender", "unit-strategy" })
        {
            DomainEvent[] facts = [new PersonnelJourneyInterruptedEvent(clock, Guid.NewGuid(), force, members[0].Id,
                kind, kind == "unit-strategy" ? UnitStrategyKind.Reinforce : null, home, corps)];
            var output = ExecutionPublication.Read(before, before, context, package.Text, package.Presentation, facts);
            Assert.Equal(2, output.Messages.Count); Assert.Single(output.Records);
        }
        foreach (bool returning in new[] { false, true })
            for (int count = 1; count <= members.Length; count++)
            {
                var occurrence = new PersonnelArrivalOccurrence(force, ruler, 10, true, corps);
                DomainEvent[] facts = members.Take(count).Select(p => returning
                    ? (DomainEvent)new PersonnelReturnedEvent(clock, p.Id, home, occurrence)
                    : new OfficerMovedEvent(clock, p.Id, home, home, occurrence)).ToArray();
                var output = ExecutionPublication.Read(before, before, context, package.Text, package.Presentation, facts);
                Assert.Single(output.Messages); Assert.Single(output.Records);
            }
        foreach (var resource in new[] { ResourceKind.Money, ResourceKind.Food })
        {
            DomainEvent[] facts = [new ReturnCargoDeliveredEvent(clock, Guid.NewGuid(), members[0].Id, force, home, corps, resource, 1000, 123)];
            var output = ExecutionPublication.Read(before, before, context, package.Text, package.Presentation, facts);
            Assert.Contains("123", Assert.Single(output.Messages).Message.BbCode);
            Assert.Contains("877", Assert.Single(output.Records).Message.BbCode);
            var plan = BattlePresentationPlan.Create(before, before, facts, context, package.Text, package.Presentation);
            Assert.Single(plan.Messages); Assert.Single(plan.Records);
            var remainder = ExecutionPublication.Read(before, before, context, package.Text, package.Presentation, facts,
                presentedFeedback: plan.PresentedFeedback);
            Assert.Empty(remainder.Messages); Assert.Empty(remainder.Records);
        }
    }
}
