using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class DiplomacyReceptionTests
{
    [Theory]
    [InlineData("gift", 0)]
    [InlineData("gift", 99)]
    [InlineData("surrender", 0)]
    [InlineData("surrender", 99)]
    public void ReceivingAdviserReadsTheArrivedRollWithoutAnsweringOrConsumingRandomness(string role, int roll)
    {
        var definition = MissionScenario.Create();
        definition = definition with
        {
            Forces = definition.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 2 } : f).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { Status = OfficerStatus.Strategist } }
                : p.State.Id == 1 ? p with { State = p.State with { AutomaticSurrenderAllowed = true, Ambition = 0 } } : p).ToArray(),
            Settlements = definition.Settlements.Select(s => s.Id == 2 ? s with { Soldiers = 100000 } : s).ToArray(),
            Military = definition.Military! with
            {
                RegionIds = Enumerable.Range(0, 90).Select(i => (int?)(i % 30 < 10 ? 1 : i % 30 < 20 ? 2 : 3)).ToImmutableArray(),
                TerritoryExits = Enumerable.Repeat(TerritoryExits.All, 90).ToImmutableArray()
            }
        };
        var session = GameSession.Start(definition, 42);
        Assert.Equal(AdviserOpinion.Unavailable, session.GetDiplomacyReceptionAdvice(Guid.NewGuid()).Opinion);
        session.EndControl(new("player"));
        GameCommand command = role == "gift" ? new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1, new(GiftKind.Money, 1000, null))
            : new DemandSurrenderCommand(Guid.NewGuid(), 2, 0, 22, 1);
        Assert.True(session.Execute(new("ai:2"), command).Submission.Accepted);
        var outbound = session.Capture();
        session = GameSession.Restore(definition, outbound with { World = outbound.World with
        {
            Simulation = outbound.World.Simulation with { Tasks = outbound.World.Simulation.Tasks.Select(t => t.Id == command.CommandId
                ? t with { Journey = t.Journey! with { SuccessRoll = roll } } : t).ToImmutableArray() }
        } });
        for (int i = 0; i < 4 && session.Snapshot.Simulation.PendingDailyTask is null; i++) session.AdvanceXun();
        Assert.NotNull(session.Snapshot.Simulation.PendingDailyTask);
        var before = session.Snapshot;
        var advice = session.GetDiplomacyReceptionAdvice(command.CommandId);
        Assert.Equal(2, advice.AdviserId);
        Assert.Equal(roll == 0 ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable, advice.Opinion);
        Assert.Equal(advice, session.GetDiplomacyReceptionAdvice(command.CommandId));
        StateAssert.Equal(before, session.Snapshot);

        string[] keys = ["prompt", "arrival", "greeting", "proposal", role + "-host-favorable", role + "-host-unfavorable"];
        var texts = MissionScenario.Texts(session.View) with { Messages = keys.ToDictionary(k => k,
            k => new MessageDefinition([new MessageLiteral(k)])) };
        var presentation = CommandPresentation.Empty with { Decisions = new(new()
        {
            [role] = new(DecisionWindowKind.Diplomacy, new("prompt", []),
                [new("arrival", []), new("greeting", [], "actor"), new("proposal", [], "actor")], [],
                new() { ["accept"] = "accept", ["decline"] = "decline" })
        }, []) { Shared = keys.Where(k => k.Contains("-host-", StringComparison.Ordinal)).ToDictionary(k => k,
            k => new DecisionMessageDefinition(k, [], "adviser")) } };
        var draft = DecisionDraft.Read(session, texts, presentation)!;
        Assert.Equal(2, draft.Introduction[^1].SpeakerId);
        Assert.Equal(role + "-host-" + (roll == 0 ? "favorable" : "unfavorable"), draft.Introduction[^1].Message.BbCode);
        Assert.Equal(draft.Meeting, draft.Introduction[^1].Diplomacy);
        StateAssert.Equal(before, session.Snapshot);
        var option = draft.Options.Single(o => o.Role == (roll == 0 ? "decline" : "accept"));
        var result = session.Execute(new(draft.ControllerId), option.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence, null, 0));
        Assert.True(result.Submission.Accepted);
        Assert.Equal(roll != 0, role == "gift" ? Assert.Single(result.Events.OfType<GiftResolvedEvent>()).Accepted
            : Assert.Single(result.Events.OfType<SurrenderResolvedEvent>()).Accepted);
    }
}
