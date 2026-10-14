using San9.Tests;
using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WarfareAuditAlignmentTests
{
    [Theory]
    [InlineData(1000u, 20, 60, 45)]
    [InlineData(1000u, 0, 60, 35)]
    [InlineData(0u, 20, 60, 70)]
    [InlineData(0u, 20, 95, 100)]
    public void CaptureImmediatelyBlendsOriginalGarrisonWithRewardedAttacker(uint defenders, int morale, int attackerMorale, int expected)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { Soldiers = defenders, Morale = morale, Durability = 0, Wounded = 30 });
        m.Replace(m.Units[1] with { Soldiers = 1000, Morale = attackerMorale, Wounded = 20 });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = Rules() }, events);
        Assert.Equal(expected, m.Settlements[2].Morale);
        Assert.Equal(defenders + 1000, m.Settlements[2].Soldiers);
        Assert.Equal(50u, m.Settlements[2].Wounded);
        Assert.False(m.Units.ContainsKey(1));
        Assert.Null(m.Officers[11].UnitId);
        Assert.Equal(2, m.Officers[11].SettlementId);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
    }

    [Fact]
    public void CaptureOnlyImmediatelyAbsorbsNearbyAttackersOfReceivingCorps()
    {
        var m = World(); List<DomainEvent> events = [];
        // Keep the defender alive: this test isolates absorption, not last-city elimination.
        m.AddSettlement(m.Settlements[2] with { Id = 99 });
        var attack = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        m.Replace(m.Settlements[2] with { Soldiers = 0 });
        m.Replace(m.Units[1] with { Position = new(30, 7), Order = attack, Morale = 60 });
        m.AddUnit(new(3, 1, new(32, 5), 1000) { FormationId = 101, Morale = 40, Order = attack });
        m.AddUnit(new(4, 1, new(10, 7), 1000) { FormationId = 101, Morale = 40, Order = attack });
        m.AddUnit(new(5, 1, new(32, 9), 1000) { FormationId = 101, Morale = 40 });
        var rootTreasury99 = ResourceAccounts.Root(m, 1).Treasury; m.RemoveCorps(ResourceAccounts.Root(m, 1).Id); m.AddCorps(new(99, 1, 11, rootTreasury99) { IsRoot = true });
        m.AddUnit(new(6, 1, new(32, 10), 1000) { FormationId = 101, Morale = 40, Order = attack, CorpsId = 99 });
        m.Replace(m.Units[2] with { Position = new(34, 7), Order = attack });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = Rules() }, events);
        Assert.Equal(new[] { 1, 3 }, events.OfType<UnitAbsorbedEvent>().Select(e => e.UnitId));
        Assert.Equal(67, m.Settlements[2].Morale);
        Assert.Equal(new[] { 2, 4, 5, 6 }, m.Units.Keys.Order());
        Assert.Equal(MilitaryOrderKind.Return, m.Units[4].Order!.Kind);
        Assert.Equal(40, m.Units[4].Morale);
    }

    [Fact]
    public void ImmediateCaptureAbsorptionUsesActualCapacityAndDemobilizesOverflow()
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { Soldiers = 1000, SoldierCapacity = 1500, Morale = 20,
            Durability = 0, Wounded = 49990, DraftPopulation = 0 });
        m.Replace(m.Units[1] with { Soldiers = 1000, Morale = 60, Wounded = 20 });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = Rules() }, events);
        Assert.Equal(1500u, m.Settlements[2].Soldiers);
        Assert.Equal(50000u, m.Settlements[2].Wounded);
        Assert.Equal(36, m.Settlements[2].Morale);
        Assert.Equal(510u, m.Settlements[2].DraftPopulation);
        Assert.False(m.Units.ContainsKey(1));
    }

    [Theory]
    [InlineData(0u, 3000u)]
    [InlineData(49000u, 50000u)]
    public void DestroyedGarrisonTransfersSurvivorsAsWounded(uint existing, uint expected)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { Soldiers = 1000, Wounded = 2000, Durability = 0 });
        m.Replace(m.Units[1] with { Wounded = existing, Morale = 60 });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = Rules(destroyed: true) }, events);
        Assert.Equal(expected, m.Units[1].Wounded);
        Assert.Equal(70, m.Units[1].Morale);
        Assert.True(m.Settlements[2].IsDestroyed);
        Assert.Equal(0u, m.Settlements[2].Soldiers + m.Settlements[2].Wounded);
        Assert.Empty(events.OfType<UnitAbsorbedEvent>());
        var recovery = Assert.Single(events.OfType<ResourceChangedEvent>(), e => e.Reason == "settlement-destroyed-recovery");
        Assert.Equal(3000, recovery.Requested);
        Assert.Equal(expected - existing, recovery.Actual);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FacilityCaptureDoesNotInventAnIncomingOrRecoveryUnit(bool destroyed)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[1] with { Morale = 60 });
        m.Replace(m.Settlements[2] with { Soldiers = 1000, Morale = 20, Durability = 0 });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, true), new() { Military = Rules(destroyed: destroyed) }, events);
        Assert.Equal(70, m.Settlements[1].Morale);
        Assert.Equal(destroyed ? 0 : 20, m.Settlements[2].Morale);
        Assert.Equal(destroyed ? 0u : 1000u, m.Settlements[2].Soldiers);
        Assert.Equal(0u, m.Settlements[1].Wounded);
        Assert.Equal(100, m.Units[1].Morale);
        Assert.Empty(events.OfType<UnitAbsorbedEvent>());
    }

    [Fact]
    public void DestroyedGarrisonRecipientAndRecoverySurviveRestore()
    {
        var world = InitialWorld();
        var m = world.BeginMutation();
        m.AddSettlement(m.Settlements[2] with { Id = 99 });
        var attack = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        m.Replace(m.Settlements[2] with { Durability = 0, Soldiers = 1000, Wounded = 2000 });
        m.Replace(m.Units[1] with { Position = new(30, 7), Order = attack });
        m.AddUnit(new(3, 1, new(32, 5), 1000) { FormationId = 101, Morale = 40, Order = attack });
        m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable, NextUnitId = 4,
            RandomStreams = [new("battle-capture-wounded", 0)] });
        world.Commit(m);
        var restored = WorldState.Restore(world.CreateSnapshot());
        List<DomainEvent> first = [], second = [];
        var rules = new SimulationRules { Military = Rules(destroyed: true) };
        m = world.BeginMutation();
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), rules, first);
        world.Commit(m);
        var other = restored.BeginMutation();
        BattleAftermath.ResolveSettlementDefeat(other, 2, new(1, false), rules, second);
        restored.Commit(other);
        Assert.Equal(3000u, m.Units[3].Wounded);
        Assert.Equal(0u, m.Units[1].Wounded);
        Assert.Equal(CanonicalJson.Text(first), CanonicalJson.Text(second));
        StateAssert.Equal(world.CreateSnapshot(), restored.CreateSnapshot());
        Assert.NotEqual(0UL, m.Simulation.RandomStreams.Single(s => s.Name == "battle-capture-wounded").State);
    }

    [Fact]
    public void AreaTacticCapturesAllTargetsBeforeAbsorbingItsSource()
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { Soldiers = 1 });
        m.AddSettlement(m.Settlements[2] with { Id = 3 });
        var baseline = Rules();
        var military = new MilitaryRulebook(new(20, 3, [.. Enumerable.Repeat(0, 60)], [new(0, 1, 1)],
            [baseline.Formations[101]], [.. baseline.Places.Values, new(3, new(37, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }]));
        var tactic = new TacticRule(999, 7, TacticEffect.Illusion, 13, 10, 0, true, true, [101]) { EffectRadius = 10 };
        IntelligenceTactics.Resolve(m, new(1, false), m.Officers[11], tactic,
            new(MilitaryOrderKind.AttackSettlement, null, 2), 10, new() { Military = military }, events);
        Assert.Equal(new[] { 3, 2 }, events.OfType<SettlementCapturedEvent>().Select(e => e.SettlementId));
        Assert.Equal(1, m.Settlements[2].OwnerId);
        Assert.Equal(1, m.Settlements[3].OwnerId);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        Assert.False(m.Units.ContainsKey(1));
    }

    [Theory]
    [InlineData(0, 1000u, 100, 600, 150)]
    [InlineData(20, 0u, 100, 600, 150)]
    [InlineData(20, 1000u, 0, 600, 150)]
    [InlineData(150, 0u, 100, 600, 150)]
    [InlineData(200, 0u, 100, 600, 200)]
    [InlineData(20, 0u, 100, 603, 150)]
    public void CaptureRaisesEveryRetainedCityToAtLeastQuarterDurability(int durability, uint soldiers, int morale, int maximum, int expected)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { Durability = durability, MaxDurability = maximum, Soldiers = soldiers, Morale = morale });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = Rules() }, events);
        Assert.Equal(1, m.Settlements[2].OwnerId);
        Assert.Equal(expected, m.Settlements[2].Durability);
        Assert.False(Assert.Single(events.OfType<SettlementCapturedEvent>()).Destroyed);
    }

    private static MilitaryRulebook Rules(bool ranged = true, bool destroyed = false) => new(new(20, 3,
        [.. Enumerable.Repeat(0, 60)], [new(0, 1, 1)],
        [new(101, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 100, 10, 50), Attack = new(15, true) { RangedSiege = ranged },
                CaptureRanks = [BattleRank.Middle] }],
        [new(1, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(2, new(32, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50), DestroyedAtZeroDurability = destroyed },
         new(99, new(92, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }])
        { Tactics = [new(901, 8, TacticEffect.Heal, 19, 10, 0, true, false, [101]) { EffectRadius = 20 }] });

    private static WorldMutation World() => InitialWorld().BeginMutation();

    private static WorldState InitialWorld() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 500), new(2, 400)],
        [new(1, 1, 1000, 100, 100, 100, 500, 100, 100, 100, 100),
         new(2, 2, 1000, 100, 100, 100, 500, 100, 100, 100, 100)],
        [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 1 },
         new(21, 2, null, new(80, 80, 80, 80), false) { UnitId = 2 }],
        units: [new(1, 1, new(22, 7), 10000) { FormationId = 101, Morale = 100,
                    CommanderId = 11, OrderClass = UnitOrderClass.Military, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Middle) },
                new(2, 2, new(52, 7), 10000) { FormationId = 101, Morale = 100,
                    CommanderId = 21, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(100000, 100000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(100000, 100000)) { IsRoot = true }]);

    [Theory]
    [InlineData(100, 1, UnitOrderClass.Military, true, 70)]
    [InlineData(20, 1, UnitOrderClass.Military, true, 1)]
    [InlineData(100, 0, UnitOrderClass.Military, true, 100)]
    [InlineData(100, 1, UnitOrderClass.NonMilitary, true, 100)]
    [InlineData(100, 1, UnitOrderClass.Military, false, 100)]
    public void HighSpiritsExpiresOnceWithIndependentPenalty(int morale, int days, UnitOrderClass order, bool pending, int expected)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Units[1] with { Morale = morale, Condition = UnitCondition.HighSpirits,
            ConditionDaysRemaining = days, OrderClass = order, HighSpiritsMoralePenaltyPending = pending });
        MilitaryConditions.Tick(m, events);
        MilitaryConditions.Tick(m, events);
        Assert.Equal(expected, m.Units[1].Morale);
        Assert.Equal(UnitCondition.Normal, m.Units[1].Condition);
        Assert.False(m.Units[1].HighSpiritsMoralePenaltyPending);
        Assert.Equal(expected == morale ? 0 : 1, events.OfType<HighSpiritsExpiredEvent>().Count());
    }

    [Fact]
    public void HealingRetainsHighSpiritsButCancelsItsFuturePenaltyAndRoundTrips()
    {
        var m = World(); var rules = Rules(); var heal = rules.Tactics[901]; List<DomainEvent> events = [];
        m.Replace(m.Units[1] with { Condition = UnitCondition.HighSpirits, ConditionDaysRemaining = 2,
            HighSpiritsMoralePenaltyPending = true });
        Assert.Empty(TacticSupport.Targets(m, new(1, false), heal, rules));
        m.Replace(m.Units[1] with { Wounded = 10 });
        int[] targets = TacticSupport.Targets(m, new(1, false), heal, rules);
        Assert.Equal([1], targets);
        TacticSupport.Resolve(m, new(1, false), m.Officers[11], heal, targets, [m.Officers[11]], rules, events);
        Assert.Equal(UnitCondition.HighSpirits, m.Units[1].Condition);
        Assert.Equal(2, m.Units[1].ConditionDaysRemaining);
        Assert.False(m.Units[1].HighSpiritsMoralePenaltyPending);
        Assert.False(Assert.Single(events.OfType<TacticHealingEvent>()).ConditionCleared);
        var restored = JsonSerializer.Deserialize<UnitState>(JsonSerializer.Serialize(m.Units[1]))!;
        Assert.Equal(m.Units[1].HighSpiritsMoralePenaltyPending, restored.HighSpiritsMoralePenaltyPending);
        m.Replace(restored);
        MilitaryConditions.Tick(m, events);
        MilitaryConditions.Tick(m, events);
        Assert.Equal(100, m.Units[1].Morale);
        Assert.Empty(events.OfType<HighSpiritsExpiredEvent>());
    }

    [Fact]
    public void EachCompletedAttackerPhaseDamagesCurrentCityValuesAndCaptureStopsIt()
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { PublicOrder = 99, Commerce = 99, Cultivation = 99 });
        CityBattleEffects.CompleteAttack(m, 1, 2, 2, events);
        CityBattleEffects.CompleteAttack(m, 1, 2, 2, events);
        Assert.Equal(97, m.Settlements[2].PublicOrder);
        Assert.Equal(97, m.Settlements[2].Commerce);
        Assert.Equal(97, m.Settlements[2].Cultivation);
        Assert.Equal(100, m.Settlements[2].MaxCommerce);
        Assert.Equal(100, m.Settlements[2].MaxCultivation);
        m.Replace(m.Settlements[2] with { OwnerId = 1 });
        CityBattleEffects.CompleteAttack(m, 1, 2, 2, events);
        Assert.Equal(2, events.OfType<CityBattleDamageEvent>().Count());
    }

    [Theory]
    [InlineData(500, 500, 400, 600, 600)]
    [InlineData(900, 500, 400, 650, 699)]
    [InlineData(10, 300, 500, 0, 0)]
    [InlineData(0, 1000, 0, 300, 300)]
    public void CaptureUsesTrustDifferenceRandomCeilingAndThreeHundredCap(int publicOrder, int newTrust, int oldTrust, int min, int max)
    {
        var m = World(); List<DomainEvent> events = [];
        m.Replace(m.Forces[1] with { BaseTrust = newTrust });
        m.Replace(m.Forces[2] with { BaseTrust = oldTrust });
        Assert.InRange(CityBattleEffects.CapturedPublicOrder(m, m.Settlements[2] with { PublicOrder = publicOrder }, 1, events), min, max);
        Assert.Equal(publicOrder, CityBattleEffects.CapturedPublicOrder(m, m.Settlements[2] with { PublicOrder = publicOrder }, 2, events));
        Assert.Single(events);
    }

    [Fact]
    public void CapturedTreasuryIncludesBothPopulationsAndTruncatesBeforeTakingHalf()
    {
        var m = World(); List<DomainEvent> events = [];
        // Captured city weighs 1,000 + 1,000 + 3,000 = 5,000; old side total is 20,000.
        m.Replace(m.Settlements[2] with { Population = 1000, DraftPopulation = 3000 });
        m.Replace(m.Units[2] with { Soldiers = 15000 });
        BattleTreasury.Transfer(m, 2, null, BattleTreasury.Weight(m.Settlements[2]), new(1, false), 2, "capture", events);
        Assert.Equal(87600u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        Assert.Equal(112400u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(87600u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
    }

    [Fact]
    public void RemoteCatapultLeavesOneWallPointThenApproachesToFinish()
    {
        var m = World(); var rules = Rules(); var attack = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        m.Replace(m.Settlements[2] with { Durability = 1 });
        Assert.Equal(0, MilitaryCombat.WallDamage(m, m.Units[1], m.Settlements[2], rules));
        Assert.Equal(1, MilitaryCombat.ApproachRange(m, m.Units[1], attack, rules, m.Units[1].Position));
        m.Replace(m.Units[1] with { Position = new(27, 7) });
        Assert.Equal(1, MilitaryCombat.WallDamage(m, m.Units[1], m.Settlements[2], rules));
    }

    [Theory]
    [InlineData(1000u, 20, 15)]
    [InlineData(999u, 20, 1)]
    [InlineData(1000u, 19, 1)]
    public void CatapultRemoteStopHasSoldierAndDurabilityThresholds(uint soldiers, int durability, int reach)
    {
        var m = World(); var rules = Rules();
        m.Replace(m.Settlements[2] with { Soldiers = soldiers, Durability = durability });
        Assert.Equal(reach, MilitaryCombat.ApproachRange(m, m.Units[1], new(MilitaryOrderKind.AttackSettlement, null, 2), rules, m.Units[1].Position));
        Assert.Equal(0, MilitaryCombat.WallDamage(m, m.Units[1], m.Settlements[2], Rules(false)));
    }

    [Fact]
    public void DiagonalAttackReachUsesExpandedRectanglesWithoutChangingTravelDistance()
    {
        var a = new MapOccupancy(new(1, 1), new(3, 3));
        var b = new MapOccupancy(new(5, 5), new(3, 3));
        Assert.Equal(4, a.DistanceTo(b));
        Assert.True(a.IntersectsExpanded(b, 2));
        Assert.False(a.IntersectsExpanded(b, 1));
    }

    [Fact]
    public void DailyEngagementAndWorkDestructionHaveSeparateGrowthSources()
    {
        var m = World(); var rules = Rules(); List<DomainEvent> events = [];
        m.Replace(m.Officers[11] with { EquippedTacticId = 901, LearnedTactics = [901] });
        m.Replace(m.Units[1] with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 } });
        m.Replace(m.Units[2] with { Position = new(27, 7) });
        BattleLearning.Daily(m, rules, events);
        Assert.Equal(1, m.Officers[11].TacticProficiencies[8]);
        Assert.Equal(0, m.Officers[21].TacticProficiencies[8]);
        m.Replace(m.Settlements[2] with { Kind = SettlementKind.Facility, CanHostPersons = false, Durability = 0 });
        BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = rules }, events);
        Assert.Equal(3, m.Officers[11].TacticProficiencies[5]);
        Assert.Single(events.OfType<FacilityDestroyedEvent>());
        Assert.Throws<InvalidOperationException>(() => BattleAftermath.ResolveSettlementDefeat(m, 2, new(1, false), new() { Military = rules }, events));
        Assert.Equal(3, m.Officers[11].TacticProficiencies[5]);
    }
}
