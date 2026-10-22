using San9.Tests;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class PrisonerReturnSessionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CaptorQuotePrecedesRequesterChoiceAndEnvoyAnswersBeforeResult(bool accept)
    {
        var session = GameSession.Start(Content(exchange: true), 42);
        Assert.True(session.Execute(new("player"), new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13)).Submission.Accepted);
        session.AdvanceXun();
        var (texts, presentation) = NegotiationText(session.View);
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.Equal(1, draft.ForceId);
        var quote = draft.Introduction[^1];
        Assert.Equal(21, quote.SpeakerId);
        Assert.Equal("Return exchange", quote.Message.BbCode);
        Assert.Equal(draft.Meeting, quote.Diplomacy);
        var option = draft.Options.Single(o => o.Role == (accept ? "accept" : "decline"));
        var command = Assert.IsType<RespondDailyTaskCommand>(option.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0));
        Assert.Equal(1, command.IssuerId);
        var result = session.Execute(new(draft.ControllerId), command);
        Assert.True(result.Submission.Accepted);
        var messages = draft.Results(option.Role, result.Events, null, 0);
        Assert.Equal(12, messages[0].SpeakerId);
        Assert.Equal(accept ? "Accept terms" : "Decline terms", messages[0].Message.BbCode);
        Assert.Equal(draft.Meeting, messages[0].Diplomacy);
        Assert.Equal(accept, Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>()).Accepted);
        if (!accept) Assert.Empty(result.Events.OfType<PrisonerRecoveredEvent>());
        Assert.Null(messages[1].SpeakerId);
    }

    [Fact]
    public void PlayerCaptorQuoteAndAiEnvoyAnswerRetainTheirSpeakersAndExchangeResult()
    {
        var session = GameSession.Start(Content(exchange: true), 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 23)).Submission.Accepted);
        session.AdvanceXun();
        var (texts, presentation) = NegotiationText(session.View);
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        var counteroffer = new DiplomacyCounterofferDraft(draft);
        Assert.Equal(CompensationKind.Free, counteroffer.Offer.Kind);
        var before = session.Snapshot;
        counteroffer.Select("prisoner", 13);
        Assert.Throws<ArgumentException>(() => counteroffer.Select("prisoner", 999));
        Assert.Equal(13, counteroffer.SelectedId);
        counteroffer.Select("money", amount: 500);
        Assert.Equal(500u, counteroffer.Offer.Money);
        counteroffer.Select("free");
        Assert.False(counteroffer.HasCondition);
        Assert.Null(counteroffer.SelectedId);
        counteroffer.Select("prisoner", 13);
        Assert.Same(before, session.Snapshot);
        var option = counteroffer.Option;
        var result = session.Execute(new(draft.ControllerId), option.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence,
            counteroffer.SelectedId, counteroffer.Amount));
        Assert.True(result.Submission.Accepted);
        var messages = draft.Results(option.Role, result.Events, 13, 0);
        Assert.Equal(11, messages[0].SpeakerId);
        Assert.Equal("Return prisoner", messages[0].Message.BbCode);
        Assert.Equal(22, messages[1].SpeakerId);
        Assert.Equal("Accept terms", messages[1].Message.BbCode);
        Assert.Equal("Exchange recovered", messages[^1].Message.BbCode);
    }

    internal static (TextCatalog Texts, CommandPresentation Presentation) NegotiationText(StrategySnapshot view, string role = "prisoner-return")
    {
        var texts = MissionScenario.Texts(view);
        texts.Messages["intro"] = new([new MessageLiteral("Arrival")]);
        texts.Messages["quote"] = new([new MessageLiteral("Return "),
            new MessageValue(new("property", "person.name", Arguments: [new("binding", "person.2")]))]);
        texts.Messages["accept"] = new([new MessageLiteral("Accept terms")]);
        texts.Messages["decline"] = new([new MessageLiteral("Decline terms")]);
        texts.Messages["money-quote"] = new([new MessageLiteral("Pay "), new MessageValue(new("binding", "number.0"))]);
        texts.Messages["other-quote"] = new([new MessageLiteral("Quoted terms")]);
        texts.Messages["result"] = new([new MessageLiteral("Result")]);
        texts.Messages["exchange-result"] = new([new MessageLiteral("Exchange recovered")]);
        var shared = DecisionPresentations.NegotiationMessageRoles(role).ToDictionary(key => key, key =>
            key.EndsWith("quote-prisoner", StringComparison.Ordinal)
                ? new DecisionMessageDefinition("quote", new() { ["person.2"] = "exchange-person" }, "host")
                : key.EndsWith("quote-money", StringComparison.Ordinal)
                    ? new DecisionMessageDefinition("money-quote", new() { ["number.0"] = "cost" }, "host")
                    : key.Contains("-quote-", StringComparison.Ordinal) ? new DecisionMessageDefinition("other-quote", [], "host")
                    : new DecisionMessageDefinition(key.EndsWith("decline", StringComparison.Ordinal) ? "decline" : "accept", [], "actor"));
        shared["diplomacy-foreign-arrival"] = new("intro", []);
        shared["diplomacy-exchanged-prisoner"] = new("exchange-result", []);
        return (texts, CommandPresentation.Empty with { Decisions = new(new()
        {
            [role] = new(DecisionWindowKind.Diplomacy, new("intro", []), [new("intro", [])],
                new() { ["success"] = [new("result", [])], ["failure"] = [new("result", [])] },
                new() { ["accept"] = "Accept", ["decline"] = "Decline", ["free"] = "Free", ["money"] = "Money",
                    ["prisoner"] = "Prisoner", ["treasure"] = "Treasure" })
        }, []) { Shared = shared } });
    }

    private static ScenarioInitialState Content(int relation = 0, bool exchange = false) => new(
        ScenarioInitialState.CurrentSchemaVersion, "prisoner-diplomacy", 190, 1, 1, 1,
        [new(1, "first", 1, 100) { LeaderId = 11, BaseTrust = 100 },
         new(2, "second", 2, 100) { LeaderId = 21, BaseTrust = 100 }],
        [new(1, "home", 1, 0, 0, 0, 100, 100, 0, []), new(2, "away", 2, 18, 0, 0, 100, 100, 0, [])], [], [])
    {
        Officers = [new("ruler", new(11, 1, 1, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler }),
            new("envoy", new(12, 1, 1, new(50, 50, 100, 50), false)),
            new("ruler2", new(21, 2, 2, new(50, 50, 100, 50), false) { Status = OfficerStatus.Ruler }),
            new("envoy2", new(22, 2, 2, new(50, 50, 100, 50), false)),
            new("prisoner", new(13, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive }),
            new("exchange", new(23, 2, exchange ? 1 : 2, new(50, 50, 50, 50), false)
                { Status = exchange ? OfficerStatus.Captive : OfficerStatus.Serving })],
        Information = new([], [], [new(9, "treasure", 0, 0, 1, null)], [new San9.Core.Domain.CorpsState(1, 1, 11, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(100000, 100000)) { IsRoot = true }], [new(9, 11, TreasureDiscovery.Discovered, null)])
            { Relations = [new(1, 2, relation)] }
    };

    [Theory]
    [InlineData(true, false)]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void AiQuotePlayerAcceptanceAndExchangeSurviveSaveAndReplay(bool accept, bool exchange)
    {
        var content = Content(exchange: exchange);
        var session = GameSession.Start(content, 42);
        var command = new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13);
        uint initialMoney = session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money;
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        Assert.Equal(initialMoney, session.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        Assert.Null(Assert.Single(session.Snapshot.Simulation.Tasks).Journey!.SuccessRoll);
        var outbound = SaveService.Decode(SaveService.Encode(session), content);
        var arrived = session.AdvanceXun();
        EventAssert.Equal(arrived.Events, outbound.AdvanceXun().Events);
        var task = Assert.Single(session.Snapshot.Simulation.Tasks, t => t.ForceId == 1);
        Assert.Equal(11, session.Snapshot.Clock.Day);
        Assert.Equal(1, task.ResolutionStep);
        Assert.Equal(exchange ? CompensationKind.Prisoner : CompensationKind.Money, task.PrisonerReturn!.Offer!.Kind);
        Assert.Single(arrived.Events.OfType<CompensationQuotedEvent>());
        Assert.DoesNotContain(arrived.Events, e => e is PrisonerReturnResolvedEvent);
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var restored = outbound;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, 1, accept);
        var result = session.Execute(new("player"), response);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), response).Events);
        StateAssert.Equal(session.Snapshot, restored.Snapshot);
        var resolved = Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>());
        Assert.Equal(accept, resolved.Accepted);
        Assert.Equal(accept && !exchange ? 500u : 0u, resolved.Received);
        Assert.Equal(accept ? exchange ? 2 : 1 : 0, result.Events.OfType<PrisonerRecoveredEvent>().Count());
        Assert.Equal(accept ? OfficerStatus.Serving : OfficerStatus.Captive, session.Snapshot.Officers.Single(p => p.Id == 13).Status);
        Assert.All(session.Snapshot.Simulation.Tasks, t => Assert.NotNull(t.Journey!.ReturnLeg));
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
    }

    [Fact]
    public void FreeReturnCompletesWithoutAcceptanceAndRewardsCaptorOnce()
    {
        var content = Content(relation: 80);
        var session = GameSession.Start(content, 42);
        Assert.True(session.Execute(new("player"), new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13)).Submission.Accepted);
        var result = session.AdvanceXun();
        Assert.True(Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>()).Accepted);
        Assert.Null(session.Snapshot.Simulation.PendingDailyTask);
        Assert.Equal(102, session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust);
        Assert.Equal(80, session.Snapshot.Relations.Single().Value);
        Assert.Single(result.Events.OfType<PrisonerRecoveredEvent>());
        Assert.Empty(session.AdvanceXun().Events.OfType<PrisonerReturnResolvedEvent>());
        Assert.Equal(102, session.Snapshot.Forces.Single(f => f.Id == 2).BaseTrust);
    }

    [Theory]
    [InlineData(CompensationKind.Free)]
    [InlineData(CompensationKind.Money)]
    [InlineData(CompensationKind.Treasure)]
    [InlineData(CompensationKind.Refuse)]
    public void PlayerCaptorQuotesAndAiRequesterResolvesWithoutSubmissionRandom(CompensationKind kind)
    {
        var content = Content(exchange: true);
        content = content with { Information = content.Information with
            { TreasureStates = [new(9, 21, TreasureDiscovery.Discovered, null)] } };
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 23)).Submission.Accepted);
        var outbound = SaveService.Decode(SaveService.Encode(session), content);
        outbound.AdvanceXun();
        session.AdvanceXun();
        var task = Assert.Single(session.Snapshot.Simulation.Tasks);
        Assert.Equal(0, task.ResolutionStep);
        var offer = new CompensationOffer(kind, kind == CompensationKind.Money ? 500u : 0u, null, kind == CompensationKind.Treasure ? 9 : null);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, 0, true) { Offer = offer };
        Assert.Equal(SaveFailure.NotStable, Assert.Throws<SaveException>(() => SaveService.Encode(session)).Failure);
        var restored = outbound;
        var result = session.Execute(new("player"), response);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), response).Events);
        Assert.Equal(kind != CompensationKind.Refuse, Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>()).Accepted);
        if (kind == CompensationKind.Treasure) Assert.Equal(11, session.Snapshot.Treasures.Single().HolderId);
    }

    [Fact]
    public void InvalidQuoteAndChangedPrisonerHaveNoPayment()
    {
        var content = Content(exchange: true);
        var session = GameSession.Start(content, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 23)).Submission.Accepted);
        session.AdvanceXun();
        var task = Assert.Single(session.Snapshot.Simulation.Tasks);
        var before = session.Snapshot;
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task.Id, 0, true)
            { Offer = new(CompensationKind.Money, 1000000, null, null) };
        Assert.Equal(CommandRejection.InvalidDecision, session.Execute(new("player"), response).Submission.Rejection);
        StateAssert.Equal(before, session.Snapshot);
        var captured = session.Snapshot;
        var changed = captured with { Officers = captured.Officers.Select(p =>
            p.Id == 23 ? p with { Status = OfficerStatus.Serving, SettlementId = 2 } : p).ToArray() };
        var restored = GameSession.EngineFactory(content)(WorldState.Restore(changed));
        var result = restored.Execute(new("player"), response with { Offer = new(CompensationKind.Free, 0, null, null) });
        Assert.True(result.Submission.Accepted);
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == task.Id);
        Assert.DoesNotContain(result.Events, e => e is PrisonerReturnResolvedEvent);
    }
}
