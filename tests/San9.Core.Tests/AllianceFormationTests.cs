using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AllianceFormationTests
{
    private static SimulationRules Rules() => new()
    {
        Personnel = new(Enumerable.Range(1, 5).Select(id => new PersonPlace(id * 100, new(id, 0))),
            cityProvinces: Enumerable.Range(1, 5).ToDictionary(id => id * 100, _ => 10))
    };

    private static SimulationEngine Ready(ulong seed = 5, params int[] players)
    {
        var controls = Enumerable.Range(1, 5).Select(id => new ControlSeat($"controller:{id}", id,
            players.Contains(id) ? ControllerKind.Player : ControllerKind.Automatic)).ToImmutableArray();
        var world = WorldState.Create(new(190, 1, 1), seed,
            Enumerable.Range(1, 5).Select(id => new ForceState(id, 0, 100000, 50) { BaseTrust = 50 }),
            Enumerable.Range(1, 5).Select(id => new SettlementState(id * 100, id, 0, 100, 100, 0, 500, 0, 0, 0, 0)
                { Population = 100000, DraftPopulation = id == 1 ? 400001u : 0 }), [],
            relations: Enumerable.Range(1, 5).SelectMany(first => Enumerable.Range(first + 1, 5 - first)
                .Select(second => new ForceRelation(first, second, first == 1 && second == 5 ? 80 : 40))), controls: controls);
        var initial = new SimulationEngine(world, Rules()).CaptureStableState();
        return new(WorldState.Restore(initial with { Clock = new(190, 12, 21),
            Simulation = initial.Simulation with { ActiveControl = controls.Length } }), Rules());
    }

    private static CommandExecutionResult Answer(SimulationEngine engine, int player, bool join) => engine.Execute(new($"controller:{player}"),
        new AllianceParticipationCommand(Guid.NewGuid(), player, engine.Snapshot.Simulation.NextSequence, 1, join));

    [Fact]
    public void AllAutomaticFormationSelectsLaterTrustTieAndInitializesFullDuration()
    {
        var engine = Ready();
        var result = engine.AdvanceXun();
        var formed = Assert.Single(result.Events.OfType<AllianceFormedEvent>());
        Assert.Equal(new[] { 2, 3, 4 }, formed.Alliance.MemberForceIds);
        Assert.Equal(4, formed.Alliance.LeaderForceId);
        Assert.Equal(36, formed.Alliance.RemainingXun);
        Assert.Equal(75, result.Snapshot.Forces.Single(f => f.Id == 4).BaseTrust);
        Assert.Equal(20, result.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 2).Value);
        Assert.Equal(60, result.Snapshot.Relations.Single(r => r.FirstId == 2 && r.SecondId == 3).Value);
        Assert.Equal(80, result.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 5).Value);
        Assert.Equal(SimulationBoundary.Stable, result.Snapshot.Simulation.Boundary);
        foreach (var control in engine.Snapshot.Simulation.Controls) engine.EndControl(new(control.ControllerId));
        Assert.Equal(35, engine.AdvanceXun().Snapshot.Simulation.Alliance!.RemainingXun);
    }

    [Fact]
    public void GateDoesNotRunBeforeFirstYearAndUsesOneQuarterlyRoll()
    {
        var engine = Ready(0);
        var result = engine.AdvanceXun();
        Assert.Null(result.Snapshot.Simulation.Alliance);
        Assert.Null(result.Snapshot.Simulation.PendingEvent);
        Assert.Single(result.Snapshot.Simulation.RandomStreams.Where(r => r.Name == "alliance-formation"));
        Assert.DoesNotContain(result.Snapshot.Simulation.RandomStreams, r => r.Name == "alliance-target");
        var beforeYear = new SimulationEngine(WorldState.Restore(Ready().Snapshot with { Clock = new(190, 3, 21) }), Rules());
        var early = beforeYear.AdvanceXun();
        Assert.Null(early.Snapshot.Simulation.Alliance);
        Assert.DoesNotContain(early.Snapshot.Simulation.RandomStreams, r => r.Name.StartsWith("alliance-", StringComparison.Ordinal));
    }

    [Fact]
    public void ExistingAllianceSuppressesFormationAndItsRandomStream()
    {
        var initial = Ready().Snapshot;
        var engine = new SimulationEngine(WorldState.Restore(initial with { Simulation = initial.Simulation with
            { Alliance = new(1, 4, [2, 3, 4], 10) } }), Rules());
        var result = engine.AdvanceXun();
        Assert.Equal(9, result.Snapshot.Simulation.Alliance!.RemainingXun);
        Assert.Empty(result.Events.OfType<AllianceProposedEvent>());
        Assert.DoesNotContain(result.Snapshot.Simulation.RandomStreams, r => r.Name.StartsWith("alliance-", StringComparison.Ordinal));
    }

    [Fact]
    public void PlayerOutsideAutomaticEligibilityCanJoinAtAnInMemoryDecisionBoundary()
    {
        var engine = Ready(5, 5);
        var arrival = engine.AdvanceXun();
        Assert.Equal(SimulationBoundary.AwaitingDecision, engine.Snapshot.Simulation.Boundary);
        var prompt = engine.GetAllianceParticipationPrompt()!;
        Assert.Equal(new[] { 2, 3, 4 }, prompt.Proposal.AutomaticMemberForceIds);
        Assert.Equal(5, prompt.Proposal.RespondingForceId);
        Assert.Null(engine.Snapshot.Simulation.Alliance);
        Assert.DoesNotContain(arrival.Events.OfType<StageCompletedEvent>(), e => e.Stage == ResolutionStage.QuarterUpdate);
        Assert.Single(arrival.Events.OfType<XunAdvancedEvent>());
        string before = engine.Snapshot.StateHash;
        var restored = new SimulationEngine(WorldState.Restore(engine.Snapshot), Rules());
        Assert.Empty(restored.InitialEvents);
        var command = new AllianceParticipationCommand(Guid.NewGuid(), 5, engine.Snapshot.Simulation.NextSequence, 1, true);
        Assert.True(engine.Validate(new("controller:5"), command).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        var expected = engine.Execute(new("controller:5"), command);
        var actual = restored.Execute(new("controller:5"), command);
        Assert.True(expected.Submission.Accepted);
        Assert.Equal(expected.Snapshot.StateHash, actual.Snapshot.StateHash);
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
        Assert.Equal(new[] { 2, 3, 4, 5 }, expected.Snapshot.Simulation.Alliance!.MemberForceIds);
        Assert.Equal(5, expected.Snapshot.Simulation.Alliance.LeaderForceId);
        Assert.Equal(new[] { ResolutionStage.QuarterStudyOffers, ResolutionStage.QuarterUpdate, ResolutionStage.AnnualAppearance, ResolutionStage.AnnualCleanup },
            expected.Events.OfType<StageCompletedEvent>().Select(e => e.Stage));
        Assert.Empty(expected.Events.OfType<XunAdvancedEvent>());
        Assert.Equal(SimulationBoundary.Stable, expected.Snapshot.Simulation.Boundary);
        Assert.Equal(0, expected.Snapshot.Simulation.ActiveControl);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void MultiplePlayersAnswerInForceOrderAndInsufficientParticipationFailsWithoutAllianceEffects(bool firstJoins)
    {
        var engine = Ready(5, 2, 3, 4);
        engine.AdvanceXun();
        var before = engine.Snapshot;
        Assert.False(Answer(engine, 3, true).Submission.Accepted);
        Assert.Equal(before.StateHash, engine.Snapshot.StateHash);
        Assert.True(Answer(engine, 2, firstJoins).Submission.Accepted);
        Assert.Equal(3, engine.GetAllianceParticipationPrompt()!.Proposal.RespondingForceId);
        engine = new(WorldState.Restore(engine.Snapshot), Rules());
        Assert.True(Answer(engine, 3, false).Submission.Accepted);
        var last = Answer(engine, 4, false);
        Assert.True(last.Submission.Accepted);
        Assert.Equal(AllianceFormationFailure.InsufficientMembers, Assert.Single(last.Events.OfType<AllianceFormationFailedEvent>()).Reason);
        Assert.Null(last.Snapshot.Simulation.Alliance);
        Assert.Null(last.Snapshot.Simulation.PendingEvent);
        Assert.Empty(last.Events.OfType<ForceRelationChangedEvent>());
        Assert.All(last.Snapshot.Forces, f => Assert.Equal(50, f.BaseTrust));
        Assert.Equal(SimulationBoundary.Stable, last.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void DecliningDoesNotBlockAutomaticMembersFromFormingAnAlliance()
    {
        var engine = Ready(5, 5);
        engine.AdvanceXun();
        var result = Answer(engine, 5, false);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(new[] { 2, 3, 4 }, result.Snapshot.Simulation.Alliance!.MemberForceIds);
        Assert.Equal(4, result.Snapshot.Simulation.Alliance.LeaderForceId);
        Assert.Equal(80, result.Snapshot.Relations.Single(r => r.FirstId == 1 && r.SecondId == 5).Value);
    }

    [Theory]
    [InlineData(5UL, 1)]
    [InlineData(12UL, 2)]
    public void TwoEligibleTargetPathsUseTheirOwnStableChoiceStream(ulong seed, int expectedTarget)
    {
        var initial = Ready(seed).Snapshot;
        var engine = new SimulationEngine(WorldState.Restore(initial with
        {
            Forces = initial.Forces.Select(f => f.Id == 2 ? f with { BaseTrust = 20 } : f).ToArray(),
            Settlements = initial.Settlements.Select(s => s.Id == 200 ? s with { DraftPopulation = 200001 } : s).ToArray()
        }), Rules());
        var result = engine.AdvanceXun();
        Assert.Equal(expectedTarget, result.Snapshot.Simulation.Alliance!.TargetForceId);
        Assert.Single(result.Snapshot.Simulation.RandomStreams.Where(r => r.Name == "alliance-target"));
    }

    [Fact]
    public void PendingChoiceRejectsOrdinaryCommandsAdvanceWrongControllerAndWrongTargetWithoutMutation()
    {
        var engine = Ready(5, 5);
        engine.AdvanceXun();
        string before = engine.Snapshot.StateHash;
        long sequence = engine.Snapshot.Simulation.NextSequence;
        Assert.Equal(CommandRejection.DecisionPending, engine.Execute(new("controller:5"), new WaitCommand(Guid.NewGuid(), 5, sequence)).Submission.Rejection);
        Assert.Equal(CommandRejection.NotActiveController, engine.Execute(new("controller:4"), new AllianceParticipationCommand(Guid.NewGuid(), 5, sequence, 1, true)).Submission.Rejection);
        Assert.Equal(CommandRejection.InvalidDecision, engine.Execute(new("controller:5"), new AllianceParticipationCommand(Guid.NewGuid(), 5, sequence, 2, true)).Submission.Rejection);
        Assert.Throws<InvalidOperationException>(() => engine.AdvanceXun());
        Assert.Throws<InvalidOperationException>(() => engine.EndControl(new("controller:5")));
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void CorruptDecisionSnapshotsCannotResumeOrSkipTheRemainingQuarter()
    {
        var engine = Ready(5, 2, 3, 4);
        engine.AdvanceXun();
        var valid = engine.Snapshot;
        var proposal = ((AllianceEventSession)valid.Simulation.PendingEvent!).Proposal;
        foreach (var invalid in new AllianceProposal[]
        {
            proposal with { TargetForceId = 999 }, proposal with { AutomaticMemberForceIds = [2] },
            proposal with { PlayerChoices = [] }, proposal with { PlayerChoices = [new(2, null), new(3, true), new(4, null)] },
            proposal with { PlayerChoices = [new(2, true), new(3, false), new(4, false)] },
            proposal with { PlayerChoices = [new(2, null), new(3, null)] }
        }) Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with { Simulation = valid.Simulation with { PendingEvent = new AllianceEventSession(invalid) } }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with { Simulation = valid.Simulation with { PendingEvent = null } }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with { Simulation = valid.Simulation with { Boundary = SimulationBoundary.Stable } }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with { Clock = new(191, 1, 11) }));
        Assert.Throws<ArgumentException>(() => WorldState.Restore(valid with
        {
            Forces = valid.Forces.Select(f => f.Id == proposal.TargetForceId ? f with { IsBarbarian = true } : f).ToImmutableArray()
        }));
    }

    [Fact]
    public void FormationClearsMemberContactsAndAttackIntentsButPreservesExternalAndPersonnelOrders()
    {
        var attackMember = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 201 };
        var attackOutside = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 301 };
        var move = new MilitaryOrder(MilitaryOrderKind.Move, new(9, 0), null);
        var units = new UnitState[]
        {
            new(101, 1, new(0, 0), 100) { FormationId = 0, ServicePlaceId = 100, Order = attackMember, EngagementTargetId = 301 },
            new(102, 1, new(0, 0), 100) { FormationId = 0, Order = attackMember },
            new(103, 1, new(0, 0), 100) { FormationId = 0, Order = move, EngagementTargetId = 201,
                MovementPlan = new(new(9, 0), new(1, 1), TravelMode.Land, [new(1, 0)]) },
            new(104, 1, new(0, 0), 100) { FormationId = 0, Order = attackMember, IsPersonnel = true },
            new(201, 2, new(2, 0), 100) { FormationId = 0, Order = attackOutside, EngagementTargetId = 101 },
            new(301, 3, new(4, 0), 100) { FormationId = 0, Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 101 } }
        };
        var world = WorldState.Create(new(190, 1, 1), 5, [new(1, 0, 0, 0), new(2, 0, 0, 0), new(3, 0, 0, 0)],
            [new(100, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0)], [], units).BeginMutation();
        List<DomainEvent> events = [];
        AllianceFormation.ClearConflicts(world, new HashSet<int> { 1, 2 }, null, events);
        Assert.Equal(new MilitaryOrder(MilitaryOrderKind.Return, null, 100), world.Units[101].Order);
        Assert.Null(world.Units[101].EngagementTargetId);
        Assert.Null(world.Units[102].Order);
        Assert.Equal(move, world.Units[103].Order);
        Assert.Null(world.Units[103].EngagementTargetId);
        Assert.Null(world.Units[103].MovementPlan);
        Assert.Equal(units[3], world.Units[104]);
        Assert.Equal(attackOutside, world.Units[201].Order);
        Assert.Null(world.Units[201].EngagementTargetId);
        Assert.Equal(units[5], world.Units[301]);
        Assert.Equal(2, events.OfType<UnitOrderChangedEvent>().Count());
        Assert.Equal(3, events.OfType<UnitEngagementChangedEvent>().Count());
    }

    [Fact]
    public void SparseRelationsCanBeEstablishedSymmetricallyWithoutInventingZeroChangeEvents()
    {
        var world = WorldState.Create(new(190, 1, 1), 1, [new(1, 0, 0, 0), new(2, 0, 0, 0)], [], []).BeginMutation();
        List<DomainEvent> events = [];
        ForceRelations.Set(world, 2, 1, 0, events);
        Assert.Empty(events);
        Assert.Empty(world.Relations);
        ForceRelations.Set(world, 2, 1, 60, events);
        Assert.Equal(60, ForceRelations.Get(world, 1, 2).Value);
        Assert.Equal((1, 2), Assert.Single(world.Relations).Key);
        Assert.Equal(0, Assert.Single(events.OfType<ForceRelationChangedEvent>()).Previous);
    }
}
