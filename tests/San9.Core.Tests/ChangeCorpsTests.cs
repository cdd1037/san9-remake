using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ChangeCorpsTests
{
    private static SimulationEngine Engine(uint money = 1000, ControllerKind controller = ControllerKind.Player)
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 0) { LeaderId = 1 }],
            Enumerable.Range(1, 3).Select(id => new SettlementState(id, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)
                { CorpsId = id == 2 ? 11 : 10 }),
            Enumerable.Range(1, 3).Select(id => new OfficerState(id, 1, id, new(50, 50, 50, 50), false)
                { Status = id == 1 ? OfficerStatus.Ruler : id == 2 ? OfficerStatus.Governor : OfficerStatus.Serving }),
            units: [new(1, 1, new(999, 999), 100) { CorpsId = 11, ServicePlaceId = 2, IsPersonnel = true },
                new(2, 1, new(999, 999), 100) { CorpsId = 10, ServicePlaceId = 3, IsEmbedded = true, IsPersonnel = true }],
            corps: [new(10, 1, 1, new(money, 1000)) { IsRoot = true }, new(11, 1, 2, new(100, 200))],
            controls: [new("controller", 1, controller)]);
        return new(world, EconomyRulebook.Empty, new PersonnelRulebook(
            Enumerable.Range(1, 3).Select(id => new PersonPlace(id, new(id * 100, 0))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3 }));
    }

    [Theory]
    [InlineData(ControllerKind.Player)]
    [InlineData(ControllerKind.Automatic)]
    public void ReplacingTerritoryReelectsLeaderAndMovesServiceObjectsWithConservedResources(ControllerKind controller)
    {
        var engine = Engine(controller: controller);
        var before = engine.Snapshot;
        var command = new ChangeCorpsCommand(Guid.NewGuid(), 1, 0, 11, [3], 50, 300, new DomesticDelegation());
        Assert.True(engine.Validate(new("controller"), command).Accepted);
        Assert.Equal(before.StateHash, engine.Snapshot.StateHash);
        var result = engine.Execute(new("controller"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(3, result.Snapshot.Corps.Single(c => c.Id == 11).LeaderId);
        Assert.IsType<DomesticDelegation>(result.Snapshot.Corps.Single(c => c.Id == 11).Delegation);
        Assert.Single(result.Events.OfType<CorpsDelegationChangedEvent>());
        Assert.Equal(OfficerStatus.Serving, result.Snapshot.Officers.Single(p => p.Id == 2).Status);
        Assert.Equal(OfficerStatus.Governor, result.Snapshot.Officers.Single(p => p.Id == 3).Status);
        Assert.Equal(10, result.Snapshot.Units.Single(u => u.Id == 1).CorpsId);
        Assert.Equal(11, result.Snapshot.Units.Single(u => u.Id == 2).CorpsId);
        Assert.All(result.Snapshot.Units, u => Assert.Equal(new WorldPosition(999, 999), u.Position));
        var treasury = before.Corps.Single(c => c.Id == 11).Treasury!;
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Money + treasury.Money - 50, result.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money);
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Food + treasury.Food - 300, result.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Food);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void InvalidChangesAndOverflowAreAtomic(int branch)
    {
        var engine = Engine(branch == 4 ? uint.MaxValue : 1000);
        var before = engine.Snapshot.StateHash;
        var result = engine.Execute(new("controller"), new ChangeCorpsCommand(Guid.NewGuid(), 1, 0,
            branch == 0 ? 10 : 11, branch == 1 ? [1] : branch == 2 ? [2, 2] : [3],
            branch == 3 ? uint.MaxValue : 0, 0, branch == 5 ? new DefeatForceDelegation(1) : new GeneralDelegation()));
        Assert.False(result.Submission.Accepted);
        Assert.Empty(result.Events);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }
}
