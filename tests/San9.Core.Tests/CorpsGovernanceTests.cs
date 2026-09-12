using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CorpsGovernanceTests
{
    private static SimulationEngine Engine(uint money = 900000)
    {
        return new(WorldState.Create(new(190, 1, 1), 1,
            [new(1, money, 900000, 0) { LeaderId = 1 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 11 }],
            [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 2, new(50, 50, 50, 50), true) { Status = OfficerStatus.Governor, IsCommander = true }],
            units: [new(5, 1, new(20, 20), 100) { CorpsId = 11 }],
            corps: [new(10, 1, 1, null), new(11, 1, 2, new(200000, 300000))],
            controls: [new("player", 1, ControllerKind.Player)]));
    }

    [Fact]
    public void DissolutionTransfersEveryReferenceAndDoesNotCapCombinedResources()
    {
        var engine = Engine();
        var before = engine.Snapshot;
        var command = new DissolveCorpsCommand(Guid.NewGuid(), 1, 0, 11);
        Assert.True(engine.Validate(new("player"), command).Accepted);
        Assert.Equal(before.StateHash, engine.Snapshot.StateHash);
        var result = engine.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(10, Assert.Single(result.Snapshot.Corps).Id);
        Assert.All(result.Snapshot.Settlements, s => Assert.Equal(10, s.CorpsId));
        Assert.Equal(10, Assert.Single(result.Snapshot.Units).CorpsId);
        var person = result.Snapshot.Officers.Single(p => p.Id == 2);
        Assert.Equal(OfficerStatus.Serving, person.Status);
        Assert.Equal(before.Officers.Single(p => p.Id == 2).Acted, person.Acted);
        Assert.True(person.IsCommander);
        Assert.Equal(2, person.SettlementId);
        Assert.Equal(Assert.Single(before.Forces).Money + before.Corps.Single(c => c.Id == 11).Treasury!.Money, Assert.Single(result.Snapshot.Forces).Money);
        Assert.Equal(Assert.Single(before.Forces).Food + before.Corps.Single(c => c.Id == 11).Treasury!.Food, Assert.Single(result.Snapshot.Forces).Food);
        Assert.True(Assert.Single(result.Snapshot.Forces).Money > 1000000);
        Assert.All(result.Events.OfType<ResourceChangedEvent>(), e => Assert.Equal(0, e.SaturationLoss));
        Assert.Equal(0, result.Events.OfType<ResourceChangedEvent>().Sum(e => e.Actual));
        Assert.Single(result.Events.OfType<CorpsDissolvedEvent>());
        Assert.Equal(result.Snapshot.StateHash, WorldState.Restore(result.Snapshot).CreateSnapshot().StateHash);
    }

    [Theory]
    [InlineData(10, false, CommandRejection.InvalidCorps)]
    [InlineData(999, false, CommandRejection.InvalidCorps)]
    [InlineData(11, true, CommandRejection.ResourceOverflow)]
    public void InvalidOrOverflowingDissolutionIsAtomic(int corps, bool overflow, CommandRejection expected)
    {
        var engine = Engine(overflow ? uint.MaxValue : 900000);
        string before = engine.Snapshot.StateHash;
        var result = engine.Execute(new("player"), new DissolveCorpsCommand(Guid.NewGuid(), 1, 0, corps));
        Assert.Equal(expected, result.Submission.Rejection);
        Assert.Empty(result.Events);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
}
