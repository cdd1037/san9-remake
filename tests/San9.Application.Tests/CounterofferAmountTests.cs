using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CounterofferAmountTests
{
    [Theory]
    [InlineData("attack-request", true)]
    [InlineData("attack-request", false)]
    [InlineData("prisoner-return", true)]
    [InlineData("prisoner-return", false)]
    public void DefaultAsksForTheMostMoneyTheAdviserFavorsWithoutChangingState(string role, bool hasAdviser)
    {
        var definition = MissionScenario.Create();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f.Id == 1 ? f with { AdviserId = hasAdviser ? 2 : null } : f).ToArray(),
            Officers = [.. definition.Officers.Select(p => p.State.Id == 2 && hasAdviser
                ? p with { State = p.State with { Status = OfficerStatus.Strategist } } : p),
                new("requested-prisoner", new(13, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive })]
        };
        var session = GameSession.Start(definition, 42);
        session.EndControl(new("player"));
        GameCommand command = role == "attack-request" ? new RequestAttackCommand(Guid.NewGuid(), 2, 0, 22, 1, 3)
            : new ReturnPrisonerCommand(Guid.NewGuid(), 2, 0, 22, 1, 13);
        Assert.True(session.Execute(new("ai:2"), command).Submission.Accepted);
        for (int i = 0; i < 4 && session.Snapshot.Simulation.PendingDailyTask is null; i++) session.AdvanceXun();
        Assert.NotNull(session.Snapshot.Simulation.PendingDailyTask);
        var texts = MissionScenario.Texts(session.View);
        texts.Messages["prompt"] = new([new MessageLiteral("Choose")]);
        var presentation = CommandPresentation.Empty with
        {
            Decisions = new(new()
            {
                [role] = new(DecisionWindowKind.Diplomacy, new("prompt", []), [new("prompt", [])], [],
                    new() { ["free"] = "Free", ["money"] = "Money", ["prisoner"] = "Prisoner", ["treasure"] = "Treasure", ["decline"] = "Decline" })
            }, []) { Shared = new() { ["diplomacy-adviser"] = new("prompt", []) } }
        };
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        var before = session.Snapshot;
        var option = draft.Options.Single(o => o.Role == "money");
        uint amount = draft.AmountDefault(option);
        Assert.InRange(amount, option.Minimum, option.Maximum);
        if (hasAdviser)
        {
            Assert.Equal(AdviserOpinion.Favorable, session.GetCounterofferAdvice(draft.Diplomacy!.TaskId,
                new(CompensationKind.Money, amount, null, null)).Opinion);
            if (amount < option.Maximum)
                Assert.Equal(AdviserOpinion.Unfavorable, session.GetCounterofferAdvice(draft.Diplomacy!.TaskId,
                    new(CompensationKind.Money, amount + 1, null, null)).Opinion);
        }
        else Assert.Equal(option.Maximum, amount);
        Assert.Equal(amount, draft.AmountDefault(option));
        Assert.Same(before, session.Snapshot);
    }
}
