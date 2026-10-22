using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DormantWorldTests(Xunit.Abstractions.ITestOutputHelper output)
{
    [Theory]
    [InlineData(100, 100)]
    [InlineData(100, 1_000)]
    [InlineData(10_000, 100)]
    [InlineData(10_000, 1_000)]
    public void CandidateTransactionsShareParticipationIndicesAtGrowingWorldSizes(int people, int candidates)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100), new(2, 100) { IsDormant = true }],
            [new(1, 1, 1000, 100, 100, 50, 0, 0, 0, 0, 0),
             new(2, 2, 1000, 100, 100, 50, 0, 0, 0, 0, 0) { IsDormant = true }],
            Enumerable.Range(0, people).Select(id => new OfficerState(id, id % 2 + 1, id % 2 + 1, new(), false)
                { IsDormant = id % 2 != 0 }),
            corps: [new(1, 1, null, new(100, 100)) { IsRoot = true }, new(2, 2, null, new(100, 100)) { IsRoot = true }]);
        var initial = world.BeginMutation();
        long before = GC.GetAllocatedBytesForCurrentThread();
        long started = System.Diagnostics.Stopwatch.GetTimestamp();
        WorldMutation last = initial;
        for (int i = 0; i < candidates; i++) last = world.BeginMutation();
        double elapsed = System.Diagnostics.Stopwatch.GetElapsedTime(started).TotalMilliseconds;
        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        output.WriteLine($"people={people}, candidates={candidates}, bytes={allocated}, ms={elapsed:F3}");
        Assert.True(allocated < candidates * 4096L, $"Transaction initialization allocated {allocated} bytes.");
        Assert.Equal(people / 2, last.Officers.Count);
        Assert.Same(initial.Officers, last.Officers);
    }

    [Fact]
    public void ParticipationIndicesRemainIsolatedAcrossForkCommitAndRestore()
    {
        var world = Create();
        var sibling = world.Fork();
        var before = world.BeginMutation();
        var mutation = world.BeginMutation();
        Assert.Same(before.Forces, mutation.Forces);
        Assert.Same(before.Settlements, mutation.Settlements);
        Assert.Same(before.Officers, mutation.Officers);
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        mutation.Replace(mutation.AllForces[2] with { IsDormant = false });
        mutation.Replace(mutation.AllSettlements[2] with { IsDormant = false });
        mutation.Replace(mutation.AllOfficers[2] with { IsDormant = false });
        world.Commit(mutation);
        Assert.Empty(sibling.BeginMutation().Officers);
        Assert.Empty(before.Officers);
        Assert.Single(world.BeginMutation().Officers);
        Assert.Same(mutation.Officers, world.Fork().BeginMutation().Officers);
        var restored = WorldState.Restore(world.CreateSnapshot());
        var next = restored.BeginMutation();
        Assert.Equal(mutation.Officers, next.Officers);
        next.Replace(next.AllOfficers[2] with { IsDormant = true });
        next.Replace(next.AllSettlements[2] with { IsDormant = true });
        next.Replace(next.AllForces[2] with { IsDormant = true });
        restored.Commit(next);
        Assert.Empty(restored.BeginMutation().Officers);
        Assert.Empty(restored.BeginMutation().Settlements);
        Assert.Single(restored.BeginMutation().Forces);
        Assert.Single(world.BeginMutation().Officers);
    }

    private static WorldState Create() => WorldState.Create(new(190, 1, XunIndex.Early), 17,
        [new(1, 50), new ForceState(2, 60) { IsDormant = true }],
        [new SettlementState(2, 2, 100, 10, 100, 50, 500, 10, 100, 10, 100) { IsDormant = true }],
        [new OfficerState(2, 2, 2, new(), false) { IsDormant = true }],
        corps: [new(2, 2, 2, new(200, 300)) { IsRoot = true }, new San9.Core.Domain.CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }], treasures: [new(2, 2, TreasureDiscovery.Discovered, null)],
        relations: [new(1, 2, 50)]);

    [Fact]
    public void DormantRecordsSurviveCommitAndRestoreWithoutEnteringRuleIndices()
    {
        var world = Create();
        var before = world.CreateSnapshot();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        Assert.Single(mutation.Forces);
        Assert.Empty(mutation.Settlements);
        Assert.Empty(mutation.Officers);
        Assert.Equal(1, Assert.Single(mutation.Corps.Values).OwnerId);
        Assert.Empty(mutation.Treasures);
        Assert.Empty(mutation.Relations);
        mutation.Replace(ResourceAccounts.Root(mutation, 1) with { Treasury = ResourceAccounts.Root(mutation, 1).Treasury with { Money = 99 } });
        world.Commit(mutation);
        var restored = WorldState.Restore(world.CreateSnapshot()).CreateSnapshot();
        Assert.Equal(before.Forces[1], restored.Forces[1]);
        Assert.Equal(before.Settlements, restored.Settlements);
        Assert.Equal(before.Officers, restored.Officers);
        Assert.Equal(before.Corps.Single(c => c.OwnerId == 2), restored.Corps.Single(c => c.OwnerId == 2));
        Assert.Equal(before.Treasures, restored.Treasures);
        Assert.Equal(before.Relations, restored.Relations);
    }

    [Fact]
    public void ActivationRefreshesDependentIndicesAndIsPublishedAtomically()
    {
        var world = Create();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        Assert.Equal(1, Assert.Single(mutation.Corps.Values).OwnerId);
        Assert.Empty(mutation.Treasures);
        Assert.Empty(mutation.Relations);
        mutation.Replace(mutation.AllForces[2] with { IsDormant = false });
        mutation.Replace(mutation.AllSettlements[2] with { IsDormant = false });
        mutation.Replace(mutation.AllOfficers[2] with { IsDormant = false });
        Assert.Equal(2, mutation.Corps.Count);
        Assert.Single(mutation.Treasures);
        Assert.Single(mutation.Relations);
        Assert.True(world.CreateSnapshot().Forces[1].IsDormant);
        world.Commit(mutation);
        Assert.False(WorldState.Restore(world.CreateSnapshot()).CreateSnapshot().Forces[1].IsDormant);
    }

    [Fact]
    public void ActiveOfficerCannotRemainInDormantSettlement()
    {
        var world = Create();
        var before = world.CreateSnapshot();
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        mutation.Replace(mutation.AllOfficers[2] with { IsDormant = false });
        Assert.Throws<ArgumentException>(() => WorldInvariants.Validate(mutation));
        Assert.Same(before, world.CreateSnapshot());
    }
}
