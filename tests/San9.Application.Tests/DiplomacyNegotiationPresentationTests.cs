using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class DiplomacyNegotiationPresentationTests
{
    [Theory]
    [InlineData("attack-request", true)]
    [InlineData("attack-request", false)]
    [InlineData("prisoner-return", true)]
    [InlineData("prisoner-return", false)]
    public void EditingTermsUsesOrdinaryAdviceAndKeepsFormalSpeechesInTheMeeting(string role, bool hasAdviser)
    {
        var definition = MissionScenario.Create();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f.Id == 1 ? f with { AdviserId = hasAdviser ? 2 : null } : f).ToArray(),
            Officers = [.. definition.Officers.Select(p => p.State.Id == 2 && hasAdviser
                    ? p with { State = p.State with { Status = OfficerStatus.Strategist } } : p),
                new("requested-prisoner", new(13, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive }),
                new("exchange-prisoner", new(14, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive })],
            Information = definition.Information with
            {
                Treasures = [new(9, "treasure", 0, 0, 1, null)],
                TreasureStates = [new(9, 21, TreasureDiscovery.Discovered, null)]
            }
        };
        var session = GameSession.Start(definition, 42);
        session.EndControl(new("player"));
        GameCommand command = role == "attack-request" ? new RequestAttackCommand(Guid.NewGuid(), 2, 0, 22, 1, 3)
            : new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 13);
        Assert.True(session.Execute(new("ai:2"), command).Submission.Accepted);
        for (int i = 0; i < 4 && session.Snapshot.Simulation.PendingDailyTask is null; i++) session.AdvanceXun();
        Assert.NotNull(session.Snapshot.Simulation.PendingDailyTask);
        var (texts, presentation) = PrisonerReturnSessionTests.NegotiationText(session.View, role);
        texts.Messages["adviser"] = new([new MessageLiteral("Advice")]);
        foreach (string key in new[] { "diplomacy-adviser", "diplomacy-counteroffer-unpredictable",
            "diplomacy-counteroffer-favorable", "diplomacy-counteroffer-unfavorable" })
            presentation.Decisions.Shared[key] = new("adviser", [], "adviser", "advice");
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.All(draft.Introduction, message => Assert.True(message.DiplomacyDialogue));
        var before = session.Snapshot;
        var terms = new DiplomacyCounterofferDraft(draft);
        var money = draft.Options.Single(o => o.Role == "money");
        uint recommended = draft.AmountDefault(money);
        Assert.InRange(recommended,money.Minimum,money.Maximum);
        if (hasAdviser)
        {
            Assert.Equal(AdviserOpinion.Favorable,session.GetCounterofferAdvice(terms.TaskId,new(CompensationKind.Money,recommended,null,null)).Opinion);
            if (recommended < money.Maximum)
                Assert.Equal(AdviserOpinion.Unfavorable,session.GetCounterofferAdvice(terms.TaskId,new(CompensationKind.Money,recommended+1,null,null)).Opinion);
        }
        else Assert.Equal(money.Maximum,recommended);
        Assert.Equal(recommended,draft.AmountDefault(money));
        Assert.False(terms.HasCondition);
        if (hasAdviser)
        {
            Assert.Equal(2, draft.Introduction[^1].SpeakerId);
            Assert.Equal(draft.Meeting, draft.Introduction[^1].Diplomacy);
            Assert.True(draft.Introduction[^1].DiplomacyDialogue);
        }
        foreach (string kind in new[] { "prisoner", "money", "treasure" })
        {
            uint manual = recommended == money.Maximum ? money.Minimum : money.Maximum;
            Assert.NotEqual(recommended,manual);
            terms.Select(kind, kind == "prisoner" ? 14 : kind == "treasure" ? 9 : null, kind == "money" ? manual : 0u);
            var advice = draft.CounterofferAdvice(terms.Option, terms.SelectedId, terms.Amount);
            if (kind == "money")
            {
                Assert.Equal(recommended,draft.AmountDefault(money));
                Assert.Equal(manual,terms.Amount);
            }
            if (hasAdviser)
            {
                var message = Assert.Single(advice);
                Assert.Equal(2, message.SpeakerId);
                Assert.Equal("advice", message.SoundRole);
                Assert.Null(message.Diplomacy);
                Assert.False(message.DiplomacyFarewell);
                Assert.False(message.DiplomacyDialogue);
            }
            else Assert.Empty(advice);
        }
        terms.Select("free");
        Assert.Empty(draft.CounterofferAdvice(terms.Option, terms.SelectedId, terms.Amount));
        Assert.Same(before, session.Snapshot);
        var refusal = draft.Options.Single(o => o.Role == "decline");
        var result = session.Execute(new(draft.ControllerId), refusal.CreateCommand(Guid.NewGuid(), before.Simulation.NextSequence, null, 0));
        Assert.True(result.Submission.Accepted);
        var speeches = draft.Results("decline", result.Events, null, 0).Where(m => m.SpeakerId is not null).ToArray();
        Assert.Equal(new int?[] { 1, 22 }, speeches.Select(m => m.SpeakerId));
        Assert.All(speeches, m => Assert.Equal(draft.Meeting, m.Diplomacy));
        Assert.All(speeches, m => Assert.True(m.DiplomacyDialogue));
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AttackRequestShowsTheActualQuoteAndRequestersEnvoyAnswer(bool accept)
    {
        var definition = MissionScenario.Create();
        definition = definition with { Settlements = definition.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 20000 } : s).ToArray(),
            Forces = definition.Forces.Select(f => f.Id == 2 ? f with { AdviserId = 22 } : f).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 22 ? p with { State = p.State with { Status = San9.Core.Domain.OfficerStatus.Strategist } } : p).ToArray() };
        var session = GameSession.Start(definition, 42);
        Assert.True(session.Execute(new("player"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 2, 2, 3)).Submission.Accepted);
        for (int i = 0; i < 4 && session.GetAttackRequestPrompt() is null; i++) session.AdvanceXun();
        var pending = session.GetAttackRequestPrompt()!;
        Assert.NotNull(pending);
        Assert.Equal(CompensationKind.Money, pending.Offer!.Kind);
        var (texts, presentation) = PrisonerReturnSessionTests.NegotiationText(session.View, "attack-request");
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.Equal(21, draft.Introduction[^1].SpeakerId);
        Assert.Equal("Pay " + pending.Offer.Money, draft.Introduction[^1].Message.BbCode);
        Assert.DoesNotContain(draft.Introduction, message => message.SpeakerId == 22);
        var option = draft.Options.Single(o => o.Role == (accept ? "accept" : "decline"));
        var result = session.Execute(new(draft.ControllerId), option.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0));
        Assert.True(result.Submission.Accepted);
        var messages = draft.Results(option.Role, result.Events, null, 0);
        Assert.Equal(2, messages[0].SpeakerId);
        Assert.Equal(accept ? "Accept terms" : "Decline terms", messages[0].Message.BbCode);
        Assert.Equal(accept ? AttackRequestOutcome.Accepted : AttackRequestOutcome.Refused,
            Assert.Single(result.Events.OfType<AttackRequestResolvedEvent>()).Outcome);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void AutomaticOutcomeIncludesHostQuoteAndEnvoyReplyBeforeMapResult(bool accepted)
    {
        var definition = MissionScenario.Create();
        definition = definition with { Forces = definition.Forces.Select(f => f.Id == 2 ? f with { AdviserId = 22 } : f).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 22 ? p with { State = p.State with { Status = San9.Core.Domain.OfficerStatus.Strategist } } : p).ToArray() };
        var session = GameSession.Start(definition, 42);
        var command = new RequestAttackCommand(Guid.NewGuid(), 1, 0, 2, 2, 3);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var before = session.View;
        string[] roles = ["diplomacy-arrival", "diplomacy-greeting", "diplomacy-welcome", "request-proposal",
            "attack-request-quote-free", "attack-request-quote-refuse", "attack-request-thanks", "attack-request-refused", "request-success", "request-failure",
            "diplomacy-host-adviser"];
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(role => role,
            role => new MessageDefinition([new MessageLiteral(role)])) };
        var messages = roles.ToDictionary(role => role, role => new DecisionMessageDefinition(role, [],
            Speaker: role.Contains("-quote-", StringComparison.Ordinal) ? "host" :
                role is "attack-request-thanks" or "attack-request-refused" ? "actor" : null,
            Sound: role is "request-success" or "request-failure" ? "result" : null));
        var fact = new AttackRequestResolvedEvent(before.World.Clock.NextDay(), command.CommandId, 2, 3,
            new(accepted ? CompensationKind.Free : CompensationKind.Refuse, 0, null, null),
            accepted ? AttackRequestOutcome.Accepted : AttackRequestOutcome.Refused, 0, 0);
        var output = MissionPresentation.Results(before, texts, [fact], content: messages, context: session.Messages(texts, before.World));
        var quote = Assert.Single(output.Messages, m => m.Message.BbCode == "attack-request-quote-" + (accepted ? "free" : "refuse"));
        var reply = Assert.Single(output.Messages, m => m.Message.BbCode == "attack-request-" + (accepted ? "thanks" : "refused"));
        Assert.Equal(21, quote.SpeakerId);
        Assert.Equal(2, reply.SpeakerId);
        Assert.Equal(quote.Diplomacy, reply.Diplomacy);
        Assert.Null(output.Messages[^1].Diplomacy);
        Assert.DoesNotContain(output.Messages, message => message.Message.BbCode == "diplomacy-host-adviser");
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GiftIncludesEnvoyAnswerWithoutExposingTheComputerHostsPrivateAdvice(bool accepted)
    {
        var definition = MissionScenario.Create();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f.Id == 2 ? f with { AdviserId = 22 } : f).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 22 ? p with
                { State = p.State with { Status = San9.Core.Domain.OfficerStatus.Strategist } } : p).ToArray()
        };
        var session = GameSession.Start(definition, 42);
        var command = new GiftCommand(Guid.NewGuid(), 1, 0, 2, 2, new(GiftKind.Money, 1000, null));
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var before = session.View;
        string[] roles = ["diplomacy-arrival", "diplomacy-greeting", "diplomacy-welcome", "gift-proposal",
            "gift-host-favorable", "gift-host-unfavorable", "gift-accepted", "gift-refused", "gift-actor-accepted", "gift-actor-refused", "gift-success", "gift-failure"];
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(role => role,
            role => new MessageDefinition([new MessageLiteral(role)])) };
        var messages = roles.ToDictionary(role => role, role => new DecisionMessageDefinition(role, [],
            Speaker: role.StartsWith("gift-actor-", StringComparison.Ordinal) ? "actor" : role is "gift-accepted" or "gift-refused" ? "target-ruler" : null,
            Sound: role is "gift-success" or "gift-failure" ? "result" : null));
        var fact = new GiftResolvedEvent(before.World.Clock.NextDay(), command.CommandId, 2, 2, command.Offer, accepted, 0, 0, 0);
        var output = MissionPresentation.Results(before, texts, [fact], content: messages, context: session.Messages(texts, before.World));
        Assert.DoesNotContain(output.Messages, m => m.Message.BbCode.StartsWith("gift-host-", StringComparison.Ordinal));
        var answer = output.Messages[^2];
        Assert.Equal(2, answer.SpeakerId);
        Assert.Equal("gift-actor-" + (accepted ? "accepted" : "refused"), answer.Message.BbCode);
        Assert.NotNull(answer.Diplomacy);
        Assert.Null(output.Messages[^1].Diplomacy);
    }

    [Fact]
    public void AlreadyAgreedAttackRequestReportsTheExistingAgreementWithoutEnteringAMeeting()
    {
        var session = GameSession.Start(MissionScenario.Create(), 42);
        var command = new RequestAttackCommand(Guid.NewGuid(), 1, 0, 2, 2, 3);
        Assert.True(session.Execute(new("player"), command).Submission.Accepted);
        var before = session.View;
        string[] roles = ["request-already-agreed-actor", "request-already-agreed"];
        var texts = MissionScenario.Texts(before) with { Messages = roles.ToDictionary(role => role,
            role => new MessageDefinition([new MessageLiteral(role)])) };
        var messages = roles.ToDictionary(role => role, role => new DecisionMessageDefinition(role, [],
            Speaker: role.EndsWith("-actor", StringComparison.Ordinal) ? "actor" : null));
        var fact = new AttackRequestResolvedEvent(before.World.Clock.NextDay(), command.CommandId, 2, 3, null,
            AttackRequestOutcome.AlreadyAgreed, 0, 0);
        var output = MissionPresentation.Results(before, texts, [fact], content: messages, context: session.Messages(texts, before.World));
        Assert.Equal(roles, output.Messages.Select(m => m.Message.BbCode));
        Assert.Equal(2, output.Messages[0].SpeakerId);
        Assert.All(output.Messages, m =>
        {
            Assert.Null(m.Diplomacy);
            Assert.Null(m.BackgroundMusicRole);
            Assert.False(m.DiplomacyDialogue);
        });
    }
}
