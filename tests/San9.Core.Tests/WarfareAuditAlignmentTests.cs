using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WarfareAuditAlignmentTests
{
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

    private static MilitaryRulebook Rules(bool ranged = true) => new(new(20, 3,
        [.. Enumerable.Repeat(0, 60)], [new(0, 1, 1)],
        [new(101, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 100, 10, 50), Attack = new(15, true) { RangedSiege = ranged },
                CaptureRanks = [BattleRank.Middle] }],
        [new(1, new(2, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(2, new(32, 7), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) }])
        { Tactics = [new(901, 8, TacticEffect.Heal, 19, 10, 0, true, false, [101]) { EffectRadius = 20 }] });

    private static WorldMutation World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100000, 100000, 500), new(2, 100000, 100000, 400)],
        [new(1, 1, 1000, 100, 100, 100, 500, 100, 100, 100, 100),
         new(2, 2, 1000, 100, 100, 100, 500, 100, 100, 100, 100)],
        [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 1 },
         new(21, 2, null, new(80, 80, 80, 80), false) { UnitId = 2 }],
        units: [new(1, 1, new(22, 7), 10000) { FormationId = 101, Morale = 100,
                    OrderClass = UnitOrderClass.Military, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Middle) },
                new(2, 2, new(52, 7), 10000) { FormationId = 101, Morale = 100,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) }]).BeginMutation();

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
        m.Replace(m.Forces[1] with { Trust = newTrust });
        m.Replace(m.Forces[2] with { Trust = oldTrust });
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
        Assert.Equal(87600u, m.Forces[2].Money);
        Assert.Equal(112400u, m.Forces[1].Money);
        Assert.Equal(87600u, m.Forces[2].Food);
    }

    [Fact]
    public void RemoteCatapultLeavesOneWallPointThenApproachesToFinish()
    {
        var m = World(); var rules = Rules(); var attack = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        m.Replace(m.Settlements[2] with { Durability = 1 });
        Assert.Equal(0, MilitaryCombat.WallDamage(m, m.Units[1], m.Settlements[2], rules));
        Assert.Equal(5, MilitaryCombat.ApproachRange(m, m.Units[1], attack, rules, m.Units[1].Position));
        m.Replace(m.Units[1] with { Position = new(27, 7) });
        Assert.Equal(1, MilitaryCombat.WallDamage(m, m.Units[1], m.Settlements[2], rules));
    }

    [Theory]
    [InlineData(1000u, 20, 15)]
    [InlineData(999u, 20, 5)]
    [InlineData(1000u, 19, 5)]
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
