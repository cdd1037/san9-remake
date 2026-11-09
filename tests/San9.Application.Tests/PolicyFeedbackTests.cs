using System.Collections.Immutable;
using San9.Application.Presentation;
using San9.Content.Interface;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed partial class ProgressFeedbackTests
{
    [Theory]
    [InlineData(UnitPolicyResponse.PostActionReturnOrdered)]
    [InlineData(UnitPolicyResponse.RetreatLowMorale)]
    public void AReturnWithoutDestinationReportsTheFailureInsteadOfSuccessfulRetreat(UnitPolicyResponse decision)
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var view = session.View;
        var mutation = WorldState.Restore(view.World).BeginMutation();
        var fact = UnitPolicyRespondedEvent.Capture(mutation, mutation.Units[10], decision, null, null,
            returnOutcome: UnitPolicyReturnOutcome.NoDestination);
        Assert.Equal(decision, fact.Response);
        var (texts, content) = Content(view);
        var context = new WorldMessageContext(definition, view.World, texts);
        var output = ExecutionPublication.Read(view, view, context, texts, content, [fact]);
        Assert.Equal(ProgressPresentationDefinition.ReturnUnavailableRole, Assert.Single(output.Records).Message.Key);
        var failedSpeech = Assert.Single(output.Messages);
        Assert.Equal(ProgressPresentationDefinition.ReturnUnavailableRole, failedSpeech.Message.Key);
        Assert.NotNull(failedSpeech.MapSpeech);
        Assert.Equal(3, failedSpeech.SpeakerId);

        var dialogue = ExecutionPublication.Read(view, view, context, texts, content, [fact], executionMap: false);
        Assert.Empty(dialogue.Records);
        if (decision == UnitPolicyResponse.PostActionReturnOrdered)
        {
            var portrait = Assert.Single(dialogue.Messages);
            Assert.Equal(ProgressPresentationDefinition.ReturnUnavailableRole, portrait.Message.Key);
            Assert.Equal(3, portrait.SpeakerId);
            Assert.Null(portrait.MapSpeech);
        }
        else Assert.Empty(dialogue.Messages);

        var started = fact with { ReturnOutcome = UnitPolicyReturnOutcome.Started, HomeId = 1 };
        var success = ExecutionPublication.Read(view, view, context, texts, content, [started]);
        Assert.Equal(ProgressFeedback.PolicyRole(decision), Assert.Single(success.Records).Message.Key);
        Assert.Equal(ProgressFeedback.PolicyRole(decision), Assert.Single(success.Messages).Message.Key);
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void PolicyRecordsRetainOccurrenceAuthorityAndSpeechesIncludeAutomaticCorps(bool player, bool direct)
    {
        var definition = MissionScenario.Create();
        var session = GameSession.Start(definition, 42);
        var world = session.View.World;
        world = world with
        {
            Corps = world.Corps.Append(world.Corps.Single(c => c.Id == 1) with { Id = 100, IsRoot = false }).ToArray(),
            Units = world.Units.Select(u => u.OwnerId == 1 ? u with { CorpsId = direct ? 1 : 100 } : u).ToImmutableArray(),
            Simulation = world.Simulation with { Controls = world.Simulation.Controls.Select(c => c.ForceId == 1 ? c with
                { Kind = player ? ControllerKind.Player : ControllerKind.Automatic } : c).ToImmutableArray() }
        };
        var mutation = WorldState.Restore(world).BeginMutation();
        var unit = mutation.Units[10];
        var fact = UnitPolicyRespondedEvent.Capture(mutation, unit, UnitPolicyResponse.PostActionAttackDespiteOrders, 23, 1);
        Assert.Equal(player && direct, fact.ReportEligible);
        // Publication must use the captured authority even after control changes before the final snapshot.
        var changed = world with { Corps = world.Corps.Select(c => c.Id == 1 ? c with
            { Treasury = new(0, 0) } : c).ToArray() };
        var view = new StrategySnapshot(definition, changed);
        var (texts, content) = Content(view);
        var context = new WorldMessageContext(definition, changed, texts);
        var output = ExecutionPublication.Read(view, view, context, texts, content, [fact]);
        Assert.Equal(player && direct ? 1 : 0, output.Records.Count);
        var speech = Assert.Single(output.Messages);
        Assert.NotNull(speech.MapSpeech);
        Assert.Equal(unit.Position, fact.Unit.Position);
        var dialogue = ExecutionPublication.Read(view, view, context, texts, content, [fact], executionMap: false);
        Assert.Empty(dialogue.Records);
        Assert.Equal(player && direct ? 1 : 0, dialogue.Messages.Count);
        Assert.All(dialogue.Messages, message => Assert.Null(message.MapSpeech));
    }

    [Theory]
    [InlineData(false, true, 1, UnitPolicyResponse.PostActionNoTarget)]
    [InlineData(true, true, 1, UnitPolicyResponse.PostActionAttackOrdered)]
    [InlineData(true, true, 3, UnitPolicyResponse.PostActionAvoidDespiteOrders)]
    [InlineData(true, false, 3, UnitPolicyResponse.PostActionReturnOrdered)]
    [InlineData(true, false, 0, UnitPolicyResponse.PostActionAttackDespiteOrders)]
    public void ActualPostActionResultUsesOneChannelForEachPresentationMode(bool hasEnemy, bool attack, int personality, UnitPolicyResponse response)
    {
        var definition = MissionScenario.Create();
        definition = definition with { Military = definition.Military! with { Formations = definition.Military.Formations
            .Select(f => f with { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true),
                Positions = San9.Tests.FormationTestData.Layout(Enumerable.Repeat(BattleRank.Front, f.OfficerCapacity).ToImmutableArray()) }).ToImmutableArray() } };
        definition = definition with { Units = definition.Units.Select(u => u with
            { MemberPositions = San9.Tests.FormationTestData.Members(definition.Officers.Where(p => p.State.UnitId == u.Id).ToImmutableDictionary(p => p.State.Id, _ => BattleRank.Front)) }).ToArray() };
        var session = GameSession.Start(definition, 42);
        var world = session.View.World with
        {
            Officers = session.View.World.Officers.Select(p => p.Id == 3 ? p with { Personality = personality } : p).ToArray(),
            Units = session.View.World.Units.Select(u => u.Id == 10 ? u with
            {
                Order = null, AwaitingPostAction = true, ServicePlaceId = 1, HasEnteredDailyCycle = true,
                Policies = new(AttackAfterAction: attack)
            } : u.Id == 20 ? u with { Position = new(hasEnemy ? 17 : 145, 7), HasEnteredDailyCycle = true } : u).ToArray()
        };
        var view = new StrategySnapshot(definition, world);
        var mutation = WorldState.Restore(world).BeginMutation();
        List<DomainEvent> events = [];
        MilitaryPolicies.ResolvePostAction(mutation, mutation.Units[10], new(definition.Military), events, null);
        var fact = Assert.Single(events.OfType<UnitPolicyRespondedEvent>());
        Assert.Equal(response, fact.Response);
        var after = new StrategySnapshot(definition, mutation.CapturePresentation());
        var (texts, content) = Content(view);
        var context = new WorldMessageContext(definition, view.World, texts);
        var map = ExecutionPublication.Read(view, after, context, texts, content, events);
        Assert.Single(map.Records);
        Assert.NotNull(Assert.Single(map.Messages).MapSpeech);
        var portrait = ExecutionPublication.Read(view, after, context, texts, content, events, executionMap: false);
        Assert.Empty(portrait.Records);
        var dialogue = Assert.Single(portrait.Messages);
        Assert.Null(dialogue.MapSpeech);
        Assert.Equal(3, dialogue.SpeakerId);
        Assert.Equal(MapCoordinates.Cell(mutation.Units[10].Position), dialogue.FocusLocation);
        var plan = BattlePresentationPlan.Create(view, after, events, context, texts, content);
        var rest = ExecutionPublication.Read(view, after, context, texts, content, events, presentedFeedback: plan.PresentedFeedback);
        Assert.Empty(rest.Messages);
        Assert.Empty(rest.Records);
    }
}
