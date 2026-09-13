using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class FacilityRuleTests
{
    private static FacilityRule Rule => new(9000, 1000, 600, new(3, 3), 15, true, true, true, 250000, null, null, null, []);

    [Fact]
    public void ConstructionGrowthRequiresExplicitWorkTacticReference()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        { Facilities = [Rule with { ConstructionGrowthTacticId = 7000 }],
            Tactics = [new(7000, 2, TacticEffect.Construction, 0, 0, 100, false, false, [])] };
        Assert.Equal(7000, new MilitaryRulebook(definition).Facilities[9000].ConstructionGrowthTacticId);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { Tactics = [] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Tactics = [definition.Tactics[0] with { Effect = TacticEffect.CounterTrap }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Facilities = [definition.Facilities[0] with { Buildable = false }] }));
    }

    [Fact]
    public void FacilityTacticProfilesRejectMissingEffectsAndInvalidEnabledReferences()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [],
            [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 9000, EnabledTacticIds = [7000] }])
        { Facilities = [Rule with { Tactics = new(10, 3, true, [7000]) }],
            Tactics = [new(7000, 3, TacticEffect.Martial, 1, 9, 0, true, false, [])] };
        var rules = new MilitaryRulebook(definition);
        Assert.Equal(7000, rules.FacilityTactic(new(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100))!.Id);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Places = [definition.Places[0] with { EnabledTacticIds = [7001] }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Facilities = [Rule with { Tactics = new(101, 3, true, [7000]) }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Facilities = [Rule with { Tactics = new(10, 3, true, [7001]) }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with
            { Tactics = [definition.Tactics[0] with { Effect = TacticEffect.CounterTrap }] }));
    }

    [Fact]
    public void ConstructionCostAndProgressPreserveTruncationAndCapacityWithoutOverflow()
    {
        Assert.Equal(1000, Rule.Cost(0));
        Assert.Equal(666, Rule.Cost(1));
        Assert.Equal(500, Rule.Cost(2));
        Assert.Equal(0, Rule.Cost(int.MaxValue));
        Assert.Equal(30, Rule.DailyProgress(10000, 100, 600));
        Assert.Equal(1, Rule.DailyProgress(1, 0, 600));
        Assert.Equal(2, Rule.DailyProgress(10000, 100, 2));
        Assert.Equal(0, Rule.DailyProgress(0, 100, 600));
        Assert.Equal(0, Rule.DailyProgress(10000, 100, 0));
        Assert.Equal(int.MaxValue, Rule.DailyProgress(uint.MaxValue, int.MaxValue, int.MaxValue));
    }

    [Fact]
    public void SitesRequireKnownBuildableTypesAndValidOccupancy()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], [])
        { Facilities = [Rule], ConstructionSites = [new(8000, new(2, 2), [9000])] };
        var rules = new MilitaryRulebook(definition);
        Assert.Single(rules.ConstructionSites);
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { Facilities = [Rule with { Buildable = false }] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { ConstructionSites = [new(8000, new(0, 0), [9000])] }));
        Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { ConstructionSites = [new(8000, new(2, 2), [3])] }));
    }

    [Fact]
    public void AvailableLocationTracksOwnershipExistingBuildingsAndUnits()
    {
        var site = new ConstructionSite(8000, new(7, 2), [9000]);
        var rules = new MilitaryRulebook(new(2, 1, [0, 0], [new(0, 1, null)],
            [new(1, 1, 0, 10000, 1, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 9000 },
                new(8000, new(7, 2), new(3, 3)) { FacilityTypeId = 9000 }])
            { Facilities = [Rule], ConstructionSites = [site] });
        var city = new SettlementState(1, 1, 100, 100, 100, 100, 0, 0, 100, 0, 100);
        var geography = new Dictionary<int, int> { [8000] = 1 };
        bool Available(IEnumerable<SettlementState> places, IEnumerable<UnitState> units) =>
            ConstructionPlacement.IsAvailable(1, site, Rule, rules, geography, places, units);
        Assert.True(Available([city], []));
        Assert.False(Available([city with { OwnerId = 2 }], []));
        Assert.False(Available([city], [new(1, 2, new(7, 2), 100) { FormationId = 1 }]));
        Assert.False(Available([city, city with { Id = 8000, Kind = SettlementKind.Facility }], []));
        Assert.True(Available([city, city with { Id = 8000, Kind = SettlementKind.Facility, IsDestroyed = true }], []));
    }
}
