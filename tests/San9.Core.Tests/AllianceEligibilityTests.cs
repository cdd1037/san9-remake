using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AllianceEligibilityTests
{
    private static SettlementState City(int owner, uint soldiers = 0, uint draft = 0) =>
        new(owner * 100, owner, soldiers, 100, 100, 50, 50, 0, 0, 0, 0) { DraftPopulation = draft };

    private static WorldMutation World() => WorldState.Create(new(190, 1, 1), 42,
        Enumerable.Range(1, 5).Select(id => new ForceState(id, 0, 0, 50) { BaseTrust = 40, LeaderId = id }),
        Enumerable.Range(1, 5).Select(id => City(id, id == 1 ? 100000u : 0, id == 1 ? 200001u : 0)),
        Enumerable.Range(1, 5).Select(id => new OfficerState(id, id, id * 100, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler }),
        relations: Enumerable.Range(1, 5).SelectMany(first => Enumerable.Range(first + 1, 5 - first).Select(second => new ForceRelation(first, second, 0))))
        .BeginMutation();

    private static SimulationRules Rules() => new()
    {
        Personnel = new(Enumerable.Range(1, 5).Select(id => new PersonPlace(id * 100, new(id, 0))),
            Enumerable.Range(1, 5).ToDictionary(id => id * 100, id => id * 100),
            cityProvinces: Enumerable.Range(1, 5).ToDictionary(id => id * 100, _ => 91))
    };

    [Fact]
    public void ThresholdIncludesDraftPopulationButNotWoundedAndIsStrict()
    {
        var world = World();
        var rules = Rules();
        Assert.Equal(300001, AllianceEligibility.Find(world, rules).LargestRealm!.MilitaryPopulation);
        world.Replace(world.Settlements[100] with { DraftPopulation = 200000, Wounded = 50000 });
        Assert.Null(AllianceEligibility.Find(world, rules).LargestRealm);
        world.AddUnit(new UnitState(11, 1, new(0, 0), 1));
        Assert.Equal(300001, AllianceEligibility.Find(world, rules).LargestRealm!.MilitaryPopulation);
    }

    [Fact]
    public void PrimaryTargetMustOwnACityEvenWhenFacilitiesSupplyThePopulationThreshold()
    {
        var world = World();
        world.Replace(world.Settlements[100] with { Kind = SettlementKind.Facility, Soldiers = 400000, DraftPopulation = 0 });
        Assert.Null(AllianceEligibility.Find(world, Rules()).LargestRealm);
    }

    [Fact]
    public void LowPrestigeUsesItsOwnStrictPopulationAndTrustGates()
    {
        var world = World();
        world.Replace(world.Forces[1] with { BaseTrust = 29 });
        world.Replace(City(1, 200000));
        Assert.Null(AllianceEligibility.Find(world, Rules()).LowPrestige);
        world.Replace(City(1, 200001));
        Assert.Equal(1, AllianceEligibility.Find(world, Rules()).LowPrestige!.ForceId);
        world.Replace(world.Forces[1] with { BaseTrust = 30 });
        Assert.Null(AllianceEligibility.Find(world, Rules()).LowPrestige);
    }

    [Fact]
    public void MemberFiltersExcludeHighRelationsParentsAndAutomaticBarbarians()
    {
        var world = World();
        world.Replace(new ForceRelation(1, 2, 59));
        Assert.Equal(new[] { 2, 3, 4, 5 }, AllianceEligibility.Find(world, Rules()).LargestRealm!.EligibleMemberForceIds);
        world.Replace(new ForceRelation(1, 2, 60));
        Assert.Equal(new[] { 3, 4, 5 }, AllianceEligibility.Find(world, Rules()).LargestRealm!.EligibleMemberForceIds);
        world.Replace(world.Officers[3] with { ParentId = 1 });
        Assert.Null(AllianceEligibility.Find(world, Rules()).LargestRealm);
        world.Replace(world.Officers[3] with { ParentId = null });
        world.Replace(world.Forces[3] with { IsBarbarian = true });
        Assert.Null(AllianceEligibility.Find(world, Rules()).LargestRealm);
        world.SetSimulation(world.Simulation with { Controls = [new("human", 3, ControllerKind.Player)] });
        Assert.Equal(new[] { 3, 4, 5 }, AllianceEligibility.Find(world, Rules()).LargestRealm!.EligibleMemberForceIds);
    }

    [Fact]
    public void TopCandidateWithTooFewMembersDoesNotFallBackToSecondPlace()
    {
        var world = World();
        world.Replace(City(2, 300001));
        world.Replace(new ForceRelation(1, 3, 60));
        world.Replace(new ForceRelation(1, 4, 60));
        Assert.Null(AllianceEligibility.Find(world, Rules()).LargestRealm);
        world.Replace(City(1, 300000));
        Assert.Equal(2, AllianceEligibility.Find(world, Rules()).LargestRealm!.ForceId);
    }

    [Fact]
    public void CapturedFacilitySharesItsCityProvinceEvenWhenTheCityHasAnotherOwner()
    {
        var world = World();
        var provinces = new Dictionary<int, int> { [100] = 10, [200] = 20, [300] = 20, [400] = 20, [500] = 20 };
        var regions = Enumerable.Range(1, 5).ToDictionary(id => id * 100, id => id * 100);
        regions.Add(9201, 200);
        var rules = new SimulationRules
        {
            Personnel = new(provinces.Keys.Select(id => new PersonPlace(id, new(0, 0))), regions, cityProvinces: provinces),
            Military = new(new(1, 1, [0], [new(0, 1, null)], [], [new(201, MapCoordinates.Center(new(0, 0)), new(1, 1))])
                { RegionIds = [9201], TerritoryExits = [TerritoryExits.All] })
        };
        Assert.Null(AllianceEligibility.Find(world, rules).LargestRealm);
        world.AddSettlement(new SettlementState(201, 1, 0, 100, 100, 50, 0, 0, 0, 0, 0) { Kind = SettlementKind.Facility });
        Assert.Equal(new[] { 2, 3, 4, 5 }, AllianceEligibility.Find(world, rules).LargestRealm!.EligibleMemberForceIds);
        world.Replace(world.Settlements[201] with { OwnerId = 2 });
        Assert.Null(AllianceEligibility.Find(world, rules).LargestRealm);
    }

    [Fact]
    public void InTransitReinforcementsRemainPartOfTheForcesMilitaryPopulation()
    {
        var world = World();
        world.Replace(City(1, 100000, 197001));
        world.SetSimulation(world.Simulation with { Tasks = [new(Guid.NewGuid(), "personnel-return", "human", 1, 1, 100,
            world.Clock, world.Clock.NextDay(), 0, 0) { Reinforcements = new(3000, 50, 100) }] });
        Assert.Equal(300001, AllianceEligibility.Find(world, Rules()).LargestRealm!.MilitaryPopulation);
    }

    [Fact]
    public void RegionNeighborsReadCurrentOwnersAndDoNotRequireSharedProvinceOrMatchingIds()
    {
        var world = World();
        var military = new MilitaryDefinition(3, 2, [0, 0, 0, 0, 0, 0], [new(0, 1, null)], [],
            [new(100, MapCoordinates.Center(new(1, 0)), new(1, 1)),
             new(200, MapCoordinates.Center(new(0, 0)), new(1, 1)),
             new(300, MapCoordinates.Center(new(2, 0)), new(1, 1)),
             new(400, MapCoordinates.Center(new(1, 1)), new(1, 1))])
        { RegionIds = [902, 901, 903, null, 904, null],
          TerritoryExits = [TerritoryExits.None, TerritoryExits.All, TerritoryExits.None, TerritoryExits.None, TerritoryExits.None, TerritoryExits.None] };
        var rules = new SimulationRules { Military = new(military) };
        Assert.Equal(new[] { 2, 3, 4 }, AllianceEligibility.Find(world, rules).LargestRealm!.EligibleMemberForceIds);
        world.Replace(world.Settlements[400] with { OwnerId = 3 });
        Assert.Null(AllianceEligibility.Find(world, rules).LargestRealm);
        world.Replace(world.Settlements[400] with { OwnerId = 4, IsDestroyed = true });
        Assert.Null(AllianceEligibility.Find(world, rules).LargestRealm);
    }

    [Fact]
    public void QueryIsPureAndProvidesIdenticalCandidatesAfterRestore()
    {
        var mutation = World();
        var world = WorldState.Create(mutation.Clock, 42, mutation.Forces.Values, mutation.Settlements.Values, mutation.Officers.Values);
        var engine = new SimulationEngine(world, Rules());
        var before = engine.CaptureStableState();
        string candidates = CanonicalJson.Text(engine.GetAllianceCandidates());
        Assert.Equal(candidates, CanonicalJson.Text(engine.GetAllianceCandidates()));
        Assert.Equal(before.StateHash, engine.Snapshot.StateHash);
        Assert.Equal(candidates, CanonicalJson.Text(new SimulationEngine(WorldState.Restore(before), Rules()).GetAllianceCandidates()));
    }
}
