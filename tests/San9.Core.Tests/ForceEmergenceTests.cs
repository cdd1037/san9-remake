using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceEmergenceTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void QuarterFoundingCanWaitForStudyAndResumeWithoutRepeatingUpdates(bool independence)
    {
        var state = World();
        var baseline = Rules();
        var rules = new SimulationRules
        {
            Personnel = new(baseline.Personnel.Places.Values, baseline.Personnel.RegionCities,
                baseline.Personnel.CityConnections, tacticCourses: [new(2, 9000)]),
            Military = new(new(6, 2, Enumerable.Repeat(0, 12).ToImmutableArray(), [new(0, 1, null)], [], [])
                { Tactics = [new(9000, 0, TacticEffect.CounterConfusion, 0, 0, 0, false, false, [])] }),
            Governance = baseline.Governance
        };
        var world = state.BeginMutation(rules.Abilities);
        world.SetClock(new(190, 4, 1));
        world.SetSimulation(world.Simulation with { ActiveControl = world.Simulation.Controls.Length });
        if (independence)
        {
            world.Replace(world.Settlements[1] with { OwnerId = 7 });
            world.Replace(world.Officers[1] with { OwnerId = 7, Status = OfficerStatus.Serving, Ambition = 15 });
        }
        // A last-xun action must not exclude a physically present person from the new quarter's course.
        world.Replace(world.Officers[70] with { Acted = true });
        List<DomainEvent> events = [];
        new ResolutionPipeline(rules).Prepare(world, events);
        Assert.Single(events, e => e is ForceFoundedEvent or CityIndependenceEvent);
        Assert.NotNull(world.Simulation.PendingTacticStudy);
        state.Commit(world);
        var engine = new SimulationEngine(state, rules);
        var answer = engine.Execute(new("player"), new ChooseTacticStudentCommand(Guid.NewGuid(), 7,
            engine.Snapshot.Simulation.NextSequence, 2, 70));
        Assert.True(answer.Submission.Accepted);
        Assert.Equal(SimulationBoundary.Stable, answer.Snapshot.Simulation.Boundary);
        Assert.DoesNotContain(answer.Events, e => e is ForceFoundedEvent or CityIndependenceEvent);
        Assert.All(answer.Snapshot.Officers.Where(p => p.IsServing), p => Assert.False(p.Acted));
        Assert.Equal(70, Assert.Single(answer.Snapshot.Simulation.Tasks).OfficerId);
        Assert.False(engine.Execute(new("player"), new SearchCommand(Guid.NewGuid(), 7,
            engine.Snapshot.Simulation.NextSequence, 70, 2)).Submission.Accepted);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }

    private static OfficerState Free(int id, int place, int ambition = 0, int affinity = 0) =>
        new(id, null, place, new(50, 50, 50, 50), false)
        { Status = OfficerStatus.Free, Ambition = ambition, Affinity = affinity };

    private static SettlementState City(int id, int? owner = null) => new(id, owner, 3000, 100, 100, 50, 600, 500, 1000, 600, 1000)
    { Kind = SettlementKind.City, Population = 300000, DraftPopulation = 100000, ConsumesFood = false };

    private static WorldState World() => WorldState.Create(new(190, 1, 1), 17,
        [new(7, 0, 0, 100) { LeaderId = 70 }], [City(1), City(2, 7), City(3)],
        [new(70, 7, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         Free(1, 1, 11), Free(2, 2, 0, 10), Free(3, 2, 0, 149), Free(4, 1, 0, 30)],
        controls: [new("player", 7, ControllerKind.Player)], freeOfficerMigrationEnabled: false,
        officeRequestsEnabled: false, annualAppearancesEnabled: false, naturalDeathsEnabled: false);

    private static SimulationRules Rules(params CityConnection[] edges) => new()
    {
        Personnel = new(Enumerable.Range(1, 4).Select(i => new PersonPlace(i, new(i * 5, 5))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 2 }, edges.Length == 0 ? [new(1, 2)] : edges),
        Governance = new([], new Dictionary<int, int> { [500] = 0, [2] = 1 })
    };

    private static WorldMutation Due(WorldState state)
    {
        var world = state.BeginMutation();
        world.SetClock(new(190, 2, 1));
        return world;
    }

    [Fact]
    public void FoundingTransfersOnlySelectedPeopleAndCityWithExactInitialization()
    {
        var state = World();
        var world = Due(state);
        var before = world.Settlements[1];
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        var founded = Assert.Single(events.OfType<ForceFoundedEvent>());
        Assert.Equal((8, 1, 1), (founded.ForceId, founded.SettlementId, founded.FounderId));
        Assert.Equal(new[] { 1, 2, 3 }, founded.MemberIds);
        var force = world.Forces[8];
        Assert.Equal(new(ForceCreationKind.EmptyCityUprising, 1, 1, world.Clock), force.Origin);
        Assert.Equal(500, force.RankId);
        Assert.Equal(100, force.BaseTrust);
        Assert.Equal(2 * EconomyMath.Revenue(before) + 3000, force.Money);
        Assert.Equal(2 * EconomyMath.Harvest(before) + 30000, force.Food);
        Assert.Equal(20000u, world.Settlements[1].Soldiers);
        Assert.Equal(90000u, world.Settlements[1].DraftPopulation);
        Assert.Equal(EconomyMath.RecruitMorale(force.Trust), world.Settlements[1].Morale);
        Assert.Equal(7, world.Settlements[2].OwnerId);
        Assert.Equal(OfficerStatus.Free, world.Officers[4].Status);
        Assert.Null(world.Officers[4].OwnerId);
        Assert.False(world.Officers[4].ForceLoyalties.ContainsKey(8));
        Assert.Equal(100, world.Officers[1].Loyalty);
        Assert.Equal(OfficerStatus.Ruler, world.Officers[1].Status);
        Assert.True(world.Officers[1].IsCommander);
        Assert.Equal(50, world.Relations[(7, 8)].Value);
        Assert.False(world.Officers[70].ForceLoyalties.ContainsKey(8));
        Assert.Equal(new("ai:8", 8, ControllerKind.Automatic), world.Simulation.Controls[^1]);
        Assert.Equal(8, Assert.Single(world.Corps.Values).OwnerId);
        Assert.All(founded.MemberIds, id => Assert.Equal(1, world.Officers[id].SettlementId));
        Assert.Single(world.Simulation.RandomStreams);
        state.Commit(world);
    }

    [Theory]
    [InlineData("early")]
    [InlineData("disabled")]
    [InlineData("owned")]
    [InlineData("ambition")]
    [InlineData("support")]
    [InlineData("destroyed")]
    public void IneligibleWorldHasNoEffectsOrRandomDraws(string reason)
    {
        var world = Due(World());
        if (reason == "early") world.SetClock(new(190, 1, 30));
        if (reason == "disabled") world.SetSimulation(world.Simulation with { ForceEmergenceEnabled = false });
        if (reason == "owned") world.Replace(world.Settlements[1] with { OwnerId = 7 });
        if (reason == "ambition") world.Replace(world.Officers[1] with { Ambition = 9 });
        if (reason == "support") world.Replace(world.Officers[2] with { Affinity = 11 });
        if (reason == "destroyed") world.Replace(world.Settlements[1] with { IsDestroyed = true });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Empty(events);
        Assert.Empty(world.Simulation.RandomStreams);
        Assert.Single(world.Forces);
    }

    [Fact]
    public void SupporterCanBecomeFounderWithoutMovingTheTargetCity()
    {
        var world = Due(World());
        world.Replace(world.Officers[2] with { ForceFounderPriority = 1 });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        var result = Assert.Single(events.OfType<ForceFoundedEvent>());
        Assert.Equal(2, result.FounderId);
        Assert.Equal(1, result.SettlementId);
        Assert.Equal(100, world.Officers[2].Loyalty);
        WorldInvariants.Validate(world);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OnlyDirectOutgoingNeighborsSupplySupporters(bool secondHop)
    {
        var world = Due(World());
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, secondHop ? Rules(new(1, 3), new(3, 2)) : Rules(new CityConnection(2, 1)), events);
        Assert.Empty(events);
    }

    [Fact]
    public void UnbuiltAssociatedRegionSuppliesSupportersAndPriorityBypassesAmbition()
    {
        var world = Due(World());
        world.Replace(world.Officers[1] with { Ambition = 0, ForceFounderPriority = 5 });
        world.Replace(world.Officers[2] with { SettlementId = null, RegionId = 4 });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Single(events.OfType<ForceFoundedEvent>());
        Assert.Null(world.Officers[2].RegionId);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void CapacityCapsAssignedSoldiersButNotDraftDeductionAndPreservesWounded()
    {
        var world = Due(World());
        world.Replace(world.Settlements[1] with { SoldierCapacity = 5000, Wounded = 123 });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Equal(5000u, world.Settlements[1].Soldiers);
        Assert.Equal(90000u, world.Settlements[1].DraftPopulation);
        Assert.Equal(123u, world.Settlements[1].Wounded);
        Assert.Equal(15000, events.OfType<ResourceChangedEvent>().Single(e => e.Resource == ResourceKind.Soldiers).SaturationLoss);
        WorldInvariants.Validate(world);
    }

    [Fact]
    public void AllocationExceedsSourceCapacityAndDoesNotReuseRetiredIdentities()
    {
        var initial = World().CreateSnapshot();
        var state = WorldState.Create(initial.Clock, 17,
            initial.Forces.Concat(Enumerable.Range(100, 100).Select(id => new ForceState(id, 0, 0, 0) { IsEliminated = true })),
            initial.Settlements, initial.Officers);
        var world = Due(state);
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Equal(200, Assert.Single(events.OfType<ForceFoundedEvent>()).ForceId);
        Assert.Equal(102, world.Forces.Count);
        state.Commit(world);
    }

    [Fact]
    public void StableOrderingIsIndependentOfCollectionInsertionAndConsumesOnlyMemberLoyalty()
    {
        var first = Due(World());
        var initial = World().CreateSnapshot();
        var second = Due(WorldState.Create(initial.Clock, 17, initial.Forces.Reverse(), initial.Settlements.Reverse(), initial.Officers.Reverse(), controls: initial.Simulation.Controls));
        List<DomainEvent> a = [], b = [];
        ForceEmergence.Apply(first, Rules(), a);
        ForceEmergence.Apply(second, Rules(), b);
        Assert.Equal(CanonicalJson.Text(a), CanonicalJson.Text(b));
        var random = new StableRandom(StableRandom.DeriveSeed(17, "event-force-emergence-loyalty"));
        Assert.Equal(90 + (int)(random.NextUInt64() % 5), first.Officers[2].Loyalty);
        Assert.Equal(95 + (int)(random.NextUInt64() % 5), first.Officers[3].Loyalty);
        Assert.Equal(random.State, Assert.Single(first.Simulation.RandomStreams).State);
    }

    [Fact]
    public void InitialCandidatesAreSortedBeforeGroupingAndOnlyOneGroupStartsEachXun()
    {
        var initial = World().CreateSnapshot();
        var state = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements,
            initial.Officers.Concat([Free(10, 3, 12), Free(11, 3), Free(12, 3)]));
        var world = Due(state);
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        var result = Assert.Single(events.OfType<ForceFoundedEvent>());
        Assert.Equal((3, 10), (result.SettlementId, result.FounderId));
        Assert.Null(world.Settlements[1].OwnerId);
        world.SetClock(new(190, 2, 11));
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Equal(new[] { 8, 9 }, events.OfType<ForceFoundedEvent>().Select(e => e.ForceId));
        state.Commit(world);
    }

    [Fact]
    public void EqualPrioritiesUseIdentityRatherThanAmbitionAndInitializeOtherServingLoyalty()
    {
        var initial = World().CreateSnapshot();
        var people = initial.Officers.Concat([
            new OfficerState(71, 7, 2, new(50, 50, 50, 50), false),
            new OfficerState(72, 7, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Captive },
            Free(73, 1) with { Status = OfficerStatus.NotAppeared }]);
        var state = WorldState.Create(initial.Clock, 17, initial.Forces, initial.Settlements, people);
        var world = Due(state);
        world.Replace(world.Officers[1] with { ForceFounderPriority = 7, Ambition = 0 });
        world.Replace(world.Officers[2] with { ForceFounderPriority = 7, Ambition = 15 });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Equal(1, Assert.Single(events.OfType<ForceFoundedEvent>()).FounderId);
        Assert.Equal(50, world.Officers[71].LoyaltyTo(8));
        Assert.Equal(50, world.Officers[72].LoyaltyTo(8));
        Assert.False(world.Officers[73].ForceLoyalties.ContainsKey(8));
        state.Commit(world);
    }

    [Fact]
    public void SelectedRecruitmentTargetCancelsMissionAndReturnsItsExecutor()
    {
        var state = World();
        var world = Due(state);
        var task = new PersistentTask(Guid.Parse("77777777-7777-7777-7777-777777777777"), PersonnelMissions.RecruitKind,
            "player", 7, 70, 2, world.Clock, world.Clock.NextDay(), 0, 0)
        { Journey = new(1, 1, 0) { Route = new(new(10, 5), new(5, 5), false) } };
        world.SetSimulation(world.Simulation with { Tasks = [task], NextSequence = 1 });
        world.Replace(world.Officers[2] with { TacticCooldown = 5, OfficeId = 900, Injury = 3 });
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, Rules(), events);
        Assert.Equal(task.Id, Assert.Single(events.OfType<TaskCancelledEvent>()).TaskId);
        var returning = Assert.Single(world.Simulation.Tasks);
        Assert.Equal(PersonnelReturns.Kind, returning.Kind);
        Assert.Equal(70, returning.OfficerId);
        Assert.Equal(2, returning.Journey!.DestinationPlaceId);
        Assert.Null(world.Officers[2].OfficeId);
        Assert.Equal(0, world.Officers[2].TacticCooldown);
        Assert.Equal(3, world.Officers[2].Injury);
        state.Commit(world);
    }

    [Fact]
    public void CityIdentityDoesNotRequireARedundantRegionMapping()
    {
        var world = Due(World());
        var rules = new SimulationRules
        {
            Personnel = new(Enumerable.Range(1, 3).Select(i => new PersonPlace(i, new(i * 5, 5))),
                new Dictionary<int, int> { [101] = 1, [102] = 2 }, [new(1, 2)])
        };
        List<DomainEvent> events = [];
        ForceEmergence.Apply(world, rules, events);
        Assert.Single(events.OfType<ForceFoundedEvent>());
    }
}
