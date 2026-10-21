using System.Collections.Immutable;
using San9.Tests;
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
        [new ForceState(1, 100) { LeaderId = 11 }, new ForceState(2, 100) { LeaderId = 21 },
         new ForceState(3, 100) { LeaderId = 31 }],
        [new(1, 1, 10000, 100, 100, 0, 0, 0, 0, 0, 0), new(2, 2, 10000, 100, 100, 0, 0, 0, 0, 0, 0),
         new(3, 3, 10000, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(11, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(50, 50, 100, 50), false),
         new OfficerState(21, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Righteousness = 5 },
         new OfficerState(22, 2, exchange ? 1 : 2, new(50, 50, 50, 50), false) { Status = exchange ? OfficerStatus.Captive : OfficerStatus.Serving },
         new OfficerState(31, 3, 3, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }],
        treasures: [new(9, 11, TreasureDiscovery.Discovered, null)],
        controls: [new("first", 1, ControllerKind.Player), new("second", 2, automatic ? ControllerKind.Automatic : ControllerKind.Player),
            new("third", 3, ControllerKind.Automatic)], relations: [new(1, 2, relation), new(1, 3, 0), new(2, 3, 0)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 31, new(10000, 10000)) { IsRoot = true }]);

    [Theory]
    [InlineData(100, AdviserOpinion.Favorable)]
    [InlineData(60, AdviserOpinion.Uncertain)]
    [InlineData(40, AdviserOpinion.Unfavorable)]
    public void AdviserSeparatesFreeAcceptanceCounterofferAndRefusal(int relation, AdviserOpinion expected)
    {
        var basis = new SimulationEngine(World(relation), Rules()).Snapshot;
        var world = basis with
        {
            Forces = basis.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 12 } : f).ToArray(),
            Officers = basis.Officers.Select(p => p.Id == 12 ? p with
                { Status = OfficerStatus.Strategist, BaseAbilities = new(100, 100, 100, 100) } :
                p.Id == 21 ? p with { Righteousness = 6 } : p).ToArray(),
            Simulation = basis.Simulation with { Controls = [new("first", 1, ControllerKind.Player),
                new("second", 2, ControllerKind.Automatic), new("third", 3, ControllerKind.Automatic)] }
        };
        var engine = new SimulationEngine(WorldState.Restore(world), Rules());
        var before = engine.Snapshot;
        Assert.Equal(expected, engine.GetAttackRequestAdvice(1, 12, 2, 3).Opinion);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Theory]
    [InlineData(100, false, 49, AdviserOpinion.Uncertain)]
    [InlineData(100, false, 50, AdviserOpinion.Unfavorable)]
    [InlineData(0, true, 49, AdviserOpinion.Favorable)]
    [InlineData(0, true, 50, AdviserOpinion.Unfavorable)]
    [InlineData(0, false, 49, AdviserOpinion.Favorable)]
    [InlineData(0, false, 50, AdviserOpinion.Uncertain)]
    public void MistakenNegotiationAdviceCanChooseEitherOtherCategory(int freeThreshold, bool payment,
        int mistakeRoll, AdviserOpinion expected)
    {
        var snapshot = new SimulationEngine(World(), Rules()).Snapshot;
        snapshot = snapshot with
        {
            Forces = snapshot.Forces.Select(f => f.Id == 1 ? f with { AdviserId = 12 } : f).ToArray(),
            Officers = snapshot.Officers.Select(p => p.Id == 12 ? p with
                { Status = OfficerStatus.Strategist, BaseAbilities = new(70, 70, 70, 70) } : p).ToArray()
        };
        var world = WorldState.Restore(snapshot).BeginMutation();
        Assert.Equal(expected, AdviserJudgment.EvaluateNegotiation(world, 1, 90, mistakeRoll,
            freeThreshold, 0, payment).Opinion);
    }

    private static SimulationEngine Arrive(bool automatic = false, int relation = 80, bool exchange = false)
    {
        var engine = new SimulationEngine(World(relation, automatic, exchange), Rules());
        Assert.True(engine.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        engine.AdvanceXun();
        return engine;
    }

    [Theory]
    [InlineData(true, true)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(false, false)]
    public void CounterofferAdviceIsReadOnlyAndUsesTheRequestersController(bool player, bool hasAdviser)
    {
        var snapshot = new SimulationEngine(World(), Rules()).Snapshot;
        snapshot = snapshot with
        {
            Forces = snapshot.Forces.Select(f => f.Id == 2 ? f with { AdviserId = hasAdviser ? 22 : null } : f).ToArray(),
            Officers = snapshot.Officers.Select(p => p.Id == 22 && hasAdviser ? p with { Status = OfficerStatus.Strategist } : p).ToArray(),
            Simulation = snapshot.Simulation with { Controls = snapshot.Simulation.Controls.Select(c =>
                c.ForceId == 1 ? c with { Kind = player ? ControllerKind.Player : ControllerKind.Automatic } : c).ToImmutableArray() }
        };
        var engine = new SimulationEngine(WorldState.Restore(snapshot), Rules());
        Assert.True(engine.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        engine.EndControl(new("first")); engine.EndControl(new("second")); engine.EndControl(new("third"));
        engine.AdvanceXun();
        var prompt = engine.GetAttackRequestPrompt()!;
        var before = engine.Snapshot;
        var offer = new CompensationOffer(CompensationKind.Money, 1000, null, null);
        var advice = engine.GetCounterofferAdvice(prompt.TaskId, offer);
        Assert.Equal(CommandRejection.None, advice.Rejection);
        Assert.Equal(hasAdviser ? 22 : (int?)null, advice.AdviserId);
        Assert.Equal(!hasAdviser ? AdviserOpinion.NoAdviser : player ? AdviserOpinion.Unavailable : AdviserOpinion.Favorable, advice.Opinion);
        Assert.Equal(advice, engine.GetCounterofferAdvice(prompt.TaskId, offer));
        Assert.Equal(CommandRejection.InvalidTask, engine.GetCounterofferAdvice(Guid.NewGuid(), offer).Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2,
            before.Simulation.NextSequence, prompt.TaskId, 0, true) { Offer = offer }).Submission.Accepted);
        Assert.Equal(CommandRejection.InvalidTask, engine.GetCounterofferAdvice(prompt.TaskId, offer).Rejection);
        if (!player && hasAdviser)
            Assert.Equal(advice.Opinion == AdviserOpinion.Favorable,
                engine.GetAutomaticDiplomacyResponse(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence).Accept);
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
        EventAssert.Equal(engine.Execute(new("second"), quote).Events, restored.Execute(new("second"), quote).Events);
        prompt = engine.GetAttackRequestPrompt()!;
        Assert.Equal(1, prompt.Step);
        Assert.Equal(1, prompt.RespondingForceId);
        restored = Restore(engine);
        var answer = new RespondDailyTaskCommand(Guid.NewGuid(), 1, 2, prompt.TaskId, 1, accept);
        var result = engine.Execute(new("first"), answer);
        EventAssert.Equal(result.Events, restored.Execute(new("first"), answer).Events);
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
        var freePrompt = free.GetAttackRequestPrompt()!;
        Assert.True(free.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, freePrompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Free, 0, null, null) }).Submission.Accepted);
        Assert.Null(free.GetAttackRequestPrompt());
        Assert.Single(free.Snapshot.Simulation.AttackRequests);
        var exchange = Arrive(true, exchange: true);
        var quotePrompt = exchange.GetAttackRequestPrompt()!;
        Assert.True(exchange.Execute(new("second"), new RespondDailyTaskCommand(Guid.NewGuid(), 2, 1, quotePrompt.TaskId, 0, true)
            { Offer = new(CompensationKind.Prisoner, 0, 22, null) }).Submission.Accepted);
        var prompt = exchange.GetAttackRequestPrompt()!;
        Assert.Equal(CompensationKind.Prisoner, prompt.Offer!.Kind);
        var result = exchange.Execute(new("first"), new RespondDailyTaskCommand(Guid.NewGuid(), 1, exchange.Snapshot.Simulation.NextSequence, prompt.TaskId, prompt.Step, true));
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
        engine = new(WorldState.Restore(snapshot with { Corps = snapshot.Corps.Select(f => f.IsRoot && f.OwnerId == 1 ? f with { Treasury = f.Treasury with { Money = 999 } } : f).ToArray() }), Rules());
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
        state.Replace(ResourceAccounts.Root(state, 1) with { Treasury = ResourceAccounts.Root(state, 1).Treasury with { Money = money } });
        var task = new PersistentTask(Guid.NewGuid(), AttackRequestDiplomacy.Kind, "first", 1, 12, 1,
            state.Clock, state.Clock, 0, 0) { ResolutionStep = 1, Journey = new(2, null, 99),
            AttackRequest = new(2, 3) { Offer = new(CompensationKind.Money, quote, null, null) } };
        Assert.Equal(expected, AttackRequestDiplomacy.Accept(state, task, Rules()));
    }

    [Fact]
    public void AdviceViewSurvivesPreviewButRefreshesAfterCommandCommit()
    {
        var engine = new SimulationEngine(World(60, automatic: true), Rules());
        var before = engine.Snapshot;
        var attack = engine.GetAttackRequestAdvice(1, 12, 2, 3);
        var surrender = engine.GetSurrenderAdvice(1, 12, 2);
        Assert.Equal(CommandRejection.None, attack.Rejection);
        Assert.Equal(CommandRejection.None, surrender.Rejection);
        var command = new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3);
        Assert.True(engine.Validate(new("first"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Equal(attack, engine.GetAttackRequestAdvice(1, 12, 2, 3));
        Assert.Equal(surrender, engine.GetSurrenderAdvice(1, 12, 2));

        Assert.True(engine.Execute(new("first"), command).Submission.Accepted);
        var committed = engine.Snapshot;
        var fresh = Restore(engine);
        Assert.Equal(CommandRejection.PersonUnavailable, engine.GetAttackRequestAdvice(1, 12, 2, 3).Rejection);
        Assert.Equal(CommandRejection.PersonUnavailable, engine.GetSurrenderAdvice(1, 12, 2).Rejection);
        foreach (int actor in new[] { 11, 12 })
        {
            Assert.Equal(fresh.GetAttackRequestAdvice(1, actor, 2, 3), engine.GetAttackRequestAdvice(1, actor, 2, 3));
            Assert.Equal(fresh.GetSurrenderAdvice(1, actor, 2), engine.GetSurrenderAdvice(1, actor, 2));
        }
        StateAssert.Equal(committed, engine.Snapshot);
    }

    [Fact]
    public void SubmissionDiceIgnoreSequenceAndQueriesDoNotMutateWorld()
    {
        var first = new SimulationEngine(World(60), Rules());
        var second = new SimulationEngine(World(60), Rules());
        var later = second.Snapshot;
        second = new(WorldState.Restore(later with { Simulation = later.Simulation with { NextSequence = 50 } }), Rules());
        var before = first.Snapshot;
        Assert.Equal(first.GetAttackRequestAdvice(1, 12, 2, 3), first.GetAttackRequestAdvice(1, 12, 2, 3));
        StateAssert.Equal(before, first.Snapshot);
        Assert.True(first.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 0, 12, 2, 3)).Submission.Accepted);
        Assert.True(second.Execute(new("first"), new RequestAttackCommand(Guid.NewGuid(), 1, 50, 12, 2, 3)).Submission.Accepted);
        Assert.Equal(first.Snapshot.Simulation.Tasks.Single().Journey!.SuccessRoll, second.Snapshot.Simulation.Tasks.Single().Journey!.SuccessRoll);
    }

    private static SimulationEngine Restore(SimulationEngine engine) => new(WorldState.Restore(
        engine.Snapshot), Rules());
}
