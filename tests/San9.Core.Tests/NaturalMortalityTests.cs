using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class NaturalMortalityTests
{
    private static OfficerState Person(int id, int? owner, int? home, int birth, OfficerStatus status = OfficerStatus.Serving) =>
        new(id, owner, home, new(70, 70, 70, 70), false) { BirthYear = birth, Status = status };

    private static WorldState World(bool enabled = true, IEnumerable<OfficerState>? others = null, IEnumerable<TreasureState>? treasures = null) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 1000, 1000, 0) { LeaderId = 1 }, new(2, 1000, 1000, 0) { LeaderId = 2 }],
        [new(10, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(20, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100),
         new(30, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        new[] { Person(1, 1, 10, 90, OfficerStatus.Ruler), Person(2, 2, 30, 170, OfficerStatus.Ruler),
         Person(11, 1, 20, 170) with { ParentId = 1, BloodlineId = 1 }, Person(12, 1, 10, 160) with { Affinity = 31 } }.Concat(others ?? []),
        naturalDeathsEnabled: enabled, treasures: treasures);

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
        Assert.Equal(CanonicalJson.Text(events), CanonicalJson.Text(restored.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
        Assert.Equal(1, Assert.Single(events.OfType<OfficerDiedEvent>()).OfficerId);
        Assert.Equal(11, engine.Snapshot.Forces.Single(f => f.Id == 1).LeaderId);
        Assert.Equal(OfficerStatus.Ruler, engine.Snapshot.Officers.Single(p => p.Id == 11).Status);
        Assert.Empty(new SimulationEngine(WorldState.Restore(engine.CaptureStableState())).InitialEvents);
        Assert.DoesNotContain(engine.AdvanceXun().Events, e => e is OfficerDiedEvent);
    }

    [Theory]
    [InlineData(false, 360)]
    [InlineData(true, 359)]
    public void DisabledOrBefore360DaysHasNoRandomConsumption(bool enabled, int days)
    {
        var world = World(enabled);
        var mutation = world.BeginMutation();
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

    [Fact]
    public void ChildCorpsSucceedsAndInheritsAssetsTreasuresAndLoyaltyAdjustment()
    {
        var world = World(treasures: [new(7, 1, TreasureDiscovery.Discovered, null)]);
        var rules = new SimulationRules { Governance = new([new(5, 10000, 0)]), Treasures = new([new(7, default, 0)]) };
        var mutation = world.BeginMutation(rules.Abilities);
        mutation.AddCorps(new(100, 1, 1, null));
        mutation.AddCorps(new(200, 1, 11, new(300, 400)) { ControlMode = CorpsControlMode.Automatic });
        mutation.Replace(mutation.Settlements[10] with { CorpsId = 100 });
        mutation.Replace(mutation.Settlements[20] with { CorpsId = 200 });
        mutation.Replace(mutation.Officers[11] with { Status = OfficerStatus.Governor, OfficeId = 5 });
        mutation.Replace(mutation.Officers[12].WithLoyalty(1, 90));
        var events = new List<DomainEvent>();
        OfficerDeath.Resolve(mutation, 1, OfficerDeathCause.Natural, rules, events, afterDailyCommands: false);
        world.Commit(mutation);
        var snapshot = world.CreateSnapshot();
        Assert.Equal(200, Assert.Single(snapshot.Corps).Id);
        Assert.Null(Assert.Single(snapshot.Corps).Treasury);
        Assert.Equal(CorpsControlMode.Direct, Assert.Single(snapshot.Corps).ControlMode);
        Assert.All(snapshot.Settlements.Where(s => s.OwnerId == 1), s => Assert.Equal(200, s.CorpsId));
        Assert.Equal(1300u, snapshot.Forces.Single(f => f.Id == 1).Money);
        Assert.Equal(1400u, snapshot.Forces.Single(f => f.Id == 1).Food);
        Assert.Equal(75, snapshot.Officers.Single(p => p.Id == 12).Loyalty);
        Assert.Null(snapshot.Officers.Single(p => p.Id == 11).OfficeId);
        Assert.Equal(11, Assert.Single(snapshot.Treasures).HolderId);
    }
}
