using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackRequestDiplomacyTests
{
    private static SimulationRules Rules() => new() { Treasures = new([new(9, default, 1)]), Personnel = new(
        [new(1, new(0, 0)), new(2, new(90, 0)), new(3, new(180, 0))],
        new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, [new(3, 2), new(2, 3)]) };
    private static WorldState World(int relation = 80, bool automatic = false, bool exchange = false) => WorldState.Create(new(190, 1, 1), 42,
        [new ForceState(1, 10000, 10000, 100) { LeaderId = 11 }, new ForceState(2, 10000, 10000, 100) { LeaderId = 21 },
         new ForceState(3, 10000, 10000, 100) { LeaderId = 31 }],
        [new(1, 1, 10000, 100, 100, 0, 0, 0, 0, 0, 0), new(2, 2, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(3, 3, 10000, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(50, 50, 100, 50), false),
         new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Righteousness = 5 },
         new OfficerState(22, 2, exchange ? 1 : 2, new(50, 50, 50, 50), false) { Status = exchange ? OfficerStatus.Captive : OfficerStatus.Serving },
         new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        treasures: [new(9, 11, TreasureDiscovery.Discovered, null)],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, automatic ? ControllerKind.Automatic : ControllerKind.Player),
            new("third", 3, ControllerKind.Automatic)], relations: [new(1, 2, relation), new(1, 3, 0), new(2, 3, 0)]);

    private static SimulationEngine Arrive(bool automatic = false, int relation = 80, bool exchange = false)
    {
        var engine = new SimulationEngine(World(relation, automatic, exchange), Rules());
        Assert.True(engine.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        engine.AdvanceXun();
        return engine;
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void PlayerCounterofferContinuesBothStagesInMemoryAndSettlesOnce(bool accept)
    {
        var engine = Arrive();
        var prompt = engine.GetAttackRequestPrompt()!;
        Assert.Equal(0, prompt.Step);
        var restored = Restore(engine);
        var quote = new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, prompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Money, 1000, null, null) };
        Assert.Equal(CanonicalJson.Text(engine.Execute(new("second"), quote).Events), CanonicalJson.Text(restored.Execute(new("second"), quote).Events));
        prompt = engine.GetAttackRequestPrompt()!;
        Assert.Equal(1, prompt.Step);
        Assert.Equal(1, prompt.RespondingForceId);
        restored = Restore(engine);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, accept);
        var result = engine.Execute(new("first"), answer);
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.Execute(new("first"), answer).Events));
        Assert.Equal(accept ? AttackRequestOutcome.Accepted : AttackRequestOutcome.Refused,
            Assert.Single(result.Events.OfType<AttackRequestResolvedEvent>()).Outcome);
        Assert.Equal(accept ? 1 : 0, result.Events.OfType<AttackRequestEstablishedEvent>().Count());
        Assert.Equal(accept ? 1000u : 0u, result.Events.OfType<AttackRequestResolvedEvent>().Single().Received);
        Assert.Single(result.Events.OfType<XunAdvancedEvent>());
        if (accept) Assert.Equal(18, Assert.Single(engine.Snapshot.Simulation.AttackRequests).RemainingXun);
    }

    [Fact]
    public void AutomaticFreeAndPrisonerOffersUseDifferentBranches()
    {
        var free = Arrive(true);
        Assert.Null(free.GetAttackRequestPrompt());
        Assert.Single(free.Snapshot.Simulation.AttackRequests);
        var exchange = Arrive(true, exchange: true);
        var prompt = exchange.GetAttackRequestPrompt()!;
        Assert.Equal(CompensationKind.Prisoner, prompt.Offer!.Kind);
        var result = exchange.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 1, prompt.TaskId, prompt.Step, true));
        Assert.Single(result.Events.OfType<PrisonerRecoveredEvent>());
        Assert.Single(exchange.Snapshot.Simulation.AttackRequests);
    }

    [Fact]
    public void ExistingIdenticalAgreementFinishesWithoutQuotePaymentRelationOrRenewal()
    {
        var world = World();
        var state = world.BeginMutation();
        AttackRequests.Establish(state, 2, 1, 3, []);
        state.SetSimulation(state.Simulation with { AttackRequests = [state.Simulation.AttackRequests[0] with { RemainingXun = 7 }] });
        world.Commit(state);
        var engine = new SimulationEngine(world, Rules());
        Assert.True(engine.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        var result = engine.AdvanceXun();
        Assert.Equal(AttackRequestOutcome.AlreadyAgreed, Assert.Single(result.Events.OfType<AttackRequestResolvedEvent>()).Outcome);
        Assert.DoesNotContain(result.Events, e => e is CompensationQuotedEvent or AttackRequestEstablishedEvent);
        Assert.Equal(80, engine.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
        Assert.Equal(5, Assert.Single(engine.Snapshot.Simulation.AttackRequests).RemainingXun);
    }

    [Fact]
    public void HonestRulerRequiresAbilityToFulfillButLowRighteousnessMayQuoteWithoutIt()
    {
        var state = World().BeginMutation();
        var rules = Rules();
        Assert.True(AttackRequestDiplomacy.CanFulfill(state, 1, 2, 3, rules));
        state.Replace(state.Settlements[2] with { Soldiers = 9999 });
        Assert.False(AttackRequestDiplomacy.CanFulfill(state, 1, 2, 3, rules));
        Assert.Equal(CompensationKind.Money, AttackRequestDiplomacy.PaymentQuote(state, 1, 12, 2, 3, rules).Kind);
        state.Replace(state.Officers[21] with { Righteousness = 6 });
        Assert.Equal(CompensationKind.Refuse, AttackRequestDiplomacy.PaymentQuote(state, 1, 12, 2, 3, rules).Kind);
    }

    [Fact]
    public void TreasureQuoteTransfersOwnershipAndLostFundsCancelWithoutAnAgreement()
    {
        var engine = Arrive();
        var prompt = engine.GetAttackRequestPrompt()!;
        Assert.True(engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, prompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Treasure, 0, null, 9) }).Submission.Accepted);
        var result = engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, true));
        Assert.Equal(21, engine.Snapshot.Treasures.Single().HolderId);
        Assert.Equal(AttackRequestOutcome.Accepted, Assert.Single(result.Events.OfType<AttackRequestResolvedEvent>()).Outcome);

        engine = Arrive();
        prompt = engine.GetAttackRequestPrompt()!;
        Assert.True(engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, prompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Money, 1000, null, null) }).Submission.Accepted);
        var snapshot = engine.Snapshot;
        engine = new(WorldState.Restore(snapshot with { Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with { Money = 999 } : f).ToArray() }), Rules());
        result = engine.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, true));
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == prompt.TaskId);
        Assert.DoesNotContain(result.Events, e => e is AttackRequestEstablishedEvent or AttackRequestResolvedEvent);
        Assert.Empty(engine.Snapshot.Simulation.AttackRequests);
    }

    [Theory]
    [InlineData(1000, 2000, false)]
    [InlineData(1000, 2002, true)]
    [InlineData(3200, 10000, true)]
    [InlineData(3201, 10000, false)]
    public void AutomaticMoneyAcceptanceUsesStrictLiquidityAndLiveRulerInputs(uint quote, uint money, bool expected)
    {
        var state = World(relation: 60).BeginMutation();
        state.Replace(state.Forces[1] with { Money = money });
        var task = new PersistentTask(Guid.NewGuid(), AttackRequestDiplomacy.Kind, "first", 1, 12, 1,
            state.Clock, state.Clock, 0, 0) { ResolutionStep = 1, Journey = new(2, null, 99),
            AttackRequest = new(2, 3) { Offer = new(CompensationKind.Money, quote, null, null) } };
        Assert.Equal(expected, AttackRequestDiplomacy.Accept(state, task, Rules()));
    }

    [Fact]
    public void SubmissionDiceIgnoreSequenceAndQueriesDoNotMutateWorld()
    {
        var first = new SimulationEngine(World(60), Rules());
        var second = new SimulationEngine(World(60), Rules());
        var later = second.Snapshot;
        second = new(WorldState.Restore(later with { Simulation = later.Simulation with { NextSequence = 50 } }), Rules());
        var before = first.Snapshot.StateHash;
        Assert.Equal(first.GetAttackRequestAdvice(1, 12, 2, 3), first.GetAttackRequestAdvice(1, 12, 2, 3));
        Assert.Equal(before, first.Snapshot.StateHash);
        Assert.True(first.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        Assert.True(second.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 50, 12, 2, 3)).Submission.Accepted);
        Assert.Equal(first.Snapshot.Simulation.Tasks.Single().Journey!.SuccessRoll, second.Snapshot.Simulation.Tasks.Single().Journey!.SuccessRoll);
    }

    private static SimulationEngine Restore(SimulationEngine engine) => new(WorldState.Restore(
        engine.Snapshot), Rules());
}
