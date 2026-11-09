using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class NaturalMortalityTests
{
    private static OfficerState Person(int id, int? owner, int? home, int birth, OfficerStatus status = OfficerStatus.Serving) =>
        new(id, owner, home, new(70, 70, 70, 70), false) { BirthYear = birth, Status = status };

    private static WorldState World(bool enabled = true, IEnumerable<OfficerState>? others = null, IEnumerable<TreasureState>? treasures = null) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 0) { LeaderId = 1 }, new(2, 0) { LeaderId = 2 }],
        [new(10, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(20, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(30, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        new[] { Person(1, 1, 10, 90, OfficerStatus.Ruler), Person(2, 2, 30, 170, OfficerStatus.Ruler),
         Person(11, 1, 20, 170) with { ParentId = 1, BloodlineId = 1 }, Person(12, 1, 10, 160) with { Affinity = 31 } }.Concat(others ?? []),
        naturalDeathsEnabled: enabled, treasures: treasures, corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(1000, 1000)) { IsRoot = true }]);

    private static SimulationEngine BeforeAnnualBoundary(WorldState world)
    {
        var engine = new SimulationEngine(world);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(190, 12, 21));
        world.Commit(mutation);
        return engine;
    }

    [Theory]
    [InlineData(155, 0, 3)]
    [InlineData(156, 0, 0)]
    [InlineData(150, 1, 0)]
    [InlineData(140, 1, 30)]
    [InlineData(92, 20, 0)]
    [InlineData(91, int.MaxValue, 100)]
    [InlineData(160, int.MaxValue, 0)]
    public void ChanceUsesBirthAgeWideLifespanArithmeticAndHundredYearOverride(int birth, int lifespan, int chance) =>
        Assert.Equal(chance, NaturalMortality.Chance(190, Person(1, null, 10, birth) with { LifespanTier = lifespan }));

    [Fact]
    public void AnnualBoundaryDeathAndSuccessionRepeatAfterRestoreWithoutReplayingOnLoad()
    {
        var world = World();
        var engine = BeforeAnnualBoundary(world);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()));
        var events = engine.AdvanceXun().Events;
        EventAssert.Equal(events, restored.AdvanceXun().Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(1, Assert.Single(events.OfType<OfficerDiedEvent>()).OfficerId);
        Assert.Equal(11, engine.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        Assert.Empty(new SimulationEngine(WorldState.Restore(engine.CaptureStableState())).InitialEvents);
        Assert.DoesNotContain(engine.AdvanceXun().Events, e => e is OfficerDiedEvent);
    }

    [Theory]
    [InlineData(false, 360, false)]
    [InlineData(true, 359, false)]
    [InlineData(true, 360, true)]
    public void DisabledMortalityOrFirstYearConsumesNoRandom(bool enabled, int days, bool infinite)
    {
        var world = World(enabled);
        var mutation = world.BeginMutation(companion: new() { InfiniteLifespan = infinite });
        var date = mutation.Clock;
        for (int i = 0; i < days; i++) date = date.NextDay();
        mutation.SetClock(date);
        var events = new List<DomainEvent>();
        NaturalMortality.Apply(mutation, new(), events);
        Assert.Empty(events);
        Assert.Empty(mutation.Simulation.RandomStreams);
        Assert.Equal(OfficerStatus.Ruler, mutation.Officers[1].Status);
    }

    [Fact]
    public void ExemptionsConsumeNoRandomButEveryEligiblePersonConsumesOneRoll()
    {
        var world = World(others: [Person(30, null, 10, 90, OfficerStatus.Special),
            Person(31, null, 10, 90, OfficerStatus.Dead), Person(32, null, 10, 0, OfficerStatus.Free),
            Person(33, null, 10, 90, OfficerStatus.Free)]);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        mutation.SetSimulation(mutation.Simulation with { Controls = [new("player", 1, ControllerKind.Player)] });
        mutation.Replace(mutation.Forces[2] with { IsBarbarian = true });
        var events = new List<DomainEvent>();
        NaturalMortality.Apply(mutation, new(), events);
        Assert.Equal(33, Assert.Single(events.OfType<OfficerDiedEvent>()).OfficerId);
        var random = new StableRandom(StableRandom.DeriveSeed(42, "personnel-natural-death"));
        // Two young eligible officers (zero chance) and the hundred-year-old free officer.
        for (int i = 0; i < 3; i++) random.NextUInt64();
        Assert.Equal(random.State, Assert.Single(mutation.Simulation.RandomStreams).State);
    }

    [Fact]
    public void SuccessionRelationshipsOutrankAuthoredPriorityAndWideAbilitySum()
    {
        var mutation = World().BeginMutation();
        var old = mutation.Officers[1];
        mutation.Replace(mutation.Officers[12] with { SuccessionPriority = int.MaxValue,
            BaseAbilities = new(int.MaxValue, int.MaxValue, int.MaxValue, int.MaxValue) });
        Assert.Equal(11, RulerSuccession.Automatic(mutation, 1, old)!.Id);
        mutation.Replace(mutation.Officers[11] with { ParentId = null });
        Assert.Equal(12, RulerSuccession.Automatic(mutation, 1, old)!.Id);
        mutation.Replace(mutation.Officers[11] with { Status = OfficerStatus.Captive, SettlementId = 30, ParentId = 1 });
        Assert.Equal(12, RulerSuccession.Automatic(mutation, 1, old)!.Id);
    }

    [Fact]
    public void LastServingPersonDeathEliminatesForceAndStopsTheCandidateQueue()
    {
        var world = World(others: [Person(99, null, 10, 90, OfficerStatus.Free)]);
        var mutation = world.BeginMutation();
        mutation.SetClock(new(191, 1, 1));
        mutation.Replace(mutation.Officers[2] with { BirthYear = 90 });
        var events = new List<DomainEvent>();
        NaturalMortality.Apply(mutation, new(), events);
        world.Commit(mutation);
        Assert.Equal("conquest", world.Simulation.TerminalReason);
        Assert.Equal(SimulationBoundary.Terminal, world.Simulation.Boundary);
        Assert.Equal(OfficerStatus.Free, world.CreateSnapshot().Officers.Single(p => p.Id == 99).Status);
        Assert.Single(events.OfType<ForceEliminatedEvent>());
        Assert.Single(events.OfType<SimulationEndedEvent>());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void RulerDeathWithoutCitiesDoesNotOfferOrCommitSuccession(bool execution)
    {
        var world = World();
        var mutation = world.BeginMutation();
        foreach (int id in new[] { 10, 20 })
            mutation.Replace(mutation.Settlements[id] with { Kind = SettlementKind.Facility });
        mutation.SetSimulation(mutation.Simulation with { Controls = [new("player", 1, ControllerKind.Player)] });
        List<DomainEvent> events = [];
        if (execution)
        {
            mutation.Replace(mutation.Officers[1] with { Status = OfficerStatus.Captive, SettlementId = 30, IsCommander = false });
            Assert.Equal(CommandRejection.None, PrisonerExecution.Start(mutation,
                new(Guid.NewGuid(), 2, 0, 1), new(), events));
        }
        else OfficerDeath.Resolve(mutation, 1, OfficerDeathCause.Natural, new(), events, false);
        Assert.Null(mutation.Simulation.PendingExecutionSuccession);
        Assert.DoesNotContain(events, e => e is ExecutionSuccessionRequestedEvent or ForceLeaderChangedEvent);
        Assert.Equal(1, Assert.Single(events.OfType<ForceEliminatedEvent>()).ForceId);
        ResolutionPipeline.CheckTerminal(mutation, events);
        world.Commit(mutation);
    }

    [Fact]
    public void ChildCorpsSucceedsAndInheritsAssetsTreasuresAndLoyaltyAdjustment()
    {
        var world = World(treasures: [new(7, 1, TreasureDiscovery.Discovered, null)]);
        var rules = new SimulationRules { Governance = new([new(5, 10000, 0)]), Treasures = new([new(7, default, 0)]) };
        var mutation = world.BeginMutation(rules.Abilities);
        var rootTreasury100 = ResourceAccounts.Root(mutation, 1).Treasury; mutation.RemoveCorps(ResourceAccounts.Root(mutation, 1).Id); mutation.AddCorps(new(100, 1, 1, rootTreasury100) { IsRoot = true });
        mutation.AddCorps(new(200, 1, 11, new(300, 400)));
        mutation.Replace(mutation.Settlements[10] with { CorpsId = 100 });
        mutation.Replace(mutation.Settlements[20] with { CorpsId = 200 });
        mutation.Replace(mutation.Officers[11] with { Status = OfficerStatus.Governor, OfficeId = 5 });
        mutation.Replace(mutation.Officers[12].WithLoyalty(1, 90));
        mutation.SetSimulation(mutation.Simulation with
        {
            NextSequence = 1,
            Tasks = [new(Guid.NewGuid(), PersonnelReturns.Kind, "system", 1, 12, 10, mutation.Clock, mutation.Clock.NextDay(), 0, 0)
            {
                Journey = new(10, null, null) { Route = new(new(0, 0), new(1, 0), false),
                    ReturnLeg = new(20, 1, false) { CorpsId = 200 } }
            }]
        });
        var events = new List<DomainEvent>();
        OfficerDeath.Resolve(mutation, 1, OfficerDeathCause.Natural, rules, events, afterDailyCommands: false);
        world.Commit(mutation);
        var snapshot = world.CreateSnapshot();
        Assert.Equal(100, Assert.Single(snapshot.Corps, c => c.OwnerId == 1).Id);
        Assert.True(Assert.Single(snapshot.Corps, c => c.OwnerId == 1).IsRoot);
        Assert.All(snapshot.Settlements.Where(s => s.OwnerId == 1), s => Assert.Equal(100, s.CorpsId));
        Assert.Equal(1300u, snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        Assert.Equal(1400u, snapshot.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Food);
        Assert.Equal(75, snapshot.Officers.Single(p => p.Id == 12).Loyalty);
        Assert.Null(snapshot.Officers.Single(p => p.Id == 11).OfficeId);
        Assert.Equal(11, Assert.Single(snapshot.Treasures).HolderId);
        Assert.Equal(100, Assert.Single(snapshot.Simulation.Tasks).Journey!.ReturnLeg!.CorpsId);
        Assert.Equal(2, Assert.Single(snapshot.Corps, c => c.OwnerId == 2).Id);
    }
}
