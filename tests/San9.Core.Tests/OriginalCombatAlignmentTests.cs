using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OriginalCombatAlignmentTests
{
    private static MilitaryDefinition Definition() => new(20, 1, [.. Enumerable.Repeat(0, 20)], [new(0, 1, 1)],
        [new(101, 1, 0, 100000, 1, new(1, 1), TravelMode.Land)
            { Combat = new(10, 10, 0, 10, 80), Attack = new(10, false), CaptureRanks = [BattleRank.Middle] },
         new(202, 1, 0, 100000, 1, new(1, 1), TravelMode.Land) { Combat = new(8, 8, 0, 8, 50), CaptureRanks = [BattleRank.Middle] }],
        [new(1, new(2, 2), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(2, new(92, 2), new(1, 1)) { Combat = new(10, 10, 0, 10, 50) },
         new(3, new(82, 2), new(1, 1)), new(4, new(72, 2), new(1, 1))])
        { UnformedFormationId = 202, RegionIds = [.. Enumerable.Repeat<int?>(1, 20)], CityInfluence = new(8, 10) };

    private static SimulationRules Rules() => new()
    {
        Military = new(Definition()),
        Personnel = new([new(1, new(2, 2)), new(2, new(92, 2)), new(3, new(82, 2)), new(4, new(72, 2)), new(5, new(67, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 2, [5] = 2 }, [new(1, 2), new(1, 3)])
    };

    private static WorldState World(uint food = 1000000) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, food, 100) { LeaderId = 11, BaseTrust = 100 }, new(2, 10000, 1000000, 100) { LeaderId = 21, BaseTrust = 100 },
         new(3, 10000, 1000000, 100) { IsBarbarian = true, LeaderId = 31 }],
        [new(1, 1, 1000, 100, 100, 45, 0, 0, 100, 0, 100),
         new(2, 2, 1000, 100, 100, 50, 0, 0, 100, 0, 100),
         new(3, 3, 1000, 100, 100, 45, 0, 0, 100, 0, 100),
         new(4, null, 0, 100, 100, 0, 0, 0, 100, 0, 100) { Kind = SettlementKind.Facility }],
        [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 1, TacticCooldown = 9 },
         new(21, 2, null, new(70, 70, 70, 70), false) { UnitId = 2 },
         new(31, 3, 3, new(70, 70, 70, 70), false)],
        units: [new(1, 1, new(12, 2), 500) { FormationId = 101, Morale = 70, ServicePlaceId = 1,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Middle) },
                new(2, 2, new(17, 2), 500) { FormationId = 101, Morale = 70, ServicePlaceId = 2,
                    MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(21, BattleRank.Middle) }]);

    [Theory]
    [InlineData(0u, true)]
    [InlineData(2u, true)]
    [InlineData(4u, true)]
    [InlineData(5u, false)]
    public void ExactExhaustionAndPartialShortageHaveTheSameDailyLoss(uint food, bool starved)
    {
        var m = World(food).BeginMutation(); var rules = Rules(); List<DomainEvent> events = [];
        var accounts = EconomicCycle.Apply(ResolutionStage.BeforeDailyCommands, m, rules.Economy, events);
        MilitaryDailyCycle.Apply(m, accounts, rules, events);
        Assert.Equal(starved ? 67 : 70, m.Units[1].Morale); // Own city influence never protects starvation.
        Assert.Equal(starved ? 495u : 500u, m.Units[1].Soldiers);
        Assert.Equal(starved ? 4u : 0u, m.Units[1].Wounded);
        Assert.Equal(starved ? 990u : 1000u, m.Settlements[1].Soldiers);
        Assert.Equal(starved ? 5u : 0u, m.Settlements[1].Wounded);
        Assert.Equal(starved ? 42 : 45, m.Settlements[1].Morale);
        Assert.Equal(69, m.Units[2].Morale); // Supplied army outside its city only takes ordinary decay.
        Assert.Equal(starved ? 2 : 0, events.OfType<DailyAttritionEvent>().Count());
        Assert.Empty(m.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void RoutedLossIsNotDoubledByStarvationOrPreventedByBarbarianOrigin(bool starved, bool barbarian)
    {
        var m = World().BeginMutation(); var rules = Rules(); List<DomainEvent> events = [];
        m.Replace(m.Forces[1] with { IsBarbarian = barbarian });
        m.Replace(m.Units[1] with { Soldiers = 1001, Condition = UnitCondition.Routed, ConditionDaysRemaining = 15 });
        DailyRationAccount[] accounts = [new(new(ResourceOwnerKind.Force, 1), starved)];
        MilitaryDailyCycle.Apply(m, accounts, rules, events);
        var loss = Assert.Single(events.OfType<DailyAttritionEvent>(), e => e.UnitId == 1);
        Assert.Equal(starved, loss.Starved);
        Assert.True(loss.Routed);
        Assert.Equal(11u, loss.SoldierLoss);
        Assert.Equal(5u, loss.AddedWounded); // Effective unformed profile, not the nominal 80% formation.
        Assert.Equal(6u, loss.Dead);
        Assert.Equal(67, m.Units[1].Morale);
        Assert.DoesNotContain(events, e => e is PeriodicMoraleLostEvent { UnitId: 1 });
    }

    [Fact]
    public void NoDemandDoesNotRunMoraleAndInjuryCapacityCannotOverflow()
    {
        var m = World().BeginMutation(); var rules = Rules(); List<DomainEvent> events = [];
        m.Replace(m.Units[1] with { Wounded = MilitaryRulebook.WoundedCapacity, Condition = UnitCondition.Routed });
        MilitaryDailyCycle.Apply(m, [], rules, events);
        MilitaryMorale.Apply(m, [], rules, events);
        Assert.Empty(events);
        MilitaryDailyCycle.Apply(m, [new(new(ResourceOwnerKind.Force, 1), false)], rules, events);
        var loss = Assert.Single(events.OfType<DailyAttritionEvent>());
        Assert.Equal(0u, loss.AddedWounded);
        Assert.Equal(5u, loss.Dead);
        Assert.Equal(MilitaryRulebook.WoundedCapacity, m.Units[1].Wounded);
    }

    [Theory]
    [InlineData(2, 100, 45, false, false, 45)]
    [InlineData(3, 100, 45, false, false, 46)]
    [InlineData(6, 99, 49, false, false, 49)]
    [InlineData(6, 100, 49, false, false, 50)]
    [InlineData(6, 100, 51, false, false, 51)]
    [InlineData(3, 100, 45, true, false, 45)]
    [InlineData(3, 100, 45, false, true, 45)]
    public void GarrisonRecoveryUsesCalendarRootForceTrustAndSupply(int day, int trust, int morale, bool starved, bool barbarian, int expected)
    {
        var m = World().BeginMutation(); List<DomainEvent> events = [];
        m.SetClock(new(190, 1, day));
        m.Replace(m.Forces[1] with { BaseTrust = trust, IsBarbarian = barbarian });
        m.AddCorps(new(77, 1, 11, new(1000, 1000)));
        m.Replace(m.Settlements[1] with { Morale = morale, ConfusionDaysRemaining = 5, CorpsId = 77 });
        m.Replace(m.Units[2] with { Order = new(MilitaryOrderKind.AttackSettlement, null, 1), Position = new(7, 2) });
        MilitaryMorale.Apply(m, [new(new(ResourceOwnerKind.Corps, 77), starved)], Rules(), events);
        Assert.Equal(expected, m.Settlements[1].Morale);
        Assert.Equal(expected - morale, events.OfType<GarrisonMoraleRecoveredEvent>().Count());
    }

    [Theory]
    [InlineData(UnitCondition.Normal, 10, 182u)]
    [InlineData(UnitCondition.NoFormation, 10, 206u)]
    [InlineData(UnitCondition.Confused, 8, 226u)]
    [InlineData(UnitCondition.Routed, 6, 246u)]
    public void ActualDefenseUsesStateEffectAndEffectiveFormation(UnitCondition condition, int effect, uint expected)
    {
        var m = World().BeginMutation(); var rules = Rules();
        m.Replace(m.Units[1] with { Soldiers = 10000, Morale = 100 });
        m.Replace(m.Units[2] with { Soldiers = 10000, Morale = 100, Condition = condition });
        Assert.Equal(effect, MilitaryCombat.Strength(m, m.Units[2]).Effect);
        Assert.Equal(expected, MilitaryCombat.NormalDamage(m, new(1, false), new(2, false), rules.Military!));
        Assert.Equal(101, m.Units[2].FormationId);
        m.Replace(m.Settlements[1] with { ConfusionDaysRemaining = 5 });
        Assert.Equal(8, MilitaryCombat.GarrisonStrength(m, m.Settlements[1]).Effect);
    }

    [Fact]
    public void UnformedProfileReferenceIsValidatedAndRoundTripsWithoutSourceIds()
    {
        var definition = Definition();
        var restored = JsonSerializer.Deserialize<MilitaryDefinition>(JsonSerializer.Serialize(definition))!;
        Assert.Equal(202, new MilitaryRulebook(restored).UnformedFormationId);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { UnformedFormationId = 999 }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Formations = [.. definition.Formations.Select(f => f.Id == 202 ? f with { Combat = null } : f)] }));
    }

    [Theory]
    [InlineData(20, 20)]
    [InlineData(90, 45)]
    public void DisplacementDrawsOnceFromPooledNeighborBuildingsAndCapsOldLoyalty(int loyalty, int expected)
    {
        var state = World(); var rules = Rules(); var m = state.BeginMutation(); List<DomainEvent> events = [];
        m.Replace(m.Officers[11].WithLoyalty(1, loyalty));
        Assert.Equal(new[] { 2, 4, 5 }, PersonnelRelocation.Destinations(m, 1, rules.Personnel));
        state.Commit(m);
        var saved = new SimulationEngine(state, rules).CaptureStableState();
        var first = WorldState.Restore(saved).BeginMutation();
        var second = WorldState.Restore(saved).BeginMutation();
        ulong draw = second.NextRandom("battle-personnel-placement");
        int expectedPlace = new[] { 2, 4, 5 }[draw % 3];
        Assert.False(BattlePersonnel.Resolve(first, 11, new(2, true), null, 0, BattleRank.Middle, false, false,
            rules.Personnel, events, false, new(12, 2), rules.Military!));
        Assert.Equal(expectedPlace, first.Officers[11].SettlementId ?? first.Officers[11].RegionId);
        Assert.Equal(expectedPlace, Assert.Single(events.OfType<PersonAffiliationChangedEvent>()).PlaceId);
        Assert.Equal(expected, first.Officers[11].LoyaltyTo(1));
        Assert.Equal(OfficerStatus.Free, first.Officers[11].Status);
        Assert.Null(first.Officers[11].OwnerId);
        Assert.Null(first.Officers[11].UnitId);
        Assert.Equal(0, first.Officers[11].TacticCooldown);
        Assert.Equal(CanonicalJson.Text(second.Simulation.RandomStreams), CanonicalJson.Text(first.Simulation.RandomStreams));
    }

    [Fact]
    public void NoCandidateRetainsGeographicOriginWithoutGlobalNearestRandomFallback()
    {
        var m = World().BeginMutation(); var rules = Rules(); List<DomainEvent> events = [];
        m.Replace(m.Settlements[2] with { OwnerId = 3 });
        BattlePersonnel.Displace(m, 11, new(12, 2), rules.Personnel, rules.Military!, events);
        Assert.Equal(1, m.Officers[11].SettlementId);
        Assert.Empty(m.Simulation.RandomStreams);
        Assert.Null(m.Officers[11].UnitId);
    }

    [Fact]
    public void DailyAttritionAndRecoveryRemainIdenticalAfterRestore()
    {
        var state = World(); var m = state.BeginMutation(); var rules = Rules();
        m.Replace(m.Units[1] with { Condition = UnitCondition.Routed, ConditionDaysRemaining = 25 });
        state.Commit(m);
        var first = new SimulationEngine(state, rules);
        var second = new SimulationEngine(WorldState.Restore(first.CaptureStableState()), rules);
        var expected = first.AdvanceXun(); var actual = second.AdvanceXun();
        Assert.NotEmpty(expected.Events.OfType<DailyAttritionEvent>());
        Assert.NotEmpty(expected.Events.OfType<GarrisonMoraleRecoveredEvent>());
        Assert.Equal(CanonicalJson.Text(expected.Events), CanonicalJson.Text(actual.Events));
        Assert.Equal(first.Snapshot.StateHash, second.Snapshot.StateHash);
        Assert.NotNull(WorldState.Restore(first.CaptureStableState()));
    }
}
