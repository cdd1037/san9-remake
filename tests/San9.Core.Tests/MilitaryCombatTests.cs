using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryCombatTests
{
    [Fact]
    public void PublicAttackRequiresConfirmationForAllianceMember()
    {
        var initial = Engine(World(controlled: true)).CaptureStableState();
        initial = initial with { Forces = initial.Forces.Append(new ForceState(3, 0, 0, 0)).ToArray() };
        var allied = Engine(WorldState.Restore(initial with
        {
            Simulation = initial.Simulation with { Alliance = new(3, 1, [1, 2], 36) }
        }));
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, new(MilitaryOrderKind.AttackSettlement, null, 2));
        string before = allied.Snapshot.StateHash;
        Assert.Equal(CommandRejection.AttackConfirmationRequired, allied.Execute(new("player"), command).Submission.Rejection);
        Assert.Equal(before, allied.Snapshot.StateHash);
        Assert.True(allied.Execute(new("player"), command with { ConfirmAttack = true }).Submission.Accepted);
        var expired = Engine(WorldState.Restore(initial));
        Assert.True(expired.Execute(new("player"), command).Submission.Accepted);
    }

    [Fact]
    public void PartialMemberRanksAreRejectedEvenForMovementOnlyFormations()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)],
            [new(1, 1, 0, 10000, 5, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Front] }], []);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition));
    }

    private static readonly CombatProfile Profile = new(10, 10, 10, 10, 50);
    private static MilitaryRulebook Rules(int range = 1, bool destroyed = false) => new(new(8, 3,
        Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
        [new(7, 10, 0, 100000, 5, new(1, 1), TravelMode.Land) { Combat = Profile, Attack = new(range, true), CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] }],
        [new(1, new(2, 7), new(1, 1)) { Combat = Profile, Attack = new(1, false) },
         new(2, new(37, 7), new(1, 1)) { Combat = Profile, Attack = new(1, false), DestroyedAtZeroDurability = destroyed },
         new(3, new(37, 2), new(1, 1)) { Combat = Profile, Attack = new(1, false) }]));

    private static WorldState World(uint defenders = 100, int durability = 100, bool controlled = false) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 10000, 100) { LeaderId = 11 }, new(2, 10000, 10000, 100) { LeaderId = 22 }],
        [new(1, 1, 100, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 2, defenders, durability, 100, 70, 500, 0, 100, 0, 100)],
        [new(11, 1, null, new(90, 90, 90, 90), false) { UnitId = 0, Status = OfficerStatus.Ruler },
         new(12, 1, 1, new(70, 70, 70, 70), false), new(22, 2, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }],
        units: [new(0, 1, new(35, 7), 10000) { FormationId = 7, Morale = 90, ServicePlaceId = 1,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front),
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }],
        controls: controlled ? [new("player", 1, ControllerKind.Player)] : []);

    private static SimulationEngine Engine(WorldState world, MilitaryRulebook? rules = null) =>
        new(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules ?? Rules());

    [Fact]
    public void PlannedAttackRechecksOwnershipAndDuplicateSubmissionCannotRepeatCapture()
    {
        var world = World(controlled: true);
        var engine = Engine(world);
        var player = new CommandContext("player");
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.AttackSettlement, null, 2));
        Assert.True(engine.Validate(player, command).Accepted);
        var mutation = world.BeginMutation();
        mutation.AddSettlement(mutation.Settlements[2] with { Id = 3 });
        mutation.Replace(mutation.Officers[22] with { SettlementId = 3 });
        mutation.Replace(mutation.Settlements[2] with { OwnerId = 1 });
        world.Commit(mutation);
        var before = engine.Snapshot.StateHash;
        var rejected = engine.Execute(player, command);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, engine.Snapshot.StateHash);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { OwnerId = 2 });
        world.Commit(mutation);
        Assert.True(engine.Execute(player, command).Submission.Accepted);
        before = engine.Snapshot.StateHash;
        rejected = engine.Execute(player, command);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        Assert.Equal(before, engine.Snapshot.StateHash);
        engine.EndControl(player);
        Assert.Single(engine.AdvanceXun().Events.OfType<SettlementCapturedEvent>());
        engine.EndControl(player);
        Assert.Empty(engine.AdvanceXun().Events.OfType<SettlementCapturedEvent>());
    }

    [Theory]
    [InlineData(UnitCondition.Normal)]
    [InlineData(UnitCondition.HighSpirits)]
    public void NormalAndHighSpiritsAttackersBothDamageWalls(UnitCondition condition)
    {
        var state = Engine(World(100000)).CaptureStableState();
        var engine = Engine(WorldState.Restore(state with
        {
            Units = state.Units.Select(u => u with { Condition = condition,
                ConditionDaysRemaining = condition == UnitCondition.Normal ? 0 : 10 }).ToArray()
        }));
        var first = engine.AdvanceXun().Events.OfType<BattleDamageEvent>().First(e => e.AttackerId == 0 && !e.AttackerIsSettlement);
        Assert.True(first.DurabilityLoss > 0);
        Assert.Equal(2, first.SettlementId);
    }

    [Fact]
    public void PublicAttackApproachesCapturesEliminatesAndTerminalRestoreIsStable()
    {
        var engine = Engine(World());
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        var result = engine.AdvanceXun();
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Single(result.Events.OfType<ForceEliminatedEvent>());
        Assert.Single(result.Events.OfType<SimulationEndedEvent>());
        Assert.Equal(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        var prisoner = engine.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal(OfficerStatus.Captive, prisoner.Status);
        Assert.Null(prisoner.OwnerId);
        Assert.Equal(0, prisoner.UnitId);
        Assert.Empty(engine.AdvanceXun().Events);
        Assert.Equal(engine.Snapshot.StateHash, Engine(WorldState.Restore(engine.CaptureStableState())).Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ZeroWallsRebuildOrDestroyAccordingToDefinition(bool destroyed)
    {
        var engine = Engine(World(100000, 1), Rules(destroyed: destroyed));
        var result = engine.AdvanceXun();
        Assert.Equal(destroyed, Assert.Single(result.Events.OfType<SettlementCapturedEvent>()).Destroyed);
        var settlement = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.Equal(destroyed ? 0 : 25, settlement.Durability);
        Assert.Equal(destroyed, settlement.IsDestroyed);
        Assert.Equal(destroyed ? null : 1, settlement.OwnerId);
    }

    [Fact]
    public void CompetingAttacksCannotCaptureTheSameSettlementTwice()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.AddUnit(new(1, 1, new(36, 6), 10000) { FormationId = 7, Morale = 90,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 2) });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        Assert.Single(engine.AdvanceXun().Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(new(MilitaryOrderKind.Return, null, 2), engine.Snapshot.Units.Single(u => u.Id == 1).Order);
    }

    [Fact]
    public void MissingCombatDefinitionAndFriendlyTargetsAreRejectedWithoutEffects()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Equal(CommandRejection.InvalidTask, MilitaryMovement.Order(mutation,
            new(Guid.NewGuid(), 1, 0, 0, new(MilitaryOrderKind.AttackSettlement, null, 1)), Rules(), events));
        Assert.Empty(events);
        Assert.Equal(2, mutation.Units[0].Order!.SettlementId);
    }

    [Fact]
    public void CounterattackConsumesDailyOpportunityAndUsesPreBattleStrength()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 } });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        var events = engine.AdvanceXun().Events.OfType<BattleDamageEvent>().ToArray();
        Assert.NotEmpty(events);
        Assert.All(events.GroupBy(e => (e.Clock, e.TargetUnitId)), day => Assert.Single(day));
        Assert.All(events, e => Assert.Equal(e.SoldierLoss, e.AddedWounded + e.Dead));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void DefeatedLeaderAndLostHomeLeaveNoDanglingUnitsOrForceReferences(bool loseHome)
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Soldiers = 1, Morale = 100,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90 });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        if (loseHome)
        {
            mutation.Replace(mutation.Settlements[1] with { OwnerId = 2 });
            mutation.Replace(mutation.Officers[12] with { OwnerId = 2 });
        }
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Order?.UnitId == 0);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.UnitId == 0);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
        if (loseHome)
        {
            Assert.True(engine.Snapshot.Forces.Single(f => f.Id == 1).IsEliminated);
            Assert.Equal(OfficerStatus.Captive, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        }
        else Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 1).IsEliminated);
    }

    [Fact]
    public void MutualDestructionSettlesBothUnitsAndMembersOnce()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(20, 7), Soldiers = 1, Morale = 90,
            Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } });
        mutation.AddUnit(new(1, 2, new(21, 7), 1) { FormationId = 7, Morale = 90, ServicePlaceId = 2,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(22, BattleRank.Rear) });
        mutation.Replace(mutation.Officers[22] with { UnitId = 1, SettlementId = null });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 2 });
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Empty(engine.Snapshot.Units);
        Assert.Equal(2, result.Events.OfType<UnitTerminatedEvent>().Count());
        Assert.All(engine.Snapshot.Officers, p => Assert.Null(p.UnitId));
        Assert.All(engine.Snapshot.Forces, f => Assert.False(f.IsEliminated));
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Fact]
    public void UnrelatedAssetlessForceIsNotEliminatedByAnotherBattle()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Append(new ForceState(3, 100, 100, 0)),
            baseline.Settlements, baseline.Officers, baseline.Units);
        var engine = Engine(world);
        engine.AdvanceXun();
        Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 3).IsEliminated);
        Assert.NotEqual(SimulationBoundary.Terminal, engine.Snapshot.Simulation.Boundary);
    }

    [Fact]
    public void CapturedCorpsDissolvesWithoutRemovingSurvivingForceOrTreasuryAnchor()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42,
            baseline.Forces.Select(f => f.Id == 2 ? f with { LeaderId = 23 } : f),
            baseline.Settlements.Select(s => s with { CorpsId = s.Id == 1 ? 10 : 21 })
                .Append(new SettlementState(3, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100) { CorpsId = 20 }),
            baseline.Officers.Select(p => p.Id == 22 ? p with { Status = OfficerStatus.Governor } : p)
                .Append(new OfficerState(23, 2, 3, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler }),
            baseline.Units.Select(u => u with { CorpsId = 10 }),
            [new(10, 1, 11, null), new(20, 2, 23, null), new(21, 2, 22, new(1234, 2345))]);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Contains(result.Events, e => e is CorpsDissolvedEvent { CorpsId: 21 });
        Assert.DoesNotContain(engine.Snapshot.Corps, c => c.Id == 21);
        Assert.Contains(engine.Snapshot.Corps, c => c.Id == 20 && c.LeaderId == 23);
        Assert.All(engine.Snapshot.Forces, f => Assert.False(f.IsEliminated));
        Assert.Equal(10, engine.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Fact]
    public void CapturePreservesUndiscoveredAndFuturePeople()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces, baseline.Settlements,
            baseline.Officers.Concat(new[] {
                new OfficerState(30, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Undiscovered },
                new OfficerState(31, null, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.NotAppeared }
            }), baseline.Units);
        var engine = Engine(world);
        engine.AdvanceXun();
        Assert.Equal(OfficerStatus.Undiscovered, engine.Snapshot.Officers.Single(p => p.Id == 30).Status);
        Assert.Equal(OfficerStatus.NotAppeared, engine.Snapshot.Officers.Single(p => p.Id == 31).Status);
    }

    [Fact]
    public void ActualHeldEscapeMountProtectsDefeatedFieldMemberWithReachableHome()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces, baseline.Settlements, baseline.Officers,
            baseline.Units.Select(u => u with { Soldiers = 1, Position = new(20, 7),
                Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } })
                .Append(new UnitState(1, 2, new(21, 7), 10000) { FormationId = 7, Morale = 90 }),
            treasures: [new(7, 11, TreasureDiscovery.Discovered, null)]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty,
            new TreasureRulebook([new(7, default, 0) { EscapeMount = true }]), military: Rules());
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events, e => e is OfficerEscapedBattleEvent { OfficerId: 11, SettlementId: 1 });
        Assert.DoesNotContain(result.Events, e => e is OfficerCapturedEvent { OfficerId: 11 });
        Assert.Equal(11, Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
    }

    [Fact]
    public void TravelingOfficerIsRepatriatedInsteadOfCapturedWithAdministrativeHome()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces,
            baseline.Settlements.Append(new SettlementState(3, 2, 100, 100, 100, 70, 500, 0, 100, 0, 100)),
            baseline.Officers, baseline.Units);
        var mutation = world.BeginMutation();
        Guid taskId = Guid.NewGuid();
        mutation.SetSimulation(mutation.Simulation with { NextSequence = 1,
            Tasks = [new(taskId, "personnel-search", "system", 2, 22, 2, new(190, 1, 1), new(190, 1, 10), 0, 0)
            { Journey = new(3, null, 99) { AcceptRecruitment = false } }] });
        world.Commit(mutation);
        var geography = new PersonnelRulebook([new(1, new(2, 7)), new(2, new(37, 7)), new(3, new(37, 2))]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, geography, TreasureRulebook.Empty, military: Rules());
        var result = engine.AdvanceXun();
        Assert.Contains(result.Events, e => e is TaskCancelledEvent cancelled && cancelled.TaskId == taskId);
        Assert.DoesNotContain(result.Events, e => e is OfficerCapturedEvent { OfficerId: 22 });
        var ruler = engine.Snapshot.Officers.Single(p => p.Id == 22);
        Assert.Equal(OfficerStatus.Ruler, ruler.Status);
        Assert.Equal(3, ruler.SettlementId);
        Assert.False(engine.Snapshot.Forces.Single(f => f.Id == 2).IsEliminated);
    }

    [Fact]
    public void OrdinaryLossDoesNotCauseProportionalMoraleCapture()
    {
        var world = World(100000);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { Morale = 1 });
        world.Commit(mutation);
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Empty(result.Events.OfType<SettlementCapturedEvent>());
        Assert.All(result.Events.OfType<BattleDamageEvent>(), e => Assert.Equal(0, e.MoraleLoss));
        var captured = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.True(captured.Soldiers > 0);
        Assert.True(captured.Durability > 0);
    }

    [Fact]
    public void BarbarianEliminationKillsNonPrisonersAndClearsTheirTreasureHoldings()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Select(f => f.Id == 2 ? f with { IsBarbarian = true } : f),
            baseline.Settlements, baseline.Officers.Append(new OfficerState(30, 2, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Special }),
            baseline.Units, treasures: [new(7, 30, TreasureDiscovery.Discovered, null)]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, new TreasureRulebook([new(7, default, 0)]), military: Rules());
        engine.AdvanceXun();
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 30).Status);
        Assert.Null(Assert.Single(engine.Snapshot.Treasures).HolderId);
        Assert.Equal(OfficerStatus.Dead, engine.Snapshot.Officers.Single(p => p.Id == 22).Status);
        Assert.Null(engine.Snapshot.Officers.Single(p => p.Id == 22).OwnerId);
    }

    [Fact]
    public void RivalClaimantDoesNotAttackNewOwnerInTheSameDailyBatch()
    {
        var baseline = World().CreateSnapshot();
        var world = WorldState.Create(baseline.Clock, 42, baseline.Forces.Append(new ForceState(3, 10000, 10000, 100) { LeaderId = 33 }),
            baseline.Settlements.Append(new SettlementState(3, 3, 100, 100, 100, 70, 500, 0, 100, 0, 100)),
            baseline.Officers.Append(new OfficerState(33, 3, null, new(90, 90, 90, 90), false) { UnitId = 1, Status = OfficerStatus.Ruler }),
            baseline.Units.Append(new UnitState(1, 3, new(36, 6), 10000) { FormationId = 7, Morale = 90,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(33, BattleRank.Front),
                Order = new(MilitaryOrderKind.AttackSettlement, null, 2) }));
        var engine = Engine(world);
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<SettlementCapturedEvent>());
        Assert.Equal(1, engine.Snapshot.Settlements.Single(s => s.Id == 2).OwnerId);
        var captureDay = Assert.Single(result.Events.OfType<SettlementCapturedEvent>()).Clock;
        Assert.DoesNotContain(result.Events.OfType<BattleDamageEvent>(), e => e.Clock == captureDay && e.AttackerId == 1 && !e.AttackerIsSettlement);
        Assert.Null(engine.Snapshot.Units.Single(u => u.Id == 1).Order);
    }
}
