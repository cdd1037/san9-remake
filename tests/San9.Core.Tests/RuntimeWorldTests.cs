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
        Assert.Equal(new DevelopmentOutcome(DevelopmentRejection.None,7,50),first.Develop(new(1,15,1,DomesticActivity.Commerce)));
        var after=engine.Snapshot;
        Assert.Equal(50u,after.Forces.Single(f=>f.Id==1).Money);
        Assert.Equal(907,after.Settlements.Single(s=>s.Id==15).Commerce);
        Assert.True(after.Officers.Single(o=>o.Id==1).Acted);
        Assert.True(after.Settlements.Single(s=>s.Id==15).CommerceUsed);
        Assert.Equal(before.Clock,after.Clock); Assert.Equal(before.RandomState,after.RandomState);
        Assert.Equal(100u,before.Forces.Single(f=>f.Id==1).Money);
        Assert.Equal(before.StateHash,new SimulationEngine(second).Snapshot.StateHash);
        Assert.NotEqual(before.StateHash,after.StateHash);
        Assert.False(first.Develop(new(1,15,2,DomesticActivity.Commerce)).Accepted);
        Assert.Equal(after.StateHash,engine.Snapshot.StateHash);
    }

    [Fact]
    public void InvalidCommandsHaveNoEffects()
    {
        var world=Create(49); var engine=new SimulationEngine(world); var initial=engine.Snapshot;
        foreach(var command in new DevelopmentCommand[] { new(1,15,1,DomesticActivity.Commerce),new(1,16,1,DomesticActivity.Commerce),
            new(1,15,3,DomesticActivity.Cultivation),new(1,15,99,DomesticActivity.Commerce),new(1,15,1,DomesticActivity.Repair) })
        {
            Assert.False(world.Develop(command).Accepted);
            Assert.Same(initial,engine.Snapshot);
        }
    }

    [Fact]
    public void XunActionRefreshAllowsNextDevelopmentAndHashIncludesActionState()
    {
        var world=Create(150); var engine=new SimulationEngine(world);
        Assert.True(world.Develop(new(1,15,1,DomesticActivity.Commerce)).Accepted);
        Assert.False(world.Develop(new(1,15,1,DomesticActivity.Cultivation)).Accepted);
        engine.AdvanceXun();
        Assert.True(world.Develop(new(1,15,1,DomesticActivity.Commerce)).Accepted);
    }
}
