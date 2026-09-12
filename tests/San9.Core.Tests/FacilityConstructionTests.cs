using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityConstructionTests
{
    private static readonly CommandContext Player = new("player");
    private static readonly PersonnelRulebook Geography = new([new(1, new(2, 2)), new(8000, new(17, 2))],
        new Dictionary<int, int> { [1] = 1, [8000] = 1 });
    private static readonly MilitaryRulebook Military = CreateMilitary();
    private static MilitaryRulebook CreateMilitary(bool hostsPeople = true, bool needsTechnology = false, bool attacksWalls = false) => new(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
        [new(1, 5, 50, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 1000, 10, 50), Attack = new(1, attacksWalls), CaptureRanks = [BattleRank.Front] }],
        [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 }])
    {
        Facilities = [new(0, 0, 600, new(1, 1), 15, false, true, true, 500000, null, null, null, []),
            new(9000, 1000, 600, new(3, 3), 15, true, hostsPeople, hostsPeople, 250000,
                needsTechnology ? 20 : null, needsTechnology ? 19 : null, null, [])
                { Combat = new(10, 10, 0, 10, 50), DestroyedAtZeroDurability = true }],
        Tactics = [new(19, 6, TacticEffect.Construction, 0, 0, 0, false, false, []),
            new(20, 6, TacticEffect.StoneFormation, 0, 0, 0, false, false, [])],
        ConstructionSites = [new(8000, new(17, 2), [9000])]
    });
    private static SimulationEngine Engine(WorldState? world = null, MilitaryRulebook? military = null) => new(world ?? WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 1000000, 100) { LeaderId = 11 }], [new(1, 1, 20000, 600, 600, 100, 0, 0, 100, 0, 100)],
        [new(11, 1, 1, new(100, 100, 100, 100), false) { Status = OfficerStatus.Ruler }],
        controls: [new("player", 1, ControllerKind.Player)]), EconomyRulebook.Empty, Geography, TreasureRulebook.Empty, military: military ?? Military);
    private static ConstructFacilityCommand Command(long sequence = 0) => new(Guid.NewGuid(), 1, sequence, 1, [11], 10000, 1, 8000, 9000, new(17, 2));
    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        engine.EndControl(Player);
        return engine.AdvanceXun();
    }

    [Fact]
    public void ConstructionMovesStartsAndCompletesOnceAndRestorePreservesUnfinishedWork()
    {
        var engine = Engine();
        var command = Command();
        var submitted = engine.Execute(Player, command);
        Assert.True(submitted.Submission.Accepted);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
        Assert.DoesNotContain(engine.Snapshot.Settlements, s => s.Id == 8000);
        Assert.False(engine.Execute(Player, command).Submission.Accepted);
        var first = Advance(engine);
        Assert.Single(first.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "started");
        var unfinished = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.NotNull(unfinished.Facility!.BuilderUnitId);
        Assert.False(unfinished.CanHostPersons);
        Assert.InRange(unfinished.Durability, 1, 599);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()));
        int completed = 0;
        for (int i = 0; i < 3; i++)
        {
            var actual = Advance(engine);
            Assert.Equal(CanonicalJson.Text(actual.Events), CanonicalJson.Text(Advance(restored).Events));
            completed += actual.Events.OfType<FacilityConstructionChangedEvent>().Count(e => e.Phase == "completed");
        }
        Assert.Equal(1, completed);
        Assert.Empty(engine.Snapshot.Units);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(600, facility.Durability);
        Assert.Null(facility.Facility!.BuilderUnitId);
        Assert.True(facility.CanHostPersons);
        Assert.Equal(10000u, facility.Soldiers);
        Assert.Equal(8000, engine.Snapshot.Officers.Single().SettlementId);
        Assert.Equal(new WorldPosition(17, 2), Military.Place(facility).Position);
    }

    [Fact]
    public void NewOrdersCancelUnfinishedFacilityWithoutRefundAndAllowReconstruction()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var result = engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0,
            new(MilitaryOrderKind.Return, null, 1)));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
        Advance(engine);
        Assert.Empty(engine.Snapshot.Units);
        Assert.True(engine.Execute(Player, Command(engine.Snapshot.Simulation.NextSequence)).Submission.Accepted);
        Advance(engine);
        Assert.False(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(8000u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData(UnitCondition.HighSpirits, true)]
    [InlineData(UnitCondition.Confused, false)]
    public void HighSpiritsAllowsWorkButConfusionPausesIt(UnitCondition condition, bool progresses)
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        int before = saved.Settlements.Single(s => s.Id == 8000).Durability;
        var changed = Engine(WorldState.Restore(saved with { Units = saved.Units.Select(u => u with
            { Condition = condition, ConditionDaysRemaining = 100 }).ToImmutableArray() }));
        Advance(changed);
        Assert.Equal(progresses, changed.Snapshot.Settlements.Single(s => s.Id == 8000).Durability > before);
    }

    [Fact]
    public void DemolishingUnfinishedFacilityClearsItsBuilderOrder()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        Assert.True(engine.Execute(Player, new DemolishFacilityCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, 8000)).Submission.Accepted);
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.Null(engine.Snapshot.Settlements.Single(s => s.Id == 8000).Facility!.BuilderUnitId);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.DoesNotContain(Advance(engine).Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
    }

    [Fact]
    public void LossOfConstructionUnitToStarvationRemovesItsUnfinishedFacility()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        var saved = engine.CaptureStableState();
        var starving = Engine(WorldState.Restore(saved with { Forces = saved.Forces.Select(f => f with { Food = 0 }).ToImmutableArray() }));
        var result = Advance(starving);
        Assert.Empty(starving.Snapshot.Units);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.True(starving.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
    }

    [Fact]
    public void DomesticRepairCannotCompleteUnfinishedWorkButRepairsTheFinishedFacilityNormally()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        string before = engine.Snapshot.StateHash;
        var repair = new DevelopCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
            new(1, 8000, [11], DomesticActivity.Repair));
        var rejected = engine.Execute(Player, repair);
        Assert.False(rejected.Submission.Accepted);
        Assert.Equal(DevelopmentRejection.UnsupportedActivity, rejected.Development!.Value.Rejection);
        Assert.Equal(before, engine.Snapshot.StateHash);
        for (int i = 0; i < 3; i++) Advance(engine);
        var saved = engine.CaptureStableState();
        var damaged = Engine(WorldState.Restore(saved with
        {
            Settlements = saved.Settlements.Select(s => s.Id == 8000 ? s with { Durability = 599 } : s).ToImmutableArray()
        }));
        var result = damaged.Execute(Player, repair with { CommandId = Guid.NewGuid(), Sequence = damaged.Snapshot.Simulation.NextSequence });
        Assert.True(result.Submission.Accepted);
        Assert.Equal(600, damaged.Snapshot.Settlements.Single(s => s.Id == 8000).Durability);
        Assert.DoesNotContain(result.Events, e => e is FacilityConstructionChangedEvent or UnitAbsorbedEvent);
    }

    [Fact]
    public void NonHostingStructureReturnsItsWorkforceToTheServiceCity()
    {
        var engine = Engine(military: CreateMilitary(hostsPeople: false));
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 4; i++) events.AddRange(Advance(engine).Events);
        Assert.Single(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.DoesNotContain(events.OfType<UnitAbsorbedEvent>(), e => e.SettlementId == 8000);
        Assert.Single(events.OfType<UnitAbsorbedEvent>(), e => e.SettlementId == 1);
        Assert.Equal(20000u, engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(600, facility.Durability);
        Assert.False(facility.CanHostPersons);
        Assert.Equal(0u, facility.Soldiers);
        Assert.Empty(engine.Snapshot.Units);
    }

    [Fact]
    public void TechnologyIsRequiredDiscountUsesParticipantsAndLossCancelsUnfinishedWork()
    {
        var rules = CreateMilitary(needsTechnology: true);
        var engine = Engine(military: rules);
        string hash = engine.Snapshot.StateHash;
        Assert.Equal(CommandRejection.TacticUnavailable, engine.Execute(Player, Command()).Submission.Rejection);
        Assert.Equal(hash, engine.Snapshot.StateHash);
        var initial = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(initial with { Forces = initial.Forces.Select(f => f with { ProvidedTactics = [19, 20] }).ToImmutableArray() }), rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Assert.Equal(9334u, engine.Snapshot.Forces.Single().Money);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with { Forces = saved.Forces.Select(f => f with { ProvidedTactics = [19] }).ToImmutableArray() }), rules);
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Equal(9334u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData("position")]
    [InlineData("type")]
    [InlineData("region")]
    [InlineData("members")]
    [InlineData("soldiers")]
    [InlineData("funds")]
    [InlineData("acted")]
    public void InvalidConstructionLeavesResourcesPeopleAndSequenceUnchanged(string invalid)
    {
        var engine = Engine();
        var saved = engine.CaptureStableState();
        if (invalid == "funds")
            engine = Engine(WorldState.Restore(saved with { Forces = saved.Forces.Select(f => f with { Money = 999 }).ToImmutableArray() }));
        if (invalid == "acted")
            engine = Engine(WorldState.Restore(saved with { Officers = saved.Officers.Select(p => p with { Acted = true }).ToImmutableArray() }));
        var command = invalid switch
        {
            "position" => Command() with { Position = new(16, 2) },
            "type" => Command() with { FacilityTypeId = 0 },
            "region" => Command() with { RegionId = 1 },
            "members" => Command() with { OfficerIds = [11, 11] },
            "soldiers" => Command() with { Soldiers = 20001 },
            _ => Command()
        };
        string before = engine.Snapshot.StateHash;
        var result = engine.Execute(Player, command);
        Assert.False(result.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.DoesNotContain(result.Events, e => e is UnitDeployedEvent or FacilityConstructionChangedEvent or ResourceChangedEvent);
    }

    [Fact]
    public void LossOfParentCityCancelsWorkWithoutCompletionOrRefund()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Settlements = saved.Settlements.Select(s => s.Id == 1 ? s with { Durability = 0 } : s).ToImmutableArray()
        }));
        var result = Advance(engine);
        Assert.Single(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.DoesNotContain(result.Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.True(engine.Snapshot.Settlements.Single(s => s.Id == 8000).IsDestroyed);
        Assert.Null(engine.Snapshot.Units.Single().Order);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single().Money);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnfinishedFacilityRejectsAttackAndStaleOrdersWithoutTakingDamage(bool attacksWalls)
    {
        var rules = CreateMilitary(attacksWalls: attacksWalls);
        var engine = Engine(military: rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = saved.Units.Select(u => u with { Condition = UnitCondition.Confused, ConditionDaysRemaining = 100 }).ToImmutableArray()
                .Add(new(1, 2, new(19, 2), 10000) { FormationId = 1, Morale = 100,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                    Order = new(MilitaryOrderKind.AttackSettlement, null, 8000) }),
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }), rules);
        var events = Advance(engine).Events;
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.False(facility.IsDestroyed);
        Assert.Equal(saved.Settlements.Single(s => s.Id == 8000).Durability, facility.Durability);
        Assert.DoesNotContain(events.OfType<SettlementCapturedEvent>(), e => e.SettlementId == 8000);
        Assert.DoesNotContain(events.OfType<BattleDamageEvent>(), e => e.SettlementId == 8000);
        Assert.NotNull(facility.Facility!.BuilderUnitId);
        Assert.Null(engine.Snapshot.Units.Single(u => u.Id == 1).Order);
        var controlled = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(controlled with
        {
            Simulation = controlled.Simulation with { Controls = [new("player", 2, ControllerKind.Player)] }
        }), rules);
        string before = engine.Snapshot.StateHash;
        var rejected = engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 2,
            engine.Snapshot.Simulation.NextSequence, 1, new(MilitaryOrderKind.AttackSettlement, null, 8000)));
        Assert.False(rejected.Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CompletedWorkOnlyDisappearsAtZeroDurabilityWithoutCaptureBenefits(bool attacksWalls)
    {
        var rules = CreateMilitary(hostsPeople: false, attacksWalls: attacksWalls);
        var engine = Engine(military: rules);
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        for (int i = 0; i < 4; i++) Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = [new(1, 2, new(19, 2), 10000) { FormationId = 1, Morale = 50,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                Order = new(MilitaryOrderKind.AttackSettlement, null, 8000) }],
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }), rules);
        var events = new List<DomainEvent>();
        for (int i = 0; i < 3; i++) events.AddRange(Advance(engine).Events);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.Equal(attacksWalls, facility.IsDestroyed);
        Assert.Equal(attacksWalls ? 0 : 600, facility.Durability);
        Assert.Equal(attacksWalls ? null : (int?)1, facility.OwnerId);
        Assert.Equal(attacksWalls ? 1 : 0, events.OfType<FacilityDestroyedEvent>().Count());
        Assert.Empty(events.OfType<SettlementCapturedEvent>());
        Assert.DoesNotContain(events.OfType<ResourceChangedEvent>(), e => e.Reason == "settlement-captured");
        Assert.Equal(50, engine.Snapshot.Units.Single().Morale);
        Assert.Equal(10000u, engine.Snapshot.Forces.Single(f => f.Id == 2).Money);
        Assert.Equal(9000u, engine.Snapshot.Forces.Single(f => f.Id == 1).Money);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.Equal(CanonicalJson.Text(Advance(engine).Events), CanonicalJson.Text(Advance(restored).Events));
    }

    [Fact]
    public void DefeatingBuilderRemovesItsSiteOnce()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, Command()).Submission.Accepted);
        Advance(engine);
        var saved = engine.CaptureStableState();
        engine = Engine(WorldState.Restore(saved with
        {
            Forces = saved.Forces.ToImmutableArray().Add(new(2, 10000, 1000000, 100) { LeaderId = 21 }),
            Officers = saved.Officers.ToImmutableArray().Add(new(21, 2, null, new(100, 100, 100, 100), false)
                { UnitId = 1, Status = OfficerStatus.Ruler }),
            Units = saved.Units.Select(u => u with { Soldiers = 1, Condition = UnitCondition.Confused, ConditionDaysRemaining = 100 }).ToImmutableArray()
                .Add(new(1, 2, new(12, 2), 10000) { FormationId = 1, Morale = 100,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Front),
                    Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 } }),
            Simulation = saved.Simulation with { NextUnitId = 2 }
        }));
        var events = Advance(engine).Events;
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 0);
        Assert.Single(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
        Assert.DoesNotContain(events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "completed");
        Assert.Empty(events.OfType<SettlementCapturedEvent>());
        var site = engine.Snapshot.Settlements.Single(s => s.Id == 8000);
        Assert.True(site.IsDestroyed);
        Assert.Null(site.Facility!.BuilderUnitId);
        Assert.DoesNotContain(Advance(engine).Events.OfType<FacilityConstructionChangedEvent>(), e => e.Phase == "cancelled");
    }
}
