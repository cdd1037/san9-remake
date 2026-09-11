using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class RuntimeWorldTests
{
    private static WorldState Create(uint funds = 100) => WorldState.Create(new(190,1,XunIndex.Early),190,
        [new(1,funds,500,100),new(2,100,500,100)],
        [new(15,1,1000,500,1000,50,500,900,1000,900,1000), new(16,2,1000,500,1000,50,500,900,1000,900,1000)],
        [new(1,1,15,new(0,0,100,0),false),new(2,1,15,new(0,0,100,0),false),new(3,2,16,new(0,0,100,0),false)]);

    [Fact]
    public void DevelopmentAtomicallyUpdatesOnlyOneWorldAndPreservesOldSnapshot()
    {
        var first=Create(); var second=Create(); var engine=new SimulationEngine(first);
        var before=engine.Snapshot;
        var command = new DevelopmentCommand(1, 15, 1, DomesticActivity.Commerce);
        var result = engine.Develop(command);
        Assert.Equal(new DevelopmentOutcome(DevelopmentRejection.None,7,50), result.Outcome);
        Assert.Equal(new DevelopmentCompletedEvent(before.Clock, command, 7, 50), Assert.Single(result.Events));
        Assert.Same(engine.Snapshot, result.Snapshot);
        Assert.Equal(before.Revision + 1, result.Snapshot.Revision);
        var after=engine.Snapshot;
        Assert.Equal(50u,after.Forces.Single(f=>f.Id==1).Money);
        Assert.Equal(907,after.Settlements.Single(s=>s.Id==15).Commerce);
        Assert.True(after.Officers.Single(o=>o.Id==1).Acted);
        Assert.True(after.Settlements.Single(s=>s.Id==15).CommerceUsed);
        Assert.Equal(before.Clock,after.Clock); Assert.Equal(before.RandomState,after.RandomState);
        Assert.Equal(100u,before.Forces.Single(f=>f.Id==1).Money);
        Assert.Equal(before.StateHash,new SimulationEngine(second).Snapshot.StateHash);
        Assert.NotEqual(before.StateHash,after.StateHash);
        Assert.False(engine.Develop(new(1,15,2,DomesticActivity.Commerce)).Outcome.Accepted);
        Assert.Equal(after.StateHash,engine.Snapshot.StateHash);
    }

    [Fact]
    public void InvalidCommandsHaveNoEffects()
    {
        var world=Create(49); var engine=new SimulationEngine(world); var initial=engine.Snapshot;
        foreach(var command in new DevelopmentCommand[] { new(1,15,1,DomesticActivity.Commerce),new(1,16,1,DomesticActivity.Commerce),
            new(1,15,3,DomesticActivity.Cultivation),new(1,15,99,DomesticActivity.Commerce),new(1,15,1,DomesticActivity.Repair) })
        {
            var result = engine.Develop(command);
            Assert.False(result.Outcome.Accepted);
            Assert.Empty(result.Events);
            Assert.Same(initial, result.Snapshot);
            Assert.Same(initial,engine.Snapshot);
        }
    }

    [Fact]
    public void XunActionRefreshAllowsNextDevelopmentAndHashIncludesActionState()
    {
        var world=Create(150); var engine=new SimulationEngine(world);
        Assert.True(engine.Develop(new(1,15,1,DomesticActivity.Commerce)).Outcome.Accepted);
        Assert.False(engine.Develop(new(1,15,1,DomesticActivity.Cultivation)).Outcome.Accepted);
        var before = engine.Snapshot;
        var wait = new WaitCommand(Guid.NewGuid(), 1, 0);
        Assert.True(engine.Submit(wait).Accepted);
        var advanced = engine.AdvanceXun();
        Assert.Equal(before.Revision + 1, advanced.Snapshot.Revision);
        Assert.Equal(before.Clock.Advance(), advanced.Snapshot.Clock);
        Assert.Equal(before.RandomState, advanced.Snapshot.RandomState);
        Assert.False(advanced.Snapshot.Officers.Single(o => o.Id == 1).Acted);
        Assert.False(advanced.Snapshot.Settlements.Single(s => s.Id == 15).CommerceUsed);
        Assert.True(before.Officers.Single(o => o.Id == 1).Acted);
        Assert.Collection(advanced.Events,
            resolved => Assert.Equal(wait.CommandId, Assert.IsType<CommandResolvedEvent>(resolved).CommandId),
            advancedEvent => Assert.Equal(new XunAdvancedEvent(before.Clock, before.Clock.Advance()), advancedEvent));
        Assert.True(engine.Validate(wait).Accepted);
        Assert.True(engine.Develop(new(1,15,1,DomesticActivity.Commerce)).Outcome.Accepted);
    }

    [Fact]
    public void MutationCommitsOnceAndRejectsStaleOrForeignWorlds()
    {
        var world = Create();
        var other = Create();
        var mutation = world.BeginMutation();
        var stale = world.BeginMutation();
        var original = world.CreateSnapshot();
        mutation.Replace(original.Forces.Single(f => f.Id == 1) with { Money = 75 });

        world.Commit(mutation);

        Assert.Equal(1, world.Revision);
        Assert.Equal(75u, world.CreateSnapshot().Forces.Single(f => f.Id == 1).Money);
        Assert.Throws<InvalidOperationException>(() => world.Commit(mutation));
        Assert.Throws<InvalidOperationException>(() => world.Commit(stale));
        Assert.Throws<InvalidOperationException>(() => other.Commit(stale));
        Assert.Equal(1, world.Revision);
        Assert.Equal(0, other.Revision);
    }

    [Fact]
    public void SnapshotOrdersEveryIdentityCollection()
    {
        var world = WorldState.Create(new(190, 1, XunIndex.Early), 190,
            [new(3, 0, 0, 0), new(1, 0, 0, 0), new(2, 0, 0, 0)],
            [new(17, 3, 1, 1, 1, 1, 1, 0, 0, 0, 0), new(15, 1, 1, 1, 1, 1, 1, 0, 0, 0, 0),
                new(16, 2, 1, 1, 1, 1, 1, 0, 0, 0, 0)],
            [new(3, 3, 17, new(), false), new(1, 1, 15, new(), false), new(2, 2, 16, new(), false)],
            [new(3, 3, 1, 1, 1), new(1, 1, 1, 1, 1), new(2, 2, 1, 1, 1)],
            [new(3, 3, 3, null), new(1, 1, 1, null), new(2, 2, 2, null)],
            [new(3, 3, TreasureDiscovery.Discovered, null), new(1, 1, TreasureDiscovery.Discovered, null),
                new(2, 2, TreasureDiscovery.Discovered, null)],
            [new(2, 3, 50), new(1, 3, 50), new(1, 2, 50)]);

        var snapshot = world.CreateSnapshot();

        Assert.Equal([1, 2, 3], snapshot.Forces.Select(f => f.Id));
        Assert.Equal([15, 16, 17], snapshot.Settlements.Select(s => s.Id));
        Assert.Equal([1, 2, 3], snapshot.Officers.Select(o => o.Id));
        Assert.Equal([1, 2, 3], snapshot.Units.Select(u => u.Id));
        Assert.Equal([1, 2, 3], snapshot.Corps.Select(c => c.Id));
        Assert.Equal([1, 2, 3], snapshot.Treasures.Select(t => t.Id));
        Assert.Equal([(1, 2), (1, 3), (2, 3)], snapshot.Relations.Select(r => (r.FirstId, r.SecondId)));
    }
}
