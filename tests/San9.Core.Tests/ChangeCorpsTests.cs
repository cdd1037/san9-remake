using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ChangeCorpsTests
{
    [Fact]
    public void CityDeltasTransferSameForceFacilitiesAndServiceUnitsFromOtherCorps()
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 0) { LeaderId = 1 }, new(2, 0) { LeaderId = 5 }],
            Enumerable.Range(1, 9).Select(id => new SettlementState(id, id is 8 or 9 ? 2 : 1,
                0, 100, 100, 0, 0, 0, 100, 0, 100)
            {
                Kind = id is >= 5 and <= 8 ? SettlementKind.Facility : SettlementKind.City,
                CorpsId = id switch { 2 => 11, >= 4 and <= 7 => 12, 8 or 9 => 20, _ => 10 }
            }),
            Enumerable.Range(1, 5).Select(id => new OfficerState(id, id == 5 ? 2 : 1,
                id == 5 ? 9 : id, new(50, 50, 50, 50), false)
            { Status = id is 1 or 5 ? OfficerStatus.Ruler : id is 2 or 4 ? OfficerStatus.Governor : OfficerStatus.Serving }),
            units: Enumerable.Range(5, 3).Select(id => new UnitState(id, 1, new(999, 999), 100)
                { CorpsId = 12, ServicePlaceId = id, IsPersonnel = true }),
            corps: [new(10, 1, 1, new(1000, 1000)) { IsRoot = true }, new(11, 1, 2, new(100, 200)),
                new(12, 1, 4, new(300, 400)), new(20, 2, 5, new(1000, 1000)) { IsRoot = true }],
            controls: [new("controller", 1, ControllerKind.Player)]);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, new PersonnelRulebook(
            Enumerable.Range(1, 9).Select(id => new PersonPlace(id, new(id * 100, 0))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 4, [5] = 2, [6] = 3, [8] = 3, [9] = 9 }));
        var before = engine.Snapshot;
        var command = new ChangeCorpsCommand(Guid.NewGuid(), 1, 0, 11, [3], 100, 200,
            new GeneralDelegation(), CorpsControlMode.Direct);
        Assert.True(engine.Validate(new("controller"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);

        var result = engine.Execute(new("controller"), command);
        Assert.True(result.Submission.Accepted);
        foreach (var (id, corpsId) in new[] { (5, 10), (6, 11), (7, 12), (8, 20) })
            Assert.Equal(corpsId, result.Snapshot.Settlements.Single(s => s.Id == id).CorpsId);
        foreach (var (id, corpsId) in new[] { (5, 10), (6, 11), (7, 12) })
            Assert.Equal(corpsId, result.Snapshot.Units.Single(u => u.Id == id).CorpsId);
        Assert.Equal(before.Corps.Single(c => c.Id == 12), result.Snapshot.Corps.Single(c => c.Id == 12));
    }

    private static SimulationEngine Engine(uint money = 1000, ControllerKind controller = ControllerKind.Player)
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 0) { LeaderId = 1 }],
            Enumerable.Range(1, 3).Select(id => new SettlementState(id, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)
                { CorpsId = id == 2 ? 11 : 10 }).Concat(
                    Enumerable.Range(4, 4).Select(id => new SettlementState(id, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)
                        { Kind = SettlementKind.Facility, CorpsId = id is 4 or 6 ? 11 : 10 })),
            Enumerable.Range(1, 3).Select(id => new OfficerState(id, 1, id, new(50, 50, 50, 50), false)
                { Status = id == 1 ? OfficerStatus.Ruler : id == 2 ? OfficerStatus.Governor : OfficerStatus.Serving }),
            units: [new(1, 1, new(999, 999), 100) { CorpsId = 11, ServicePlaceId = 2, IsPersonnel = true },
                new(2, 1, new(999, 999), 100) { CorpsId = 10, ServicePlaceId = 3, IsEmbedded = true, IsPersonnel = true },
                new(3, 1, new(999, 999), 100) { CorpsId = 11, ServicePlaceId = 6, IsPersonnel = true }],
            corps: [new(10, 1, 1, new(money, 1000)) { IsRoot = true }, new(11, 1, 2, new(100, 200))],
            controls: [new("controller", 1, controller)]);
        return new(world, EconomyRulebook.Empty, new PersonnelRulebook(
            Enumerable.Range(1, 7).Select(id => new PersonPlace(id, new(id * 100, 0))),
            new Dictionary<int, int> { [1] = 1, [2] = 2, [3] = 3, [4] = 2, [5] = 3, [7] = 2 }));
    }

    [Theory]
    [InlineData(ControllerKind.Player)]
    [InlineData(ControllerKind.Automatic)]
    public void ReplacingTerritoryReelectsLeaderAndMovesServiceObjectsWithConservedResources(ControllerKind controller)
    {
        var engine = Engine(controller: controller);
        var before = engine.Snapshot;
        var command = new ChangeCorpsCommand(Guid.NewGuid(), 1, 0, 11, [3], 50, 300, new DomesticDelegation(), CorpsControlMode.Direct);
        Assert.True(engine.Validate(new("controller"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(new("controller"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(3, result.Snapshot.Corps.Single(c => c.Id == 11).LeaderId);
        Assert.IsType<DomesticDelegation>(result.Snapshot.Corps.Single(c => c.Id == 11).Delegation);
        Assert.Single(result.Events.OfType<CorpsDelegationChangedEvent>());
        Assert.Equal(OfficerStatus.Serving, result.Snapshot.Officers.Single(p => p.Id == 2).Status);
        Assert.Equal(OfficerStatus.Governor, result.Snapshot.Officers.Single(p => p.Id == 3).Status);
        Assert.Equal(10, result.Snapshot.Units.Single(u => u.Id == 1).CorpsId);
        Assert.Equal(11, result.Snapshot.Units.Single(u => u.Id == 2).CorpsId);
        Assert.Equal(11, result.Snapshot.Units.Single(u => u.Id == 3).CorpsId);
        Assert.Equal(10, result.Snapshot.Settlements.Single(s => s.Id == 4).CorpsId);
        Assert.Equal(11, result.Snapshot.Settlements.Single(s => s.Id == 5).CorpsId);
        Assert.Equal(11, result.Snapshot.Settlements.Single(s => s.Id == 6).CorpsId);
        Assert.Equal(10, result.Snapshot.Settlements.Single(s => s.Id == 7).CorpsId);
        Assert.All(result.Snapshot.Units, u => Assert.Equal(new WorldPosition(999, 999), u.Position));
        var treasury = before.Corps.Single(c => c.Id == 11).Treasury!;
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Money + treasury.Money - 50, result.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Money);
        Assert.Equal(before.Corps.Single(c => c.IsRoot).Treasury.Food + treasury.Food - 300, result.Snapshot.Corps.Single(c => c.IsRoot).Treasury.Food);
    }

    [Fact]
    public void ChangingSettingsWithoutChangingCitiesPreservesAllTerritoryAndServiceUnits()
    {
        var engine = Engine();
        var before = engine.Snapshot;
        var result = engine.Execute(new("controller"), new ChangeCorpsCommand(Guid.NewGuid(), 1, 0,
            11, [2], 50, 300, new DomesticDelegation(), CorpsControlMode.Cooperative));

        Assert.True(result.Submission.Accepted);
        Assert.Equal(before.Settlements, result.Snapshot.Settlements);
        Assert.Equal(before.Units, result.Snapshot.Units);
        var corps = result.Snapshot.Corps.Single(c => c.Id == 11);
        Assert.Equal(CorpsControlMode.Cooperative, corps.ControlMode);
        Assert.IsType<DomesticDelegation>(corps.Delegation);
        Assert.Equal(50u, corps.Treasury.Money);
        Assert.Equal(300u, corps.Treasury.Food);
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
        var before = engine.Snapshot;
        var result = engine.Execute(new("controller"), new ChangeCorpsCommand(Guid.NewGuid(), 1, 0,
            branch == 0 ? 10 : 11, branch == 1 ? [1] : branch == 2 ? [2, 2] : [3],
            branch == 3 ? uint.MaxValue : 0, 0, branch == 5 ? new DefeatForceDelegation(1) : new GeneralDelegation(), CorpsControlMode.Direct));
        Assert.False(result.Submission.Accepted);
        Assert.Empty(result.Events);
        StateAssert.Equal(before, engine.Snapshot);
    }
}
