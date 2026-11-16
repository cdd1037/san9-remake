using System.Collections.Immutable;
using San9.Application.Persistence;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class GiftPeaceSessionTests
{
    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void GiftReplyPreviewsOwnRetreatAndPublishesActualBilateralConsequencesOnce(bool accept)
    {
        var basis = MissionScenario.Create();
        var formation = basis.Military!.Formations[0];
        var scenario = basis with
        {
            Information = basis.Information with { Corps = [.. basis.Information.Corps,
                new(4, 1, 2, new(0, 0)) { Delegation = new DefeatForceDelegation(2) }] },
            Military = basis.Military with { Formations =
                [formation with { Combat = new(10, 10, 0, 10, 50), Attack = new(1, false),
                    Positions = San9.Tests.FormationTestData.Layout([.. Enumerable.Repeat(BattleRank.Middle, formation.OfficerCapacity)]) }] },
            Units = basis.Units.Select(u => u with
            {
                ServicePlaceId = u.OwnerId, Condition = UnitCondition.Confused, ConditionDaysRemaining = 30,
                MemberPositions = San9.Tests.FormationTestData.Members(ImmutableDictionary<int, BattleRank>.Empty.Add(u.CommanderId!.Value, BattleRank.Middle)),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = u.Id == 10 ? 20 : 10 }
            }).ToArray()
        };
        var session = GameSession.Start(scenario, 42);
        session.EndControl(new("player"));
        Assert.True(session.Execute(new("ai:2"), new GiftCommand(Guid.NewGuid(), 2, 0, 22, 1,
            new(GiftKind.Money, 1000, null))).Submission.Accepted);
        var restored = SaveService.Decode(SaveService.Encode(session), scenario);
        session.AdvanceXun(); restored.AdvanceXun();
        var pending = session.Snapshot.Simulation.PendingDailyTask!;
        Guid task = pending.RemainingTaskIds[0];
        var before = session.Snapshot;
        var preview = session.GetGiftReceiptPreview(task)!;
        Assert.Equal(new[] { 10 }, preview.ReturningUnitIds);
        Assert.Equal(new[] { 4 }, preview.ClearedCorpsIds);
        StateAssert.Equal(before, session.Snapshot);
        var (texts, content) = Presentation(session.View);
        var draft = DecisionDraft.Read(session, texts, content)!;
        Assert.Equal(new[] { "return-confirmation", "corps-confirmation" }, draft.AcceptanceConfirmations.Select(m => m.Key));
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, session.Snapshot.Simulation.NextSequence, task, 0, accept);
        var result = session.Execute(new("player"), answer);
        Assert.True(result.Submission.Accepted);
        EventAssert.Equal(result.Events, restored.Execute(new("player"), answer).Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        Assert.Equal(accept, result.Events.OfType<DiplomaticConflictsEndedEvent>().Any());
        if (accept)
        {
            Assert.All(session.Snapshot.Units.Where(u => !u.IsPersonnel), u =>
            {
                Assert.Equal(MilitaryOrderKind.Return, u.Order!.Kind); Assert.True(u.AutonomousReturn);
                Assert.Null(u.EngagementTargetId); Assert.Null(u.ContactTarget);
            });
            var fact = Assert.Single(result.Events.OfType<DiplomaticConflictsEndedEvent>());
            Assert.Equal(new[] { 10, 20 }, fact.ReturningUnitIds);
            Assert.Equal(new[] { 4 }, fact.ClearedCorpsIds);
        }
        var output = ExecutionPublication.Read(draft.Before, session.View, session.Messages(texts), texts, content, result.Events);
        Assert.Equal(accept ? 1 : 0, output.Records.Count(r => r.Message.Key == "diplomacy-units-returned"));
        Assert.Equal(accept ? 1 : 0, output.Records.Count(r => r.Message.Key == "diplomacy-corps-cleared"));
        var plan = BattlePresentationPlan.Create(draft.Before, session.View, result.Events, session.Messages(texts), texts, content);
        var remaining = ExecutionPublication.Read(draft.Before, session.View, session.Messages(texts), texts, content,
            result.Events, presentedFeedback: plan.PresentedFeedback);
        Assert.DoesNotContain(remaining.Records, r => r.Message.Key == "diplomacy-units-returned");
        Assert.Equal(accept ? 1 : 0, plan.Records.Count(r => r.Record.Message.Key == "diplomacy-units-returned"));
        var dialogue = ExecutionPublication.Read(draft.Before, session.View, session.Messages(texts), texts, content, result.Events, executionMap: false);
        Assert.Equal(accept ? 1 : 0, dialogue.Messages.Count(m => m.Message.Key == "diplomacy-units-returned"));
        Assert.DoesNotContain(dialogue.Records, r => r.Message.Key == "diplomacy-units-returned");
        var foreign = new DiplomaticConflictsEndedEvent(session.Snapshot.Clock, 2, 3, [20], [4]);
        var foreignOutput = ExecutionPublication.Read(session.View, session.View, session.Messages(texts), texts, content, [foreign]);
        Assert.Empty(foreignOutput.Messages); Assert.Empty(foreignOutput.Records);
    }

    private static (TextCatalog, CommandPresentation) Presentation(StrategySnapshot view)
    {
        string[] roles = ["prompt", "return-confirmation", "corps-confirmation", "diplomacy-units-returned", "diplomacy-corps-cleared", "growth"];
        var texts = MissionScenario.Texts(view) with
        { Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)])) };
        foreach (string role in new[] { "diplomacy-units-returned", "diplomacy-corps-cleared" })
            texts.Messages[role] = new([new MessageLiteral(role),
                new MessageValue(new("property", "force.country-name", Arguments: [new("binding", "force.0")])),
                new MessageValue(new("property", "force.country-name", Arguments: [new("binding", "force.1")]))]);
        Dictionary<string, string> forces = new() { ["force.0"] = "force", ["force.1"] = "second-force" };
        var progress = new ProgressPresentationDefinition(new()
        {
            ["diplomacy-units-returned"] = new("diplomacy-units-returned", forces),
            ["diplomacy-corps-cleared"] = new("diplomacy-corps-cleared", forces), ["ability-grown"] = new("growth", [])
        }, []) { Feedback = new()
        {
            ["diplomacy-units-returned"] = new("diplomacy-units-returned", forces),
            ["diplomacy-corps-cleared"] = new("diplomacy-corps-cleared", forces), ["ability-grown"] = new("growth", [], "actor")
        } };
        var relationPresentation = DiplomacyFeedbackTests.Presentation(view);
        foreach (var pair in relationPresentation.Texts.Messages) texts.Messages[pair.Key] = pair.Value;
        foreach (string role in ProgressPresentationDefinition.RelationRoles)
        {
            progress.Records[role] = relationPresentation.Content.Progress!.Records[role];
            progress.Feedback[role] = relationPresentation.Content.Progress.Feedback[role];
        }
        return (texts, CommandPresentation.Empty with { Progress = progress, Decisions = new(new()
        {
            ["gift"] = new(DecisionWindowKind.Diplomacy, new("prompt", []), [], [], new() { ["accept"] = "accept", ["decline"] = "decline" })
        }, []) { Shared = new()
        {
            ["gift-return-confirmation"] = new("return-confirmation", []), ["gift-corps-confirmation"] = new("corps-confirmation", [])
        } } });
    }
}
