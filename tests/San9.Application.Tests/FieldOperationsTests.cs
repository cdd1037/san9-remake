using System.Collections.Immutable;
using San9.Application.AI;
using San9.Application.Persistence;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class FieldOperationsTests
{
    private static ScenarioInitialState Fixture() => new(ScenarioInitialState.CurrentSchemaVersion, "field", 190, 1, 1, 1,
        [new(1, "enemy", 1, 100) { LeaderId = 1 }, new(2, "own", 2, 100) { LeaderId = 2 }],
        [new(1, "enemy-home", 1, 8, 1, 1000, 1000, 1000, 100, []),
            new(2, "home", 2, 0, 1, 1000, 1000, 1000, 100, [])], [], [])
    {
        Officers = [new("enemy", new(1, 1, null, new(40, 40, 40, 40), false) { Status = OfficerStatus.Ruler, UnitId = 10 }),
            new("own", new(2, 2, null, new(90, 70, 70, 90), false) { Status = OfficerStatus.Ruler, UnitId = 20 })],
        Units = [new(10, 1, new(22, 7), 1500) { FormationId = 7, Morale = 100, CommanderId = 1, ServicePlaceId = 1,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(1, BattleRank.Middle) },
            new(20, 2, new(17, 7), 8000) { FormationId = 7, Morale = 100, CommanderId = 2, ServicePlaceId = 2,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Middle) }],
        Military = new(10, 3, Enumerable.Repeat(0, 30).ToImmutableArray(), [new(0, 1, null)],
            [new(7, 10, 0, 20000, 5, new(1, 1), TravelMode.Land) { Combat = new(20, 20, 10, 20, 50), Attack = new(1, false),
                CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] }],
            [new(1, new(42, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
                new(2, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }]),
        Information = new([], [], [], [new CorpsState(1, 1, 1, new(10000, 100000)) { IsRoot = true },
            new CorpsState(2, 2, 2, new(10000, 100000)) { IsRoot = true }], [])
    };

    private static (SimulationEngine Engine, FieldObservation Observation) Observe(ScenarioInitialState definition, long budget)
    {
        var session = GameSession.Start(definition, 42);
        var engine = GameSession.EngineFactory(definition)(WorldState.Restore(session.Snapshot));
        engine.EndControl(new("player"));
        var seat = engine.Snapshot.Simulation.Controls.Single(c => c.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        var before = engine.Snapshot;
        var result = FieldOperations.Capture(engine, seat, [(seat.ControllerId, economy)], session.MilitaryRules!, new(budget), new());
        StateAssert.Equal(before, engine.Snapshot);
        return (engine, result);
    }

    [Theory]
    [InlineData(512)]
    [InlineData(2048)]
    [InlineData(8192)]
    [InlineData(65536)]
    public void AdvantageousFieldTargetIsLegallyInterceptedAtEveryBudget(long budget)
    {
        Assert.True(FieldOperations.ExchangeValue(new(8000, 100, 90, 1, 10), new(20, 20, 10, 20, 50),
            new(1500, 100, 40, 1, 10), new(20, 20, 10, 20, 50), true, 1, new(100)) > 0);
        var (engine, observation) = Observe(Fixture(), budget);
        var candidate = Assert.Single(observation.Candidates);
        var attack = Assert.IsType<OrderUnitCommand>(candidate.Intent);
        Assert.Equal(10, attack.Order.UnitId);
        Assert.False(attack.Policies!.Pursue);
        Assert.True(engine.Execute(new("ai:2"), attack with
            { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence }).Submission.Accepted);
    }

    [Fact]
    public void LosingPursuitReturnsButUnfinishedAnalysisDoesNotOrderWithdrawal()
    {
        var basis = Fixture();
        var definition = basis with { Units = basis.Units.Select(u => u.Id == 20 ? u with { Soldiers = 1000,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 10 } } : u with { Soldiers = 20000 }).ToArray() };
        var observed = Observe(definition, 65536).Observation;
        var retreat = Assert.IsType<OrderUnitCommand>(Assert.Single(observed.Candidates).Intent);
        Assert.Equal(MilitaryOrderKind.Return, retreat.Order.Kind);
        Assert.Equal(2, retreat.Order.SettlementId);
        var incomplete = Observe(definition, 1).Observation;
        Assert.False(incomplete.Complete);
        Assert.Empty(incomplete.Candidates);
    }

    [Fact]
    public void HiddenEnemyOrderCannotChangeFieldDecision()
    {
        var basis = Fixture();
        var other = basis with { Units = basis.Units.Select(u => u.Id == 10 ? u with
            { Order = new(MilitaryOrderKind.Return, null, 1) } : u).ToArray() };
        var first = Observe(basis, 65536).Observation;
        var second = Observe(other, 65536).Observation;
        Assert.Equal(first.Candidates.Select(c => c.Intent), second.Candidates.Select(c => c.Intent));
    }

    [Fact]
    public void AcceptedMissionCannotBeOverwrittenByAnotherFrozenOrderOrPolicy()
    {
        var definition = Fixture();
        var (engine, observation) = Observe(definition, 65536);
        var candidate = observation.Candidates.First(c => c.Intent is OrderUnitCommand);
        var seat = engine.Snapshot.Simulation.Controls.Single(c => c.ForceId == 2);
        var economy = EconomicObservation.Capture(engine, seat, new(seat.ControllerId), 1000);
        var batch = new AllocationObservations { Economies = [(seat.ControllerId, economy)] };
        var command = candidate.Intent with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        var result = engine.Execute(new(seat.ControllerId), command);
        Assert.True(result.Submission.Accepted);
        batch.Applied(engine, seat, new MilitaryRulebook(definition.Military!), command, candidate.Intent, candidate, result.Events);
        var oldReturn = candidate with { Intent = new OrderUnitCommand(Guid.Empty, 2, 0, 20, new(MilitaryOrderKind.Return, null, 2)) };
        var oldPolicy = candidate with { Intent = new SetUnitPoliciesCommand(Guid.Empty, 2, 0, 20, new(Pursue: true)) };
        Assert.Empty(batch.Remaining([oldReturn, oldPolicy]));
    }

    [Fact]
    public void WoundedArmyReturnsToAReceivingPlaceWithEnoughCapacity()
    {
        var basis = Fixture();
        var definition = basis with { Units = basis.Units.Select(u => u.Id == 20 ? u with { Soldiers = 1000, Wounded = 2000 } : u).ToArray() };
        var candidate = Assert.Single(Observe(definition, 65536).Observation.Candidates);
        Assert.Equal(MilitaryOrderKind.Return, Assert.IsType<OrderUnitCommand>(candidate.Intent).Order.Kind);
        var full = definition with { Settlements = definition.Settlements.Select(s => s.Id == 2 ? s with
            { Soldiers = s.SoldierCapacity } : s).ToArray() };
        Assert.Empty(Observe(full, 65536).Observation.Candidates);
    }

    [Fact]
    public void IncompatibleEquipmentIsImprovedAndPublicSelectionMatchesEligibility()
    {
        var basis = Fixture();
        var definition = basis with
        {
            Officers = basis.Officers.Select(p => p.State.Id == 2 ? p with { State = p.State with { LearnedTactics = [42] } } : p).ToArray(),
            Military = basis.Military! with { Tactics = [new(42, 0, TacticEffect.Martial, 20, 3, 0, true, false, [7])],
                Formations = basis.Military.Formations.Select(f => f with { Tactics = new(100, 0, 2, [0], []) }).ToImmutableArray() }
        };
        var (engine, observation) = Observe(definition, 65536);
        var selection = Assert.Single(observation.Candidates.Select(c => c.Intent).OfType<SelectTacticCommand>());
        Assert.Equal(42, selection.TacticId);
        var command = selection with { CommandId = Guid.NewGuid(), Sequence = engine.Snapshot.Simulation.NextSequence };
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(new("ai:2"), command));
        Assert.True(engine.Execute(new("ai:2"), command).Submission.Accepted);
    }

    [Fact]
    public void ProductionInterceptionAndRecoveryReplayFromStableSave()
    {
        var definition = Fixture();
        var session = GameSession.Start(definition, 42);
        session.BeginRecording();
        var restored = SaveService.Decode(SaveService.Encode(session), definition);
        var first = session.Apply(session.Request(new PrepareAdvanceOperation()));
        EventAssert.Equal(first.Events, restored.Apply(restored.Request(new PrepareAdvanceOperation())).Events);
        Assert.Contains(first.Events.OfType<UnitOrderChangedEvent>(), e => e.UnitId == 20 && e.Order?.UnitId == 10);
        for (int i = 0; i < 2; i++) EventAssert.Equal(session.AdvanceXun().Events, restored.AdvanceXun().Events);
        StateAssert.Equal(session.Capture(), restored.Capture());
        var replay = RecordingService.Replay(RecordingService.Encode(session.EndRecording()), definition, GameSession.EngineFactory(definition));
        StateAssert.Equal(session.Capture(), replay.Session.Capture());
    }
}
