using San9.Tests;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PrisonerReturnTests
{
    [Fact]
    public void FieldRecoveryUsesPhysicalPositionAndCancelsOtherNegotiationsForThePrisoner()
    {
        var world = World();
        var state = world.BeginMutation();
        state.AddUnit(new(30, 2, new(180, 0), 100));
        state.Replace(state.Officers[13] with { SettlementId = null, UnitId = 30 });
        var pending = new PersistentTask(Guid.NewGuid(), PrisonerReturnDiplomacy.Kind, "first", 1, 12, 1,
            state.Clock, state.Clock, 0, 0)
        { Journey = new(2, null, null) { Route = new(new(0, 0), new(300, 0), false) }, PrisonerReturn = new(2, 13) };
        state.SetSimulation(state.Simulation with { Tasks = [pending] });
        List<DomainEvent> events = [];
        Assert.Equal(CommandRejection.None, PrisonerRelease.Depart(state, 13, 2, 1,
            PrisonerDepartureKind.Recovered, null, Rules().Personnel, EconomyRulebook.Empty, events, new(180, 0)));
        var recovered = state.Officers[13];
        Assert.Null(recovered.UnitId);
        Assert.Equal(1, recovered.SettlementId);
        Assert.Equal(OfficerStatus.Serving, recovered.Status);
        var returning = Assert.Single(state.Simulation.Tasks, t => t.OfficerId == 13);
        Assert.Equal(new WorldPosition(180, 0), returning.Journey!.Route.Origin);
        Assert.Equal(PersonnelRulebook.JourneyDays(new(180, 0), new(0, 0)), returning.Journey.ReturnLeg!.CountdownDays);
        Assert.False(returning.Journey.ReturnLeg.DiplomaticEnvoy);
        Assert.Contains(events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == pending.Id);
        Assert.All(state.Simulation.Tasks, t => Assert.Equal(PersonnelReturns.Kind, t.Kind));
    }

    [Theory]
    [InlineData(1, 1, 9)]
    [InlineData(1, 10, 8)]
    [InlineData(3, 30, 0)]
    public void SalaryReserveIncludesOnlyServingPrimaryAccountMembers(int month, int day, int xuns)
    {
        var state = World().BeginMutation();
        var rootTreasury10 = ResourceAccounts.Root(state, 1).Treasury; state.RemoveCorps(ResourceAccounts.Root(state, 1).Id); state.AddCorps(new(10, 1, 11, rootTreasury10) { IsRoot = true });
        state.AddCorps(new(20, 1, 14, new(0, 0)));
        state.Replace(state.Settlements[1] with { CorpsId = 10 });
        state.AddUnit(new(30, 1, new(0, 0), 100) { CorpsId = 20 });
        state.Replace(state.Officers[14] with { SettlementId = null, UnitId = 30, OfficeId = 7 });
        state.Replace(state.Officers[12] with { OfficeId = 7 });
        state.SetClock(new(190, month, day));
        var rules = new SimulationRules { Personnel = Rules().Personnel, Economy = new([new(7, 20)], []) };
        Assert.Equal(20L * xuns, PrisonerReturnDiplomacy.SalaryReserve(state, 1, rules));
        state.Replace(state.Officers[12] with { OfficeId = null });
        Assert.Equal((long)xuns, PrisonerReturnDiplomacy.SalaryReserve(state, 1, rules));
    }

    private static SimulationRules Rules() => new() { Personnel = new([new(1, new(0, 0)), new(2, new(90, 0))],
        new Dictionary<int, int> { [1] = 1, [2] = 2 }, []) };
    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 100) { LeaderId = 11, AdviserId = 14 },
         new ForceState(2, 100) { LeaderId = 21 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0), new(2, 2, 0, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(50, 50, 100, 50), false),
         new OfficerState(13, 1, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive },
         new OfficerState(14, 1, 1, new(50, 100, 50, 50), false) { Status = OfficerStatus.Strategist },
         new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(999900, 0)) { IsRoot = true }]);

    private static SimulationEngine Arrive()
    {
        var engine = new SimulationEngine(World(), Rules());
        Assert.True(engine.Execute(new("first"), new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.AdvanceXun();
        return engine;
    }

    [Fact]
    public void TwoPlayersContinueBothStagesInMemoryRejectStaleAnswersAndPayExactlyOnceWithOverflowAccounted()
    {
        var engine = Arrive();
        var prompt = engine.GetPrisonerReturnPrompt()!;
        Assert.Equal(2, prompt.RespondingForceId);
        Assert.Equal(0, prompt.Step);
        var response = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, prompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Money, 500, null, null) };
        var snapshot = engine.Snapshot;
        var restored = new SimulationEngine(WorldState.Restore(snapshot), Rules());
        var quoted = engine.Execute(new("second"), response);
        Assert.True(quoted.Submission.Accepted);
        EventAssert.Equal(quoted.Events, restored.Execute(new("second"), response).Events);
        Assert.DoesNotContain(quoted.Events, e => e is TaskCompletedEvent or PrisonerReturnResolvedEvent or XunAdvancedEvent);
        prompt = engine.GetPrisonerReturnPrompt()!;
        Assert.Equal(1, prompt.RespondingForceId);
        Assert.Equal(1, prompt.Step);
        var before = engine.Snapshot;
        var final = new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, true);
        Assert.Equal(CommandRejection.InvalidDecision, engine.Execute(new("first"), final with { Step = 0 }).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("second"), final).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        snapshot = engine.Snapshot;
        restored = new(WorldState.Restore(snapshot), Rules());
        var result = engine.Execute(new("first"), final);
        EventAssert.Equal(result.Events, restored.Execute(new("first"), final).Events);
        var resolved = Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>());
        Assert.True(resolved.Accepted);
        Assert.Equal(100u, resolved.Received);
        Assert.Equal(400u, resolved.Spent);
        Assert.Single(result.Events.OfType<ResourceChangedEvent>(), e => e.Reason == "prisoner-compensation" && e.Actual == -500);
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
        Assert.Null(engine.GetPrisonerReturnPrompt());
    }

    [Fact]
    public void LostFundsCancelQuotedTaskInsteadOfPartiallyPaying()
    {
        var engine = Arrive();
        var prompt = engine.GetPrisonerReturnPrompt()!;
        Assert.True(engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, prompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Money, 500, null, null) }).Submission.Accepted);
        var snapshot = engine.Snapshot;
        snapshot = snapshot with { Corps = snapshot.Corps.Select(f => f.IsRoot && f.OwnerId == 1 ? f with { Treasury = f.Treasury with { Money = 499 } } : f).ToArray() };
        engine = new(WorldState.Restore(snapshot), Rules());
        var result = engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, true));
        Assert.True(result.Submission.Accepted);
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == prompt.TaskId);
        Assert.DoesNotContain(result.Events, e => e is PrisonerReturnResolvedEvent or PrisonerRecoveredEvent);
        Assert.DoesNotContain(result.Events, e => e is ResourceChangedEvent resource && resource.Reason == "prisoner-compensation");
    }

    [Fact]
    public void FreeReturnAdviceStillRespectsPlayerChoiceAndAdviserAvailability()
    {
        var snapshot = new SimulationEngine(World(), Rules()).Snapshot;
        snapshot = snapshot with { Relations = [new(1, 2, 100)] };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), Rules());
        Assert.Equal(AdviserOpinion.Unavailable, engine.GetPrisonerReturnAdvice(1, 12, 2, 13).Opinion);
        snapshot = snapshot with
        {
            Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with { AdviserId = null } : f).ToArray(),
            Officers = snapshot.Officers.Select(p => p.Id == 14 ? p with { Status = OfficerStatus.Serving } : p).ToArray(),
            Simulation = snapshot.Simulation with { Controls = [new("first", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic)] }
        };
        engine = new(WorldState.Restore(snapshot), Rules());
        Assert.Equal(AdviserOpinion.NoAdviser, engine.GetPrisonerReturnAdvice(1, 12, 2, 13).Opinion);
        Assert.Equal(AdviserOpinion.Unavailable, engine.GetPrisonerReturnAdvice(1, 12, 2, 999).Opinion);
    }

    [Theory]
    [InlineData(79, false)]
    [InlineData(80, true)]
    [InlineData(100, true)]
    public void AdviceIncludesFreeReturnAndAutomaticControllerCanQuoteThroughPublicResponse(int relation, bool free)
    {
        var snapshot = new SimulationEngine(World(), Rules()).Snapshot;
        snapshot = snapshot with { Simulation = snapshot.Simulation with { Controls = [new("first", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic)] } };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), Rules());
        var before = engine.Snapshot;
        var advice = engine.GetPrisonerReturnAdvice(1, 12, 2, 13);
        Assert.Equal(AdviserOpinion.Favorable, advice.Opinion);
        Assert.Equal(advice, engine.GetPrisonerReturnAdvice(1, 12, 2, 13));
        StateAssert.Equal(before, engine.Snapshot);
        snapshot = engine.Snapshot with { Corps = engine.Snapshot.Corps.Select(f => f.IsRoot && f.OwnerId == 1 ? f with { Treasury = f.Treasury with { Money = 0 } } : f).ToArray(),
            Relations = [new(1, 2, relation)] };
        engine = new(WorldState.Restore(snapshot), Rules());
        var beforeFreeAdvice = engine.Snapshot;
        Assert.Equal(free ? AdviserOpinion.Favorable : AdviserOpinion.Unfavorable,
            engine.GetPrisonerReturnAdvice(1, 12, 2, 13).Opinion);
        StateAssert.Equal(beforeFreeAdvice, engine.Snapshot);
        Assert.True(engine.Execute(new("first"), new ReturnPrisonerCommand(Guid.NewGuid(), 1, 0, 12, 2, 13)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("ai:2"));
        engine.AdvanceXun();
        var prompt = engine.GetPrisonerReturnPrompt()!;
        var result = engine.Execute(new("ai:2"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, engine.Snapshot.Simulation.NextSequence, prompt.TaskId, 0, free)
            { Offer = free ? new(CompensationKind.Free, 0, null, null) : null });
        var resolved = Assert.Single(result.Events.OfType<PrisonerReturnResolvedEvent>());
        Assert.Equal(free, resolved.Accepted);
        Assert.Equal(free ? CompensationKind.Free : CompensationKind.Refuse, resolved.Offer.Kind);
    }
}
