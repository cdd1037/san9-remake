using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FreeOfficerMigrationTests
{
    private static readonly PersonnelRulebook Places = new(
        [new(1, new(0, 0)), new(2, new(300, 0)), new(20, new(350, 0)), new(3, new(600, 0))],
        new Dictionary<int, int> { [1] = 1, [2] = 2, [20] = 2, [3] = 3 }, [new(1, 2)]);

    private static WorldState World(ulong seed, bool enabled = true) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 1000, 1000, 0) { LeaderId = 1 }, new(2, 1000, 1000, 0) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(9, null, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Free, StatusStartYear = 180 }
             .WithLoyalty(1, 70)], controls: [new("player", 1, ControllerKind.Player)], freeOfficerMigrationEnabled: enabled);

    private static ulong SeedFor(int roll)
    {
        for (ulong seed = 0; ; seed++)
        {
            var random = new StableRandom(StableRandom.DeriveSeed(seed, "personnel-free-migration"));
            if (random.NextUInt64() % 100 == (ulong)roll) return seed;
        }
    }

    [Theory]
    [InlineData(29, true, 49, false)]
    [InlineData(30, false, 49, false)]
    [InlineData(30, true, 49, true)]
    [InlineData(30, true, 50, false)]
    public void ThirtyDayAndStrictProbabilityGates(int elapsed, bool enabled, int roll, bool migrates)
    {
        var world = World(SeedFor(roll), enabled).BeginMutation();
        world.SetClock(elapsed == 29 ? new(190, 1, 30) : new(190, 2, 1));
        var before = world.Officers[9];
        List<DomainEvent> events = [];
        FreeOfficerMigration.Apply(world, Places, events);
        if (migrates)
        {
            var change = Assert.Single(events.OfType<FreeOfficerMigratedEvent>());
            Assert.Equal(9, change.OfficerId);
            Assert.Equal(1, change.OriginId);
            Assert.Contains(change.DestinationId, new[] { 2, 20 });
            Assert.Equal(before with { SettlementId = change.DestinationId == 2 ? 2 : null,
                RegionId = change.DestinationId == 20 ? 20 : null }, world.Officers[9]);
        }
        else { Assert.Empty(events); Assert.Equal(before, world.Officers[9]); }
        Assert.Equal(elapsed >= 30 && enabled ? (migrates ? 2 : 1) : 0, world.Simulation.RandomStreams.Length);
    }

    [Fact]
    public void DestinationUsesDirectedNeighborsAndExcludesBarbarianCitiesAndNonHostingBuildings()
    {
        var world = World(1).BeginMutation();
        Assert.Equal(new[] { 2, 20 }, PersonnelRelocation.Destinations(world, 1, Places));
        Assert.Empty(PersonnelRelocation.Destinations(world, 2, Places));
        world.Replace(world.Settlements[2] with { CanHostPersons = false });
        Assert.Equal(new[] { 20 }, PersonnelRelocation.Destinations(world, 1, Places));
        world.Replace(world.Forces[2] with { IsBarbarian = true });
        Assert.Empty(PersonnelRelocation.Destinations(world, 1, Places));
    }

    [Fact]
    public void NoDestinationConsumesOnlyTheMigrationGate()
    {
        var world = World(SeedFor(0)).BeginMutation();
        world.SetClock(new(190, 2, 1));
        var before = world.Officers[9];
        List<DomainEvent> events = [];
        FreeOfficerMigration.Apply(world, PersonnelRulebook.Empty, events);
        Assert.Empty(events);
        Assert.Equal(before, world.Officers[9]);
        Assert.Equal("personnel-free-migration", Assert.Single(world.Simulation.RandomStreams).Name);
    }

    [Fact]
    public void OnlyFreeOfficersRollInIdentityOrderRegardlessOfInsertionOrder()
    {
        var initial = World(190).CreateSnapshot();
        var free = initial.Officers.Single(p => p.Id == 9);
        var excluded = Enum.GetValues<OfficerStatus>().Where(s => s != OfficerStatus.Free).ToArray();
        var world = WorldState.Create(initial.Clock, 190, initial.Forces, initial.Settlements,
            initial.Officers.Concat(new[] { 40, 10, 30, 20 }.Select(id => free with { Id = id }))
                .Concat(excluded.Select(status => free with { Id = 1000 + (int)status }))).BeginMutation();
        world.SetClock(new(190, 2, 1));
        foreach (var status in excluded)
            world.Replace(world.Officers[9] with { Id = 1000 + (int)status, Status = status });
        var random = new StableRandom(StableRandom.DeriveSeed(190, "personnel-free-migration"));
        var expected = new[] { 9, 10, 20, 30, 40 }.Where(_ => random.NextUInt64() % 100 < 50).ToArray();
        List<DomainEvent> events = [];
        FreeOfficerMigration.Apply(world, Places, events);
        Assert.Equal(expected, events.OfType<FreeOfficerMigratedEvent>().Select(e => e.OfficerId));
        Assert.Equal(random.State, world.Simulation.RandomStreams.Single(s => s.Name == "personnel-free-migration").State);
    }

    [Fact]
    public void PublicXunBoundaryMigratesOnceAndStableRestoreDoesNotRepeat()
    {
        var engine = new SimulationEngine(World(SeedFor(0)), EconomyRulebook.Empty, Places);
        for (int i = 0; i < 2; i++)
        {
            engine.EndControl(new("player"));
            Assert.Empty(engine.AdvanceXun().Events.OfType<FreeOfficerMigratedEvent>());
            Assert.Empty(engine.Snapshot.Simulation.RandomStreams.Where(s => s.Name.StartsWith("personnel-free-migration")));
        }
        var resumed = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, Places);
        engine.EndControl(new("player")); resumed.EndControl(new("player"));
        var result = engine.AdvanceXun();
        Assert.Single(result.Events.OfType<FreeOfficerMigratedEvent>());
        Assert.Equal(CanonicalJson.Text(result.Events), CanonicalJson.Text(resumed.AdvanceXun().Events));
        Assert.Equal(engine.Snapshot.StateHash, resumed.Snapshot.StateHash);
        var restored = new SimulationEngine(WorldState.Restore(engine.CaptureStableState()), EconomyRulebook.Empty, Places);
        Assert.Empty(restored.InitialEvents);
        Assert.Equal(engine.Snapshot.StateHash, restored.Snapshot.StateHash);
    }
}
