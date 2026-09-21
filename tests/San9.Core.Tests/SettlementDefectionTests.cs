using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class SettlementDefectionTests
{
    [Theory]
    [InlineData(false, 3)]
    [InlineData(true, 2)]
    public void EliminatedArmyMembersUsePhysicalRegionAndPreserveEnemyDetention(bool neighbors, int destination)
    {
        var world = World(true);
        world.Replace(world.Settlements[3] with { Kind = SettlementKind.Facility });
        foreach (int id in new[] { 1, 11 })
            world.Replace(world.Officers[id] with { SettlementId = null, UnitId = 200, IsCommander = false });
        world.Replace(world.Officers[12] with { Status = OfficerStatus.Captive, SettlementId = 2, StatusStartYear = 180 });
        world.Replace(world.Officers[30] with { OwnerId = 2, Status = OfficerStatus.Captive, SettlementId = null, UnitId = 200 });
        world.AddUnit(new(200, 1, new(302, 2), 10000) { FormationId = 1, CorpsId = 10, ServicePlaceId = 10,
            CommanderId = 1, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(1, BattleRank.Front).Add(11, BattleRank.Middle) });
        var baseline = Rules();
        var rules = new SimulationRules { Military = baseline.Military,
            Personnel = new(baseline.Personnel.Places.Values, new Dictionary<int, int> { [3] = 10, [2] = 2 },
                neighbors ? [new(10, 2)] : []) };
        List<DomainEvent> events = [];
        BattleAftermath.Reconcile(world, new HashSet<int> { 1 }, rules, events);
        ResolutionPipeline.CheckConquest(world, events);
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
        foreach (int id in new[] { 1, 11 })
        {
            Assert.Equal(OfficerStatus.Free, world.Officers[id].Status);
            Assert.Equal(destination, world.Officers[id].SettlementId);
            Assert.Equal(190, world.Officers[id].StatusStartYear);
        }
        Assert.Null(world.Officers[12].OwnerId);
        Assert.Equal(OfficerStatus.Captive, world.Officers[12].Status);
        Assert.Equal(2, world.Officers[12].SettlementId);
        Assert.Equal(180, world.Officers[12].StatusStartYear);
        Assert.Equal(OfficerStatus.Serving, world.Officers[30].Status);
        Assert.Equal(2, world.Officers[30].OwnerId);
        Assert.Equal(2, world.Officers[30].SettlementId);
        Assert.Empty(world.Units);
    }

    private static SettlementState Place(int id, int owner, uint soldiers = 1000) =>
        new(id, owner, soldiers, 100, 100, 70, 500, 100, 1000, 100, 1000)
        { Kind = id == 10 ? SettlementKind.Facility : SettlementKind.City, CorpsId = owner * 10, ConsumesFood = false };

    private static OfficerState Person(int id, int owner, int place, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, place, new(60, 60, 60, 60), false)
        { Status = status, IsCommander = status == OfficerStatus.Ruler || id == 10, Ambition = 5 }
        .WithLoyalty(1, id == 12 ? 100 : 0).WithLoyalty(2, 255);

    private static WorldMutation World(bool occupied = false, ulong seed = 17)
    {
        var state = WorldState.Create(new(190, 1, 1), seed,
            [new(1, 100000, 200000, 100) { LeaderId = 1 }, new(2, 100000, 200000, 100) { LeaderId = 2 }],
            [Place(2, 2, 200000), Place(3, 1), Place(10, 1, occupied ? 1000u : 0u)],
            [Person(1, 1, 3, OfficerStatus.Ruler), Person(2, 2, 2, OfficerStatus.Ruler),
             Person(30, 2, 10) with { OwnerId = null, Status = OfficerStatus.Undiscovered },
             .. occupied ? new[] { Person(10, 1, 10), Person(11, 1, 10), Person(12, 1, 10) } : []],
            relations: [new(1, 2, 0)],
            corps: [new(10, 1, 1, null), new(20, 2, 2, null)],
            controls: [new("player", 1, ControllerKind.Player), new("other", 2, ControllerKind.Player)]);
        var world = state.BeginMutation();
        world.SetClock(new(190, 2, 1));
        world.SetSimulation(world.Simulation with { NextUnitId = 1000 });
        return world;
    }

    private static SimulationRules Rules(bool friendlyNeighbor = false) => new()
    {
        Personnel = new([new(2, new(102, 2)), new(3, new(302, 2)), new(10, new(52, 2))],
            new Dictionary<int, int> { [2] = 2, [3] = 3, [10] = 2 }, friendlyNeighbor ? [new(2, 3)] : []),
        Military = new(new(80, 1, Enumerable.Repeat(0, 80).ToImmutableArray(), [new(0, 1, null)],
            [new(1, 1, 0, 1000000, 5, new(1, 1), TravelMode.Land) { CaptureRanks = [BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle, BattleRank.Middle], Attack = new(10, true), Combat = new(10, 10, 10, 10, 50) }],
            [new(2, new(102, 2), new(1, 1)), new(3, new(302, 2), new(1, 1)),
             new(10, new(52, 2), new(1, 1)) { Combat = new(10, 10, 10, 10, 50) }])
            { RegionIds = Enumerable.Range(0, 80).Select(i => (int?)(i < 40 ? 2 : 3)).ToImmutableArray() })
    };

    private static List<DomainEvent> Apply(WorldMutation world, SimulationRules? rules = null)
    {
        List<DomainEvent> events = [];
        SettlementDefections.Apply(world, rules ?? Rules(), events);
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
        return events;
    }

    [Fact]
    public void EmptyFacilityFollowsCityWithoutRandomOrRecruitingHiddenPeople()
    {
        var world = World();
        world.Replace(world.Settlements[10] with { Wounded = 9 });
        world.Replace(Person(30, 1, 10) with { OwnerId = null, Status = OfficerStatus.Undiscovered });
        var result = Assert.Single(Apply(world).OfType<SettlementDefectedEvent>());
        Assert.Equal(SettlementDefectionKind.Abandoned, result.Kind);
        Assert.Equal((10, 1, 2), (result.SettlementId, result.PreviousForceId, result.ForceId));
        Assert.Null(result.CommanderId);
        Assert.Empty(result.FollowerIds);
        Assert.Empty(world.Simulation.RandomStreams);
        Assert.Equal(20, world.Settlements[10].CorpsId);
        Assert.Equal(9u, world.Settlements[10].Wounded);
        Assert.Equal(OfficerStatus.Undiscovered, world.Officers[30].Status);
        Assert.Empty(Apply(world).OfType<SettlementDefectedEvent>());
    }

    [Theory]
    [InlineData("soldiers")]
    [InlineData("city")]
    [InlineData("own-city")]
    [InlineData("neutral-city")]
    [InlineData("barbarian-city")]
    [InlineData("destroyed")]
    [InlineData("work")]
    [InlineData("other-holding")]
    [InlineData("field-force")]
    [InlineData("disabled")]
    [InlineData("too-early")]
    public void EmptyFacilityGatesDoNotRollOrChangeOwnership(string gate)
    {
        var world = World();
        switch (gate)
        {
            case "soldiers": world.Replace(world.Settlements[10] with { Soldiers = 1 }); break;
            case "city": world.Replace(world.Settlements[10] with { Kind = SettlementKind.City }); break;
            case "own-city": world.Replace(world.Settlements[2] with { OwnerId = 1, CorpsId = 10 }); break;
            case "neutral-city": world.Replace(world.Settlements[2] with { OwnerId = null, CorpsId = null }); break;
            case "barbarian-city": world.Replace(world.Forces[2] with { IsBarbarian = true }); break;
            case "destroyed": world.Replace(world.Settlements[10] with { IsDestroyed = true }); break;
            case "work": world.Replace(world.Settlements[10] with { CanHostPersons = false }); break;
            case "other-holding": world.AddSettlement(Place(11, 1) with { Kind = SettlementKind.Facility }); break;
            case "field-force": world.AddUnit(new UnitState(100, 1, new(2, 2), 1) { ServicePlaceId = 3 }); break;
            case "disabled": world.SetSimulation(world.Simulation with { SettlementDefectionsEnabled = false }); break;
            case "too-early": world.SetClock(new(190, 1, 30)); break;
        }
        var rules = Rules();
        if (gate == "other-holding") rules = new() { Military = rules.Military, Personnel = new(rules.Personnel.Places.Values.Append(new(11, new(62, 2))),
            rules.Personnel.RegionCities.Add(11, 2)) };
        // Some deliberately incomplete arrangements are eligibility probes, not committed worlds.
        List<DomainEvent> events = [];
        SettlementDefections.Apply(world, rules, events);
        Assert.Empty(events);
        Assert.Equal(1, world.Settlements[10].OwnerId);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void PersonnelUnitAndDistantFieldUnitDoNotBlockAbandonedFacility()
    {
        var world = World();
        world.AddUnit(new UnitState(100, 1, new(2, 2), 1) { IsPersonnel = true, ServicePlaceId = 10 });
        world.AddUnit(new UnitState(101, 1, new(302, 2), 1) { ServicePlaceId = 10 });
        Assert.Single(Apply(world).OfType<SettlementDefectedEvent>());
        Assert.Equal(1, world.Units[101].OwnerId);
    }

    [Fact]
    public void OccupiedFacilityTransfersFollowersAndReturnsLoyalPeopleWithoutChangingTroops()
    {
        var world = World(true);
        var before = world.Settlements[10];
        world.Replace(world.Officers[10] with { Injury = 3, TacticCooldown = 5 });
        var events = Apply(world);
        var result = Assert.Single(events.OfType<SettlementDefectedEvent>());
        Assert.Equal(SettlementDefectionKind.Isolated, result.Kind);
        Assert.Equal(new[] { 10, 11 }, result.FollowerIds);
        Assert.Equal(2, world.Officers[10].OwnerId);
        Assert.Equal(100, world.Officers[10].Loyalty);
        Assert.Equal(3, world.Officers[10].Injury);
        Assert.Equal(0, world.Officers[10].TacticCooldown);
        Assert.Equal(1, world.Officers[12].OwnerId);
        Assert.Equal(3, world.Officers[12].SettlementId);
        Assert.Equal(100, world.Officers[12].Loyalty);
        Assert.Contains(world.Simulation.Tasks, t => t.OfficerId == 12 && t.Kind == PersonnelReturns.Kind);
        Assert.Equal(before.Soldiers, world.Settlements[10].Soldiers);
        Assert.Equal(before.Morale, world.Settlements[10].Morale);
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.Reason == "settlement-defected");
    }

    [Theory]
    [InlineData("loyal-neighbor")]
    [InlineData("friendly-target")]
    [InlineData("ambition")]
    public void OccupiedEligibilityFailureDoesNotConsumeRandom(string gate)
    {
        var world = World(true);
        if (gate == "friendly-target") world.Replace(new ForceRelation(1, 2, 40));
        if (gate == "ambition") world.Replace(world.Officers[10] with { Ambition = 15 });
        Assert.Empty(Apply(world, Rules(gate == "loyal-neighbor")).OfType<SettlementDefectedEvent>());
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(49, true)]
    [InlineData(50, false)]
    public void ProbabilityUsesStrictBoundaryAndIntegerStrengthRatio(int roll, bool transfers)
    {
        var world = World(true);
        world.Replace(world.Settlements[2] with { Soldiers = 0 });
        world.Replace(world.Officers[10].WithLoyalty(1, 0).WithLoyalty(2, 100));
        for (ulong seed = 0; ; seed++)
        {
            var random = new StableRandom(seed);
            if (random.NextUInt64() % 100 != (ulong)roll) continue;
            world.SetSimulation(world.Simulation with { RandomStreams = [new(SettlementDefections.RandomStream, seed)] });
            break;
        }
        Assert.Equal(transfers, Apply(world).OfType<SettlementDefectedEvent>().Any());
    }

    [Fact]
    public void PrisonerRecoversAndFormerSubcorpsDissolves()
    {
        var world = World(true);
        world.AddCorps(new CorpsState(11, 1, 10, new(500, 700)));
        world.Replace(world.Settlements[10] with { CorpsId = 11 });
        world.Replace(Person(30, 2, 10, OfficerStatus.Captive));
        var events = Apply(world);
        Assert.Single(events.OfType<PrisonerDisplacedEvent>());
        Assert.Equal(OfficerStatus.Serving, world.Officers[30].Status);
        Assert.Equal(2, world.Officers[30].OwnerId);
        Assert.False(world.Corps.ContainsKey(11));
    }

    private static void Attackers(WorldMutation world, int count)
    {
        for (int i = 0; i < count; i++) world.AddUnit(new UnitState(100 + i, 2, new(47 + i * 10, 2), 100000)
        { FormationId = 1, ServicePlaceId = 2, CorpsId = 20, Morale = 100,
            Order = new(MilitaryOrderKind.AttackSettlement, null, 10) });
    }

    [Theory]
    [InlineData(1, false)]
    [InlineData(2, true)]
    public void SiegeRequiresTwoNearbyAttackersWhenTerritorialRouteIsBlocked(int count, bool succeeds)
    {
        var world = World(true);
        Attackers(world, count);
        var events = Apply(world, Rules(true));
        Assert.Equal(succeeds, events.OfType<SettlementDefectedEvent>().Any());
        if (succeeds)
        {
            Assert.Equal(SettlementDefectionKind.Besieged, Assert.Single(events.OfType<SettlementDefectedEvent>()).Kind);
            Assert.All(world.Units.Values, u => Assert.Null(u.Order));
        }
    }

    [Fact]
    public void FriendlyFieldSupportBlocksSiegeEvenWhenItsServicePlaceIsElsewhere()
    {
        var world = World(true);
        Attackers(world, 2);
        world.AddUnit(new UnitState(200, 1, new(97, 2), 1) { ServicePlaceId = 3 });
        Assert.Empty(Apply(world, Rules(true)).OfType<SettlementDefectedEvent>());
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void TravelingOfficerDoesNotFollowAndReturnsToRemainingHome()
    {
        var world = World(true);
        var route = PersonnelLocations.Route(world, Rules().Personnel, 3, 10);
        var cause = new PersistentTask(Guid.NewGuid(), PersonnelReturns.Kind, "player", 1, 11, 3,
            world.Clock, world.Clock, 0, 0);
        PersonnelReturns.Schedule(world, cause, 11, 3, 10, route.Days, false, [], route);
        var events = Apply(world);
        Assert.DoesNotContain(11, Assert.Single(events.OfType<SettlementDefectedEvent>()).FollowerIds);
        Assert.Equal(1, world.Officers[11].OwnerId);
        Assert.Equal(3, world.Officers[11].SettlementId);
        Assert.Single(world.Simulation.Tasks.Where(t => t.OfficerId == 11));
    }

    [Theory]
    [InlineData(39, true)]
    [InlineData(40, false)]
    public void AbandonedFacilityDoesNotRequireHostilityButOccupiedOneDoes(int relation, bool occupiedChanges)
    {
        var occupied = World(true);
        occupied.Replace(new ForceRelation(1, 2, relation));
        Assert.Equal(occupiedChanges, Apply(occupied).OfType<SettlementDefectedEvent>().Any());
        var empty = World();
        empty.Replace(new ForceRelation(1, 2, relation));
        Assert.Single(Apply(empty).OfType<SettlementDefectedEvent>());
    }

    [Theory]
    [InlineData(false, false)] [InlineData(true, false)]
    [InlineData(false, true)] [InlineData(true, true)]
    public void RulerInLastCityNeverDefectsThroughIsolationOrSiege(bool player, bool siege)
    {
        var world = World(true);
        world.Replace(world.Settlements[3] with { OwnerId = 2, CorpsId = 20 });
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.City });
        world.Replace(world.Officers[1] with { SettlementId = 10, IsCommander = true, Ambition = 0 });
        world.Replace(world.Officers[10] with { IsCommander = false });
        world.Replace(world.Forces[2] with { Trust = 1000 });
        world.SetSimulation(world.Simulation with { Controls = [new("owner", 1, player ? ControllerKind.Player : ControllerKind.Automatic),
            new("other", 2, ControllerKind.Player)] });
        Attackers(world, 2);
        if (siege) world.Replace(world.Settlements[3] with { OwnerId = null, CorpsId = null });
        world.AddSettlement(Place(11, 2, 200000) with { Kind = SettlementKind.Facility });
        var baseline = Rules();
        var rules = new SimulationRules { Military = baseline.Military, Personnel = new(baseline.Personnel.Places.Values.Append(new(11, new(62, 2))),
            new Dictionary<int, int> { [2] = 2, [3] = 3, [10] = 10, [11] = 10 }, siege ? [new(10, 3)] : []) };
        Assert.Empty(Apply(world, rules).OfType<SettlementDefectedEvent>());
        Assert.Equal(1, world.Settlements[10].OwnerId);
        Assert.False(world.Forces[1].IsEliminated);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void OrdinaryCommanderDefectingWithLastCityEliminatesRulersRemainingArmyAndFacility()
    {
        var world = World(true);
        world.Replace(world.Settlements[10] with { Kind = SettlementKind.City });
        world.Replace(world.Settlements[3] with { Kind = SettlementKind.Facility });
        world.Replace(world.Officers[1] with { SettlementId = null, UnitId = 200, IsCommander = false });
        world.AddUnit(new(200, 1, new(302, 2), 10000) { FormationId = 1, CorpsId = 10, ServicePlaceId = 3,
            CommanderId = 1, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(1, BattleRank.Front) });
        world.AddSettlement(Place(11, 2, 200000) with { Kind = SettlementKind.Facility });
        var baseline = Rules();
        var rules = new SimulationRules { Military = baseline.Military, Personnel = new(baseline.Personnel.Places.Values.Append(new(11, new(62, 2))),
            new Dictionary<int, int> { [2] = 2, [3] = 3, [10] = 10, [11] = 10 }) };
        var events = Apply(world, rules);
        Assert.Equal(10, Assert.Single(events.OfType<SettlementDefectedEvent>()).CommanderId);
        Assert.Equal(1, Assert.Single(events.OfType<ForceEliminatedEvent>()).ForceId);
        Assert.Equal(2, world.Settlements[10].OwnerId);
        Assert.Null(world.Settlements[3].OwnerId);
        Assert.False(world.Units.ContainsKey(200));
        Assert.Null(world.Officers[1].OwnerId);
        Assert.Equal(3, world.Officers[1].SettlementId);
        Assert.Equal(world.Clock.Year, world.Officers[1].StatusStartYear);
    }
}
