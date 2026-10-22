using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CityIndependenceTests
{
    private static SettlementState City(int id, int owner, int corps) => new(id, owner, 12345, 100, 100, 73, 600, 400, 1000, 500, 1000)
    { Kind = SettlementKind.City, CorpsId = corps, DraftPopulation = 100000, Population = 200000, Wounded = 17, ConsumesFood = false };

    private static OfficerState Person(int id, int owner, int home, int loyalty, OfficerStatus status = OfficerStatus.Serving) =>
        new OfficerState(id, owner, home, new(50, 50, 50, 50), false)
        { Status = status, Ambition = 15, IsCommander = status is OfficerStatus.Ruler or OfficerStatus.Governor }.WithLoyalty(owner, loyalty);

    private static WorldState World(ulong seed = 17) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 100) { LeaderId = 1 }, new(2, 100) { LeaderId = 20 }],
        [City(1, 1, 11), City(2, 1, 10), City(3, 2, 20)],
        [Person(1, 1, 2, 100, OfficerStatus.Ruler), Person(10, 1, 1, 0, OfficerStatus.Governor),
         Person(11, 1, 1, 0), Person(12, 1, 1, 255), Person(20, 2, 3, 100, OfficerStatus.Ruler)],
        corps: [new(10, 1, 1, new(1000, 2000)) { IsRoot = true }, new(11, 1, 10, new(500, 700)), new(20, 2, 20, new(2000, 3000)) { IsRoot = true }],
        controls: [new("player", 1, ControllerKind.Player), new("ai:2", 2, ControllerKind.Automatic)],
        freeOfficerMigrationEnabled: false, officeRequestsEnabled: false, forceEmergenceEnabled: false,
        annualAppearancesEnabled: false, naturalDeathsEnabled: false);

    private static SimulationRules Rules(bool neighbors = true) => new()
    {
        Personnel = new(Enumerable.Range(1, 3).Select(i => new PersonPlace(i, new(i * 30, 0))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }, neighbors ? [new(1, 2)] : [])
    };

    private static WorldMutation Due(WorldState state)
    {
        var world = state.BeginMutation();
        world.SetClock(new(190, 2, 1));
        return world;
    }

    [Fact]
    public void IndependenceCreatesForceWithoutRecruitRewardsAndSettlesOldCorpsTreasury()
    {
        var state = World();
        var world = Due(state);
        var oldCity = world.Settlements[1];
        // A second direct holding, rather than the root corps city, receives the loyal person.
        world.Replace(world.Settlements[2] with { CorpsId = 11 });
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        var result = Assert.Single(events.OfType<CityIndependenceEvent>());
        Assert.Equal((1, 1, 3, 10), (result.SettlementId, result.PreviousForceId, result.ForceId, result.FounderId));
        Assert.Equal(new[] { 10, 11 }, result.MemberIds);
        Assert.Equal(ForceCreationKind.CityIndependence, world.Forces[3].Origin!.Kind);
        Assert.Equal(1, world.Forces[3].Origin!.PredecessorId);
        Assert.Equal(oldCity.Soldiers, world.Settlements[1].Soldiers);
        Assert.Equal(oldCity.DraftPopulation, world.Settlements[1].DraftPopulation);
        Assert.Equal(oldCity.Morale, world.Settlements[1].Morale);
        Assert.Equal(oldCity.Wounded, world.Settlements[1].Wounded);
        Assert.Equal(2 * EconomyMath.Revenue(oldCity) + 3000, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 3).Treasury.Money);
        // The surviving child owns the old force's only remaining city, so it is merged into the empty root.
        Assert.Equal(1500u, world.Corps[10].Treasury.Money);
        Assert.Equal(2700u, world.Corps[10].Treasury.Food);
        Assert.False(world.Corps.ContainsKey(11));
        Assert.Equal(10, world.Settlements[2].CorpsId);
        Assert.Contains(events.OfType<CorpsDissolvedEvent>(), e => e.CorpsId == 11 && e.DestinationCorpsId == 10);
        Assert.Equal(2, world.Officers[12].SettlementId);
        Assert.Equal(1, world.Officers[12].OwnerId);
        Assert.Equal(255, world.Officers[12].Loyalty);
        Assert.Equal(45, world.Officers[12].LoyaltyTo(3));
        Assert.Equal(12, Assert.Single(world.Simulation.Tasks).OfficerId);
        Assert.Equal(world.Clock, Assert.Single(world.Simulation.Tasks).Due);
        Assert.False(Assert.Single(world.Simulation.Tasks).Journey!.ReturnLeg!.AfterDailyCommands);
        Assert.InRange(world.Relations[(1, 3)].Value, 10, 19);
        Assert.Equal(50, world.Relations[(2, 3)].Value);
        state.Commit(world);
    }

    [Fact]
    public void NoOtherDirectHoldingDetachesLoyalPersonRatherThanMovingToAnotherCorps()
    {
        var state = World();
        var world = Due(state);
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.Single(events.OfType<CityIndependenceEvent>());
        var departed = Assert.Single(events.OfType<OfficerDepartedEvent>());
        Assert.True(departed.ServicePlaceLost);
        Assert.Equal((12, 1), (departed.OfficerId, departed.DestinationId));
        Assert.Equal(OfficerStatus.Free, world.Officers[12].Status);
        Assert.Null(world.Officers[12].OwnerId);
        Assert.Equal(45, world.Officers[12].LoyaltyTo(1));
        Assert.Equal(1500u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Equal(2700u, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Food);
        Assert.False(world.Corps.ContainsKey(11));
        Assert.Contains(events.OfType<ResourceChangedEvent>(), e => e.Reason == "city-independence-corps-dissolved");
        state.Commit(world);
    }

    [Theory]
    [InlineData(29, true)]
    [InlineData(30, false)]
    public void TimingAndSwitchDoNotConsumeRandom(int days, bool enabled)
    {
        var world = Due(World());
        world.SetClock(days == 29 ? new(190, 1, 30) : new(190, 2, 1));
        world.SetSimulation(world.Simulation with { CityIndependenceEnabled = enabled });
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    private static ulong SeedFor(int roll) => TestSeeds.ForNamedRoll("event-city-independence", roll);

    [Theory]
    [InlineData(48, true)]
    [InlineData(49, false)]
    public void StrictProbabilityBoundaryAndFacilityDeparture(int roll, bool leaves)
    {
        var world = Due(World(SeedFor(roll)));
        world.Replace(world.Settlements[1] with { Kind = SettlementKind.Facility });
        world.Replace(world.Officers[10].WithLoyalty(1, 40) with { Ambition = 15, Righteousness = 5 }); // threshold 49
        world.Replace(world.Officers[11].WithLoyalty(1, 255));
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        Assert.Empty(events.OfType<CityIndependenceEvent>());
        Assert.Equal(leaves, events.OfType<OfficerDepartedEvent>().Any());
        Assert.Equal(leaves ? OfficerStatus.Free : OfficerStatus.Governor, world.Officers[10].Status);
        Assert.Equal(40, world.Officers[10].LoyaltyTo(1));
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void FailedFormationWithEmptyPlacementLeavesPersonAndOccupationUntouched()
    {
        var world = Due(World());
        world.Replace(world.Settlements[1] with { Kind = SettlementKind.Facility });
        var before = world.Officers[10];
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.Empty(events);
        Assert.Equal(before, world.Officers[10]);
        Assert.DoesNotContain(world.Simulation.RandomStreams, s => s.Name.Contains("placement"));
    }

    [Fact]
    public void OrdinarySuccessDoesNotReplaceTheSnapshotCommanderAndStartAForce()
    {
        var world = Due(World());
        world.Replace(world.Officers[10].WithLoyalty(1, 255));
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        Assert.Empty(events.OfType<CityIndependenceEvent>());
        Assert.Equal(11, Assert.Single(events.OfType<OfficerDepartedEvent>()).OfficerId);
        Assert.Equal(1, world.Settlements[1].OwnerId);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void PrisonersReturnToOriginalForceWithoutReleaseLoyaltyReward()
    {
        var initial = World().CreateSnapshot();
        var prisoner = Person(30, 2, 1, 100, OfficerStatus.Captive).WithLoyalty(1, 20) with { Righteousness = 15 };
        var state = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements,
            initial.Officers.Append(prisoner), corps: initial.Corps, controls: initial.Simulation.Controls);
        var world = Due(state);
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        Assert.Equal(OfficerStatus.Serving, world.Officers[30].Status);
        Assert.Equal(2, world.Officers[30].OwnerId);
        Assert.Equal(3, world.Officers[30].SettlementId);
        Assert.Equal(20, world.Officers[30].LoyaltyTo(1));
        Assert.Equal(50, world.Officers[30].LoyaltyTo(3));
        Assert.Single(events.OfType<PrisonerDisplacedEvent>());
        Assert.Contains(world.Simulation.Tasks, t => t.OfficerId == 30 && t.Kind == PersonnelReturns.Kind);
        state.Commit(world);
    }

    [Fact]
    public void UnaffiliatedPrisonerWithNoDestinationRemainsInNewOwnersCustody()
    {
        var initial = World().CreateSnapshot();
        var prisoner = new OfficerState(30, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive };
        var state = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements,
            initial.Officers.Append(prisoner), corps: initial.Corps, controls: initial.Simulation.Controls);
        var world = Due(state);
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.Equal(OfficerStatus.Captive, world.Officers[30].Status);
        Assert.Null(world.Officers[30].OwnerId);
        Assert.Equal(1, world.Officers[30].SettlementId);
        Assert.Equal(3, world.Settlements[1].OwnerId);
        Assert.Empty(events.OfType<PrisonerDisplacedEvent>());
        state.Commit(world);
    }

    [Fact]
    public void TravelingServiceOfficerIsNotMarkedAndContinuesToHisValidDestination()
    {
        var state = World();
        var world = Due(state);
        world.Replace(world.Officers[12].WithLoyalty(1, 0));
        Assert.Equal(CommandRejection.None, PersonnelMissions.StartMove(world, new("player"),
            new(Guid.NewGuid(), 1, 0, [12], 2), Rules(false).Personnel, null, []));
        world.SetSimulation(world.Simulation with { NextSequence = 1 });
        var task = Assert.Single(world.Simulation.Tasks);
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.Equal(new[] { 10, 11 }, Assert.Single(events.OfType<CityIndependenceEvent>()).MemberIds);
        Assert.Equal(1, world.Officers[12].OwnerId);
        Assert.Equal(2, world.Officers[12].SettlementId);
        Assert.Equal(task, Assert.Single(world.Simulation.Tasks));
        Assert.DoesNotContain(events.OfType<TaskCancelledEvent>(), e => e.TaskId == task.Id);
        WorldInvariants.Validate(world);
        state.Commit(world);
    }

    [Fact]
    public void LastHoldingLossRetiresPredecessorAndPreservesOriginAndPrisonerIdentity()
    {
        var world = Due(World());
        world.Replace(world.Settlements[2] with { OwnerId = 2, CorpsId = 20 });
        world.Replace(world.Officers[1] with { Status = OfficerStatus.Captive, SettlementId = 3, IsCommander = false });
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.True(world.Forces[1].IsEliminated);
        Assert.Equal(1, world.Forces[3].Origin!.PredecessorId);
        Assert.Equal(OfficerStatus.Captive, world.Officers[1].Status);
        Assert.Null(world.Officers[1].OwnerId);
        Assert.DoesNotContain(world.Simulation.Controls, c => c.ForceId == 1);
        Assert.DoesNotContain(world.Corps.Values, c => c.OwnerId == 1);
        Assert.Equal("player-defeated", world.Simulation.TerminalReason);
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData("barbarian")]
    [InlineData("non-hosting")]
    [InlineData("ruler")]
    public void ExcludedPopulationDoesNotRoll(string exclusion)
    {
        var world = Due(World());
        if (exclusion == "barbarian") world.Replace(world.Forces[1] with { IsBarbarian = true });
        if (exclusion == "non-hosting") world.Replace(world.Settlements[1] with { CanHostPersons = false });
        if (exclusion == "ruler")
        {
            foreach (int id in new[] { 10, 11, 12 }) world.Replace(world.Officers[id] with { Status = OfficerStatus.Free, OwnerId = null });
        }
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(), events);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.RandomStreams);
    }

    [Fact]
    public void EachCityCanFormAForceInTheSameXun()
    {
        var world = Due(World());
        world.Replace(world.Officers[1] with { SettlementId = 3, Status = OfficerStatus.Captive, IsCommander = false });
        world.Replace(world.Officers[12].WithLoyalty(1, 0) with { SettlementId = 2, IsCommander = true });
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.Equal(new[] { 1, 2 }, events.OfType<CityIndependenceEvent>().Select(e => e.SettlementId));
        Assert.Equal(new[] { 3, 4 }, events.OfType<CityIndependenceEvent>().Select(e => e.ForceId));
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void PredecessorExitCancelsBothDiplomaticDirectionsAndRefundsIncomingGift()
    {
        var initial = World().CreateSnapshot();
        var state = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements,
            initial.Officers.Select(p => p.Id == 12 ? p with { SettlementId = 2 } : p).Append(Person(21, 2, 3, 100)),
            corps: initial.Corps.Select(c => c.OwnerId == 1 && c.IsRoot
                ? c with { Treasury = c.Treasury with { Money = 2000 } } : c), controls: initial.Simulation.Controls);
        var engine = new SimulationEngine(state, Rules());
        var outgoing = new GiftCommand(Guid.NewGuid(), 1, 0, 12, 2, new(GiftKind.Money, 1000, null));
        Assert.True(engine.Execute(new("player"), outgoing).Submission.Accepted);
        engine.EndControl(new("player"));
        uint incomingForceMoney = engine.Snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money;
        var incoming = new GiftCommand(Guid.NewGuid(), 2, 1, 21, 1, new(GiftKind.Money, 1000, null));
        Assert.True(engine.Execute(new("ai:2"), incoming).Submission.Accepted);
        var world = Due(state);
        world.Replace(world.Settlements[2] with { OwnerId = 2, CorpsId = 20 });
        world.Replace(world.Officers[1] with { Status = OfficerStatus.Captive, SettlementId = 3, IsCommander = false });
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, Rules(false), events);
        Assert.True(world.Forces[1].IsEliminated);
        Assert.Equal(new[] { outgoing.CommandId, incoming.CommandId }.Order(), events.OfType<TaskCancelledEvent>().Select(e => e.TaskId).Order());
        Assert.DoesNotContain(world.Simulation.Tasks, t => t.Gift is not null || t.ForceId == 1);
        Assert.Equal(incomingForceMoney, world.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money);
        ResolutionPipeline.CheckTerminal(world, events);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void DepartureFromACityDoesNotRequireAnAuthoredSelfMapping()
    {
        var world = Due(World());
        world.Replace(world.Officers[10].WithLoyalty(1, 255));
        var rules = new SimulationRules { Personnel = new(Rules().Personnel.Places.Values,
            new Dictionary<int, int> { [101] = 1, [102] = 2 }, [new(1, 2)]) };
        List<DomainEvent> events = [];
        CityIndependence.Apply(world, rules, events);
        Assert.Equal(2, Assert.Single(events.OfType<OfficerDepartedEvent>()).DestinationId);
        WorldInvariants.Validate(world);
    }
}
