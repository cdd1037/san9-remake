using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceEliminationTests
{
    [Theory]
    [InlineData(true, 1200u)]
    [InlineData(false, 0u)]
    public void ResidualGarrisonDemobilizesAndResetsWithoutDestroyingTheBuilding(bool associatedCity, uint returned)
    {
        var world = World();
        world.Replace(world.Settlements[10] with { Wounded = 200, ConfusionDaysRemaining = 4 });
        var events = Eliminate(world, Rules(associatedCity: associatedCity));
        var place = world.Settlements[10];
        Assert.Null(place.OwnerId);
        Assert.Null(place.CorpsId);
        Assert.Equal(0u, place.Soldiers + place.Wounded);
        Assert.Equal(0, place.Morale);
        Assert.Equal(0, place.ConfusionDaysRemaining);
        Assert.Equal(100, place.Durability);
        Assert.False(place.IsDestroyed);
        Assert.Equal(returned, world.Settlements[2].DraftPopulation);
        Assert.Equal(place, Assert.Single(events.OfType<SettlementNeutralizedEvent>()).Result);
        Assert.Empty(events.OfType<SettlementCapturedEvent>());
        Assert.True(events.FindIndex(e => e is SettlementNeutralizedEvent) < events.FindIndex(e => e is ForceEliminatedEvent));
    }

    [Theory]
    [InlineData(true, 3)]
    [InlineData(false, 10)]
    public void StationedMembersUseSharedPlacementAndKeepTheAdoptedEmptyPoolFallback(bool neighbors, int destination)
    {
        var world = World();
        world.Replace(world.Officers[11] with { TacticCooldown = 8, OfficeId = 7 });
        var events = Eliminate(world, Rules(neighbors));
        var person = world.Officers[11];
        Assert.Equal(OfficerStatus.Free, person.Status);
        Assert.Equal(destination, person.SettlementId);
        Assert.Null(person.OwnerId);
        Assert.Null(person.OfficeId);
        Assert.Equal(0, person.TacticCooldown);
        Assert.Equal(0, person.LoyaltyTo(1));
        Assert.Equal(190, person.StatusStartYear);
        Assert.Single(events.OfType<PersonAffiliationChangedEvent>(), e => e.OfficerId == 11);
        Assert.Equal(neighbors, world.Simulation.RandomStreams.Any(s => s.Name == "battle-personnel-placement"));
    }

    [Fact]
    public void ForeignPrisonersReturnButExternalDetentionOfFormerMembersIsPreserved()
    {
        var world = World();
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive, SettlementId = 2, StatusStartYear = 180 });
        world.Replace(world.Officers[30] with { OwnerId = 2, Status = OfficerStatus.Captive });
        Eliminate(world);
        Assert.Equal(OfficerStatus.Serving, world.Officers[30].Status);
        Assert.Equal(2, world.Officers[30].OwnerId);
        Assert.Equal(2, world.Officers[30].SettlementId);
        Assert.Contains(world.Simulation.Tasks, t => t.OfficerId == 30 && t.Kind == PersonnelReturns.Kind);
        Assert.Null(world.Officers[12].OwnerId);
        Assert.Equal(OfficerStatus.Captive, world.Officers[12].Status);
        Assert.Equal(2, world.Officers[12].SettlementId);
        Assert.Equal(180, world.Officers[12].StatusStartYear);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FieldTroopsReturnInFullToTheirPhysicalCityIncludingWoundedAndOverCap(bool noCity)
    {
        var world = World();
        world.Replace(world.Settlements[10] with { Soldiers = 0 });
        world.Replace(world.Settlements[3] with { DraftPopulation = 500000 });
        world.Replace(world.Officers[11] with { SettlementId = null, UnitId = 200 });
        world.AddUnit(new(200, 1, new(302, 2), 10001) { Wounded = 100, FormationId = 1, CorpsId = 10, ServicePlaceId = 10,
            CommanderId = 11, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) });
        world.SetSimulation(world.Simulation with { NextUnitId = 201 });
        var basis = Rules();
        var military = noCity ? new MilitaryRulebook(new(80, 1, Enumerable.Repeat(0, 80).ToImmutableArray(),
            [new(0, 1, null)], basis.Military!.Formations.Values.ToImmutableArray(), basis.Military.Places.Values.ToImmutableArray())
            { RegionIds = Enumerable.Repeat<int?>(10, 80).ToImmutableArray() }) : basis.Military;
        var events = Eliminate(world, new() { Military = military, Personnel = noCity ? Rules(associatedCity: false).Personnel : basis.Personnel });
        Assert.Equal(noCity ? 500000u : 510101u, world.Settlements[3].DraftPopulation);
        Assert.Equal(0u, world.Settlements[2].DraftPopulation);
        Assert.Equal(0u, world.Settlements[10].Wounded);
        Assert.Empty(world.Units);
        var ended = Assert.Single(events.OfType<UnitTerminatedEvent>());
        Assert.Equal(UnitTerminationReason.ForceEliminated, ended.Reason);
        Assert.Equal(noCity ? 10101 : 0, ended.UnrecoveredSoldiers);
        Assert.Equal(200, ended.Target.Id);
    }

    [Theory]
    [InlineData(OfficerStatus.Serving, 0)]
    [InlineData(OfficerStatus.Undiscovered, 0)]
    [InlineData(OfficerStatus.NotAppeared, 0)]
    [InlineData(OfficerStatus.Dead, 0)]
    [InlineData(OfficerStatus.Special, 0)]
    public void RetirementRemovesTheOldLoyaltyRelationshipRegardlessOfStatus(OfficerStatus status, int loyalty)
    {
        var world = World();
        world.Replace(world.Officers[30] with { Status = status, OwnerId = status == OfficerStatus.Serving ? 2 : null,
            SettlementId = status == OfficerStatus.Serving ? 2 : 10 });
        world.Replace(world.Officers[2].WithLoyalty(1, 100));
        Eliminate(world);
        Assert.Equal(loyalty, world.Officers[30].LoyaltyTo(1));
        Assert.All(world.AllOfficers.Values, p => Assert.False(p.ForceLoyalties.ContainsKey(1)));
        Assert.Equal(0, world.Officers[2].LoyaltyTo(1));
        Assert.Equal(status, world.Officers[30].Status);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(100)]
    public void RetirementAlsoRemovesDormantPeoplesZeroAndNonzeroRelationships(int loyalty)
    {
        var world = World();
        world.Replace(world.Officers[30].WithLoyalty(1, loyalty).WithLoyalty(2, 77) with { IsDormant = true });
        Eliminate(world);
        Assert.False(world.AllOfficers[30].ForceLoyalties.ContainsKey(1));
        Assert.Equal(77, world.AllOfficers[30].LoyaltyTo(2));
        Assert.True(world.AllOfficers[30].IsDormant);
    }

    [Fact]
    public void ARemainingCityRecoversItsOwnGarrisonWithoutOverwritingPopulationAndRetiresOnce()
    {
        var world = World();
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.City, DraftPopulation = 500000 });
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == 1).ToArray())
            world.Replace(person with { OwnerId = null, Status = OfficerStatus.Free });
        world.Replace(world.Forces[1] with { LeaderId = null });
        world.Replace(world.Corps[10] with { LeaderId = null });
        var events = Eliminate(world);
        Assert.Equal(501000u, world.Settlements[10].DraftPopulation);
        Assert.DoesNotContain(world.Corps.Values, c => c.OwnerId == 1);
        Assert.DoesNotContain(world.Simulation.Controls, c => c.ForceId == 1);
        Assert.Single(events.OfType<ForceEliminatedEvent>());
        Assert.Empty(Eliminate(world).OfType<SettlementNeutralizedEvent>());
    }

    [Fact]
    public void NeutralizationClearsOtherArmiesTargetsAndPreservesTransferredAssets()
    {
        var world = World();
        world.Replace(world.Officers[2] with { SettlementId = null, UnitId = 200 });
        world.AddUnit(new(200, 2, new(102, 2), 2000) { FormationId = 1, CorpsId = 20, CommanderId = 2,
            ServicePlaceId = 2, Order = new(MilitaryOrderKind.AttackSettlement, null, 10),
            Waypoint = new(92, 2), MovementRemainder = 1,
            ContactTarget = new(10, true), MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(2, BattleRank.Front) });
        world.SetSimulation(world.Simulation with { NextUnitId = 201 });
        Eliminate(world);
        Assert.Null(world.Units[200].Order);
        Assert.Null(world.Units[200].ContactTarget);
        Assert.Null(world.Units[200].Waypoint);
        Assert.Equal(0, world.Units[200].MovementRemainder);
        Assert.Equal(2000u, world.Units[200].Soldiers);
        Assert.Equal(1000u, world.Settlements[3].Soldiers);
        Assert.Equal(2, world.Settlements[3].OwnerId);
    }

    private static List<DomainEvent> Eliminate(WorldMutation world, SimulationRules? rules = null)
    {
        rules ??= Rules();
        WorldInvariants.Validate(world);
        List<DomainEvent> events = [];
        GovernanceLifecycle.Reconcile(world, new HashSet<int> { 1 }, rules, events, true);
        CorpsDelegations.Cleanup(world, rules.Military, events);
        world.MaintainLeadership(events);
        WorldInvariants.Validate(world);
        return events;
    }

    private static SettlementState Place(int id, int owner) =>
        new(id, owner, 1000, 100, 100, 70, 500, 0, 1000, 0, 1000)
        { Kind = id == 10 ? SettlementKind.Facility : SettlementKind.City, CorpsId = owner * 10 };

    private static OfficerState Person(int id, int? owner, int place, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, place, new(60, 60, 60, 60), false) { Status = status }.WithLoyalty(1, 100);

    private static WorldMutation World() => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 100) { LeaderId = 1 }, new(2, 100) { LeaderId = 2 }],
        [Place(2, 2), Place(3, 2), Place(10, 1)],
        [Person(1, 1, 10, OfficerStatus.Ruler), Person(2, 2, 2, OfficerStatus.Ruler),
            Person(11, 1, 10), Person(12, 1, 10), Person(30, null, 10, OfficerStatus.Undiscovered)],
        corps: [new(10, 1, 1, new(1000, 2000)) { IsRoot = true }, new(20, 2, 2, new(1000, 2000)) { IsRoot = true }],
        controls: [new("player", 1, ControllerKind.Player)]).BeginMutation();

    private static SimulationRules Rules(bool neighbors = true, bool associatedCity = true) => new()
    {
        Personnel = new([new(2, new(102, 2)), new(3, new(302, 2)), new(10, new(52, 2))],
            associatedCity ? new Dictionary<int, int> { [2] = 2, [3] = 3, [10] = 2 } : new Dictionary<int, int> { [2] = 2, [3] = 3 }, neighbors ? [new(2, 3)] : []),
        Military = new(new(80, 1, Enumerable.Repeat(0, 80).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 1, 0, 1000000, 5, new(1, 1), TravelMode.Land)
                { CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle] }],
            [new(2, new(102, 2), new(1, 1)), new(3, new(302, 2), new(1, 1)), new(10, new(52, 2), new(1, 1))])
            { RegionIds = Enumerable.Range(0, 80).Select(i => (int?)(i < 40 ? 2 : 3)).ToImmutableArray() })
    };
}
