using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class CreateCorpsTests
{
    [Fact]
    public void ExplicitGovernorUsesTheSelectedEligiblePersonInsteadOfAutomaticPrecedence()
    {
        var engine = Engine();
        var result = engine.Execute(new("player"), new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [2], 100, 200, new GeneralDelegation(), CorpsControlMode.Automatic)
            { GovernorId = 2 });
        Assert.True(result.Submission.Accepted);
        Assert.Equal(2, result.Snapshot.Corps.Single(c => !c.IsRoot).LeaderId);
        Assert.Equal(OfficerStatus.Governor, result.Snapshot.Officers.Single(p => p.Id == 2).Status);
    }

    [Fact]
    public void OmittedCorpsReferenceMeansRootForCreationAsForPublicControlAndAccounts()
    {
        var snapshot = Engine().Snapshot;
        var world = WorldState.Restore(snapshot with { Settlements = snapshot.Settlements.Select(s => s with { CorpsId = null }).ToArray() });
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, new PersonnelRulebook(
            [new(1, new(0, 0)), new(2, new(100, 100)), new(20, new(110, 110))],
            new Dictionary<int, int> { [1] = 1, [2] = 2, [20] = 2 }));
        var result = engine.Execute(new("player"), new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [2], 100, 200, new GeneralDelegation(), CorpsControlMode.Automatic)
            { GovernorId = 2 });
        Assert.True(result.Submission.Accepted);
        int child = result.Snapshot.Corps.Single(c => !c.IsRoot).Id;
        Assert.Equal(child, result.Snapshot.Settlements.Single(s => s.Id == 20).CorpsId);
        Assert.Null(result.Snapshot.Settlements.Single(s => s.Id == 1).CorpsId);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(999)]
    [InlineData(3)]
    public void ExplicitGovernorOutsideEligibleTerritoryRejectsAtomically(int person)
    {
        var engine = Engine(rulerAtFacility: true);
        var before = engine.Snapshot;
        var result = engine.Execute(new("player"), new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [2], 100, 200, new GeneralDelegation(), CorpsControlMode.Automatic)
            { GovernorId = person });
        Assert.False(result.Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
    }

    private static SimulationEngine Engine(bool rulerAtFacility = false)
    {
        var world = WorldState.Create(new(190, 1, 1), 1,
            [new(1, 0) { LeaderId = 1 }],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(2, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10 },
             new(20, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { CorpsId = 10, Kind = SettlementKind.Facility }],
            [new(1, 1, rulerAtFacility ? 20 : 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
             new(2, 1, 2, new(50, 50, 50, 50), false),
             new(3, 1, 20, new(50, 50, 50, 100), false)],
            corps: [new(10, 1, 1, new(1000, 1000)) { IsRoot = true }], controls: [new("player", 1, ControllerKind.Player)]);
        return new(world, EconomyRulebook.Empty, new PersonnelRulebook(
            [new(1, new(0, 0)), new(2, new(100, 100)), new(20, new(110, 110))],
            new Dictionary<int,int> { [1] = 1, [2] = 2, [20] = 2 }));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void InvalidCitySetsOrFundingRejectWithoutAllocatingIdentityOrChangingState(int branch)
    {
        var engine = Engine();
        var command = new CreateCorpsCommand(Guid.NewGuid(), 1, 0, branch switch
        { 0 => [], 1 => [1], 2 => [1, 2], 3 => [2, 2], _ => [2] }, branch == 4 ? uint.MaxValue : 0, 0, new GeneralDelegation(), CorpsControlMode.Automatic);
        var before = engine.Snapshot;
        var result = engine.Execute(new("player"), command);
        Assert.False(result.Submission.Accepted);
        Assert.Empty(result.Events);
        StateAssert.Equal(before, engine.Snapshot);
    }

    [Fact]
    public void InvalidDelegationRejectsBeforeAllocatingResourcesOrCorpsIdentity()
    {
        var engine = Engine();
        var before = engine.Snapshot;
        var result = engine.Execute(new("player"), new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [2], 100, 200, new DefeatForceDelegation(1), CorpsControlMode.Automatic));
        Assert.False(result.Submission.Accepted);
        Assert.Empty(result.Events);
        StateAssert.Equal(before, result.Snapshot);
    }

    [Fact]
    public void RulerFacilityStaysWithRootAndItsOtherPeopleCannotBecomeGovernor()
    {
        var engine = Engine(true);
        var command = new CreateCorpsCommand(Guid.NewGuid(), 1, 0, [2], 0, 0, new DomesticDelegation(), CorpsControlMode.Automatic);
        var before = engine.Snapshot;
        Assert.True(engine.Validate(new("player"), command).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted);
        Assert.Equal(2, result.Snapshot.Corps.Single(c => c.Id == 11).LeaderId);
        Assert.IsType<DomesticDelegation>(result.Snapshot.Corps.Single(c => c.Id == 11).Delegation);
        Assert.Equal(10, result.Snapshot.Settlements.Single(s => s.Id == 20).CorpsId);
        Assert.Equal(11, result.Snapshot.Settlements.Single(s => s.Id == 2).CorpsId);
    }
}
