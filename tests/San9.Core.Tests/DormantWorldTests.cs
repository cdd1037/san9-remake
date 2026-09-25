using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DormantWorldTests
{
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
        Assert.Throws<ArgumentException>(() => world.Commit(mutation));
        Assert.Same(before, world.CreateSnapshot());
    }
}
