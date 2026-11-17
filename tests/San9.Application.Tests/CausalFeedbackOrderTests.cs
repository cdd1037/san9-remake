using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Content.Scenarios;
using San9.Content.Text;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class CausalFeedbackOrderTests
{
    [Fact]
    public void RecruitmentFinishesBeforeOtherEnvoysWithdrawAndBeforeGrowthIsReported()
    {
        var definition = Quiet(G01ScenarioTests.Fixture());
        definition = definition with { Officers = [.. definition.Officers.Select(p => p.State.Id == 2
            ? p with { State = p.State with { BaseAbilities = new(50, 50, 80, 50) } } : p),
            new("free", new OfficerState(7, null, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free }.WithLoyalty(1, 255))] };
        var session = GameSession.Start(definition, 14);
        Submit(session, new RecruitOfficersCommand(Guid.NewGuid(), 1, 0, [2, 3], 7));
        var before = session.View;
        var events = session.AdvanceXun().Events;
        var (texts, content) = Presentation(before);
        var context = session.Messages(texts, before.World);
        var output = ExecutionPublication.Read(before, session.View, context, texts, content, events);
        Assert.Equal(["employ-meeting", "employ-success", "employ-result-success",
            "journey-interrupted", "journey-withdrawal"], output.Messages.Select(m => m.Message.Key));
        var plan = BattlePresentationPlan.Create(before, session.View, events, context, texts, content);
        Assert.Equal(output.Messages, plan.Messages.Select(c => c.Message));
        var withdrawal = plan.Messages[^1];
        Assert.Equal(plan.Messages[2].Id, plan.Messages[3].AfterDialogueId);
        Assert.Equal(plan.Messages[3].Id, withdrawal.AfterDialogueId);
        var growth = Assert.Single(plan.Records, c => c.Record.Message.Key == "ability-grown");
        Assert.True(growth.FactIndex > plan.Messages[2].FactIndex);
        Assert.Equal(withdrawal.Id, growth.AfterDialogueId);
        Assert.Empty(ExecutionPublication.Read(before, session.View, context, texts, content, events,
            presentedFeedback: plan.PresentedFeedback).Messages);
        var steps = output.Steps.ToList();
        Assert.True(steps.FindIndex(s => s.Records.Any(r => r.Message.Key == "ability-grown")) >
            steps.FindIndex(s => s.Messages.Any(m => m.Message.Key == "employ-result-success")));
    }

    [Fact]
    public void NaturalDeathIsAnnouncedBeforeRecruitmentWithdrawalInBothPublicationPaths()
    {
        var definition = Distant();
        definition = definition with { NaturalDeathsEnabled = true, Officers = [.. definition.Officers,
            new("elder", new OfficerState(7, null, 2, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free, BirthYear = 90 })] };
        var session = GameSession.Start(definition, 42);
        for (int xun = 0; xun < 35; xun++) session.AdvanceXun();
        Submit(session, new RecruitOfficersCommand(Guid.NewGuid(), 1, 0, [2], 7));
        var before = session.View;
        var events = session.AdvanceXun().Events;
        Assert.Single(events.OfType<OfficerDiedEvent>());
        AssertNarration(session, before, events, ["free-officer-death", "journey-interrupted", "journey-withdrawal"]);
    }

    [Fact]
    public void PrisonerEscapeIsAnnouncedBeforeTheRecruitingEnvoyWithdraws()
    {
        var basis = Distant();
        var definition = basis with {
            Settlements = [.. basis.Settlements, basis.Settlements[0] with { Id = 3, NameKey = "distant-home", X = 300 }],
            Officers = [.. basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { SettlementId = 3 } } : p),
                new("captive", new OfficerState(7, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive })]
        };
        var session = GameSession.Start(definition, 1);
        Assert.Equal(OfficerStatus.Captive, session.Snapshot.Officers.Single(p => p.Id == 7).Status);
        var state = session.Capture();
        session = GameSession.Restore(definition, state with { World = state.World with {
            Officers = state.World.Officers.Select(p => p.Id == 7 ? p with { BaseAbilities = p.BaseAbilities with { War = 5000 } } : p).ToImmutableArray() } });
        Submit(session, new RecruitOfficersCommand(Guid.NewGuid(), 1, 0, [2], 7));
        var before = session.View;
        var events = session.AdvanceXun().Events;
        Assert.Single(events.OfType<PrisonerEscapedEvent>());
        AssertNarration(session, before, events,
            ["prisoner-escape-speech", "prisoner-escape-result", "journey-interrupted", "journey-withdrawal"]);
    }

    [Fact]
    public void AutomaticGiftMeetingPrecedesItsPeaceConsequencesInClosingFeedback()
    {
        var definition = Military();
        definition = definition with { Information = definition.Information with { Corps = [.. definition.Information.Corps,
            new(4, 1, 2, new(0, 0)) { Delegation = new DefeatForceDelegation(2) }] } };
        var session = GameSession.Start(definition, 4);
        Submit(session, new GiftCommand(Guid.NewGuid(), 1, 0, 2, 2, new(GiftKind.Money, 1000, null)));
        var before = session.View;
        var events = session.AdvanceXun().Events;
        Assert.True(Assert.Single(events.OfType<GiftResolvedEvent>()).Accepted);
        var (texts, content) = Presentation(before);
        var output = ExecutionPublication.Read(before, session.View, session.Messages(texts, before.World), texts, content, events);
        var steps = output.Steps.ToList();
        int meeting = steps.FindIndex(s => s.Messages.Any(m => m.Message.Key == "gift-accepted"));
        int peace = steps.FindIndex(s => s.Records.Any(r => r.Message.Key == "diplomacy-corps-cleared"));
        Assert.True(meeting >= 0 && peace > meeting);
        var dialogue = ExecutionPublication.Read(before, session.View, session.Messages(texts, before.World), texts, content, events, executionMap: false);
        var keys = dialogue.Messages.Select(m => m.Message.Key).ToList();
        Assert.True(keys.IndexOf("diplomacy-corps-cleared") > keys.IndexOf("gift-accepted"));
    }

    [Theory]
    [InlineData(UnitStrategyKind.Inspire, UnitCondition.HighSpirits, "inspire", 0)]
    [InlineData(UnitStrategyKind.Disorder, UnitCondition.NoFormation, "disorder", 0)]
    [InlineData(UnitStrategyKind.FalseReport, UnitCondition.Disguised, "false-report", 1)]
    public void StrategyActionPrecedesItsStateAndEffect(UnitStrategyKind strategy, UnitCondition condition, string role, int seed)
    {
        var definition = Military();
        var session = GameSession.Start(definition, (ulong)seed);
        Submit(session, new UnitStrategyCommand(Guid.NewGuid(), 1, 0, 2, strategy,
            strategy == UnitStrategyKind.Inspire ? 10 : 20));
        var before = session.View;
        var events = session.AdvanceXun().Events;
        Assert.True(Assert.Single(events.OfType<UnitStrategyResolvedEvent>()).Success);
        var (texts, content) = Presentation(before);
        content = content with { Battle = new([], [], "hit", "block") {
            ConditionEffects = new() { [condition] = new("status", 8, 100) } } };
        content.Missions[role + "-action"] = content.Missions[role + "-action"] with { Speaker = "actor" };
        content.Missions[role + "-reaction-success"] = content.Missions[role + "-reaction-success"] with { Speaker = "target-person" };
        var plan = BattlePresentationPlan.Create(before, session.View, events, session.Messages(texts, before.World), texts, content);
        var action = Assert.Single(plan.Messages, c => c.Message.Message.Key == role + "-action");
        var reaction = Assert.Single(plan.Messages, c => c.Message.Message.Key == role + "-reaction-success");
        var effect = Assert.Single(plan.MapEffects);
        Assert.True(effect.FactIndex > action.FactIndex);
        Assert.True(effect.Start > action.Time);
        Assert.Equal(reaction.Time + reaction.Message.MapSpeech!.DelaySeconds, effect.Start);
        var state = Assert.Single(plan.Values, c => c.Condition == condition);
        Assert.Equal(effect.Start, state.Time);
    }

    private static void AssertNarration(GameSession session, StrategySnapshot before, IReadOnlyList<DomainEvent> events, string[] expected)
    {
        var (texts, content) = Presentation(before);
        var context = session.Messages(texts, before.World);
        var output = ExecutionPublication.Read(before, session.View, context, texts, content, events);
        Assert.Equal(expected, output.Messages.Select(m => m.Message.Key));
        var plan = BattlePresentationPlan.Create(before, session.View, events, context, texts, content);
        Assert.Equal(output.Messages, plan.Messages.Select(c => c.Message));
        for (int index = 1; index < plan.Messages.Count; index++)
            Assert.Equal(plan.Messages[index - 1].Id, plan.Messages[index].AfterDialogueId);
        var remainder = ExecutionPublication.Read(before, session.View, context, texts, content, events, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(remainder.Messages);
        Assert.Empty(remainder.Records);
    }

    private static void Submit(GameSession session, GameCommand command) => Assert.True(session.Execute(new("player"),
        command with { Sequence = session.Snapshot.Simulation.NextSequence }).Submission.Accepted);

    private static ScenarioInitialState Quiet(ScenarioInitialState definition) => definition with {
        OfficeRequestsEnabled = false, FreeOfficerMigrationEnabled = false,
        OriginalAi = definition.OriginalAi with { WorkRules = definition.OriginalAi.WorkRules.ToImmutableDictionary(
            pair => pair.Key, pair => pair.Value with { OpeningWaitDays = 10000 }) },
        Information = definition.Information with { Relations = [new(1, 2, 0)] }
    };

    private static ScenarioInitialState Distant()
    {
        var definition = Quiet(G01ScenarioTests.Fixture());
        return definition with { Settlements = definition.Settlements.Select(s => s with { X = s.Id == 2 ? 300 : 0, Y = 0 }).ToArray() };
    }

    private static ScenarioInitialState Military()
    {
        var definition = Quiet(MissionScenario.Create());
        return definition with {
            Units = definition.Units.Select(u => u with { ServicePlaceId = u.OwnerId, Order = null }).ToArray(),
            Officers = definition.Officers.Select(p => p.State.Id == 2
                ? p with { State = p.State with { BaseAbilities = p.State.BaseAbilities with { Intelligence = 500 } } } : p).ToArray()
        };
    }

    private static (TextCatalog Texts, CommandPresentation Content) Presentation(StrategySnapshot before)
    {
        string[] personnel = ["employ-meeting", "employ-success", "employ-result-success", "journey-interrupted",
            "journey-withdrawal", "journey-interrupted-record", "journey-arrived", "journey-arrived-record"];
        string[] missions = ["diplomacy-arrival", "diplomacy-greeting", "diplomacy-welcome", "gift-proposal", "gift-accepted",
            "gift-refused", "gift-actor-accepted", "gift-actor-refused", "gift-success", "gift-failure",
            "inspire-action", "inspire-reaction-success", "inspire-success", "disorder-action", "disorder-reaction-success",
            "disorder-success", "false-report-action", "false-report-reaction-success", "false-report-success"];
        var roles = EventPresentationDefinition.RequiredRoles.Concat(ProgressPresentationDefinition.RecordRoles)
            .Concat(ProgressPresentationDefinition.FeedbackRoles).Concat(ProgressPresentationDefinition.MapSpeechRoles)
            .Concat(personnel).Concat(missions).Distinct();
        var texts = MissionScenario.Texts(before) with {
            Messages = roles.ToDictionary(r => r, r => new MessageDefinition([new MessageLiteral(r)]))
        };
        Dictionary<string, DecisionMessageDefinition> Define(IEnumerable<string> names) =>
            names.ToDictionary(r => r, r => new DecisionMessageDefinition(r, []));
        return (texts, CommandPresentation.Empty with {
            Personnel = new(Define(personnel)), Missions = Define(missions), Events = new(Define(EventPresentationDefinition.RequiredRoles)),
            Progress = new(Define(ProgressPresentationDefinition.RecordRoles), []) {
                Feedback = Define(ProgressPresentationDefinition.FeedbackRoles), MapSpeeches = Define(ProgressPresentationDefinition.MapSpeechRoles) }
        });
    }
}
