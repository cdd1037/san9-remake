using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityReprisalTests
{
    private static readonly CommandContext Player = new("player");
    private static readonly FacilityReprisal Reprisal = new(6, 1000, 10, 10, 10, 5);

    private static (SimulationEngine Engine, MilitaryRulebook Rules) Create(int proficiency = 0, int intelligence = 100,
        uint soldiers = 10000, uint wounded = 0, int durability = 600, bool walls = true, bool enabled = true,
        FacilityReprisal? reprisal = null, ulong seed = 42)
    {
        var combat = new CombatProfile(0, 0, 1, 1000, 50);
        var military = new MilitaryRulebook(new(4, 1, [0, 0, 0, 0], [new(0, 1, null)],
            [new(700, 5, 0, 100000, 2, new(1, 1), TravelMode.Land)
                { Combat = combat, Attack = new(1, walls), CaptureRanks = [BattleRank.Front, BattleRank.Rear] }],
            [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 8001, Combat = combat },
             new(2, new(7, 2), new(1, 1)) { FacilityTypeId = 8000, Combat = combat, DestroyedAtZeroDurability = true },
             new(3, new(17, 2), new(1, 1)) { FacilityTypeId = 8001, Combat = combat }])
        {
            Facilities = [new(8000, 0, 600, new(1, 1), 0, false, false, false, 100000, null, null, null, [])
                { Combat = combat, DestroyedAtZeroDurability = true, Reprisal = enabled ? reprisal ?? Reprisal : null },
                new(8001, 0, 600, new(1, 1), 0, false, true, true, 100000, null, null, null, []) { Combat = combat }]
        });
        var world = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 10000, 100000, 100) { LeaderId = 11 }, new(2, 10000, 100000, 100) { LeaderId = 21 }],
            [new(1, 1, 1000, 600, 600, 100, 0, 0, 100, 0, 100),
             new(2, 2, 0, durability, 600, 0, 0, 0, 100, 0, 100) { CanHostPersons = false, ConsumesFood = false },
             new(3, 2, 1000, 600, 600, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, null, new(100, 0, 0, 0), false) { UnitId = 0, Status = OfficerStatus.Ruler },
             new(12, 1, null, new(10, intelligence, 0, 0), false) { UnitId = 0,
                TacticProficiencies = [0, 0, 0, 0, 0, 0, proficiency, 0, 0] },
             new(21, 2, 3, new(10, 10, 10, 10), false) { Status = OfficerStatus.Ruler }],
            units: [new(0, 1, new(6, 2), soldiers) { FormationId = 700, Morale = 100, Wounded = wounded, ServicePlaceId = 1,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front).Add(12, BattleRank.Rear) }],
            controls: [new("player", 1, ControllerKind.Player)], computerDamageBonusPercent: 100);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: military);
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0,
            new(MilitaryOrderKind.AttackSettlement, null, 2))).Submission.Accepted);
        return (engine, military);
    }

    private static AdvanceXunResult Advance(SimulationEngine engine) { engine.EndControl(Player); return engine.AdvanceXun(); }

    [Theory]
    [InlineData(0, 100u)]
    [InlineData(1, 99u)]
    [InlineData(999, 1u)]
    [InlineData(1000, 1u)]
    public void SurvivingWorkUsesHighestMemberProficiencyWithoutCombatBonusOrMoraleLoss(int proficiency, uint damage)
    {
        var (engine, _) = Create(proficiency);
        var events = Advance(engine).Events.OfType<BattleDamageEvent>().Where(e => e.AttackerIsSettlement && e.SoldierLoss > 0).ToArray();
        Assert.NotEmpty(events);
        Assert.All(events, e =>
        {
            Assert.Equal(damage, e.SoldierLoss);
            Assert.Equal(damage, e.AddedWounded + e.Dead);
            Assert.Equal(0, e.MoraleLoss);
        });
        Assert.Equal(100, engine.Snapshot.Units.Single().Morale);
        Assert.DoesNotContain(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "military-facility-reprisal");
    }

    [Theory]
    [InlineData(false, true, 600)]
    [InlineData(true, false, 600)]
    [InlineData(true, true, 1)]
    public void ReprisalRequiresItsCapabilityAndNonlethalSiegeDamage(bool enabled, bool walls, int durability)
    {
        var (engine, _) = Create(durability: durability, walls: walls, enabled: enabled);
        var result = Advance(engine);
        Assert.DoesNotContain(result.Events.OfType<BattleDamageEvent>(), e => e.AttackerIsSettlement && e.SoldierLoss > 0);
        Assert.Equal(11000L, engine.Snapshot.Units.Where(u => u.OwnerId == 1).Sum(u => (long)u.Soldiers) +
            engine.Snapshot.Settlements.Where(s => s.OwnerId == 1).Sum(s => (long)s.Soldiers));
        Assert.Equal(durability == 1, engine.Snapshot.Settlements.Single(s => s.Id == 2).IsDestroyed);
    }

    [Fact]
    public void ReprisalDefeatUsesTerminationWithoutPrisonersInTheWork()
    {
        var (engine, _) = Create(soldiers: 50, wounded: 50000, intelligence: 0);
        var result = Advance(engine);
        var loss = Assert.Single(result.Events.OfType<BattleDamageEvent>(), e => e.AttackerIsSettlement && e.SoldierLoss > 0);
        Assert.Equal(50u, loss.SoldierLoss);
        Assert.Equal(0u, loss.AddedWounded);
        Assert.Equal(50u, loss.Dead);
        Assert.Single(result.Events.OfType<UnitTerminatedEvent>());
        Assert.Empty(engine.Snapshot.Units);
        Assert.DoesNotContain(engine.Snapshot.Officers, p => p.UnitId == 0 || p.SettlementId == 2);
        Assert.DoesNotContain(result.Events.OfType<BattleConditionChangedEvent>(), e => e.ObjectId == 0);
        Assert.NotNull(WorldState.Restore(engine.CaptureStableState()));
    }

    [Fact]
    public void ConfusionPausesAttackExpiresAndContinuesIdenticallyAfterRestore()
    {
        var (engine, rules) = Create(intelligence: 0, reprisal: Reprisal with { ConfusionChance = 100 });
        var first = Advance(engine);
        var confused = first.Events.OfType<BattleConditionChangedEvent>().Where(e => e.Condition == UnitCondition.Confused).ToArray();
        Assert.Equal(2, confused.Length);
        Assert.Equal(5, confused[1].Clock.DayNumber - confused[0].Clock.DayNumber);
        Assert.Contains(first.Events.OfType<BattleConditionChangedEvent>(), e => e.Condition == UnitCondition.Normal);
        Assert.NotNull(engine.Snapshot.Units.Single().Order);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        Assert.Equal(CanonicalJson.Text(Advance(engine).Events), CanonicalJson.Text(Advance(restored).Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(60UL, true)] // First reprisal roll is 9, below the 10 percent threshold.
    [InlineData(5UL, false)] // First roll is 10, outside the threshold.
    public void ConfusionProbabilityBoundaryAndNamedStreamSurviveRestore(ulong seed, bool confused)
    {
        var (engine, rules) = Create(intelligence: 0, seed: seed);
        var result = Advance(engine);
        Assert.Equal(confused, result.Events.OfType<BattleConditionChangedEvent>()
            .Any(e => e.Clock.Day == 2 && e.Condition == UnitCondition.Confused));
        Assert.Contains(engine.Snapshot.Simulation.RandomStreams, s => s.Name == "military-facility-reprisal");
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty,
            PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        Assert.Equal(CanonicalJson.Text(Advance(engine).Events), CanonicalJson.Text(Advance(restored).Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    [Fact]
    public void InvalidReprisalDefinitionsAreRejectedAtContentBoundary()
    {
        foreach (var invalid in new[] { Reprisal with { ProficiencyCategory = 9 }, Reprisal with { DamageDivisor = 0 },
            Reprisal with { IntelligenceDivisor = 0 }, Reprisal with { ConfusionChance = 101 },
            Reprisal with { ConfusionDays = 0 }, Reprisal with { ProficiencyThreshold = -1 } })
            Assert.Throws<ArgumentException>(() => Create(reprisal: invalid));
    }
}
