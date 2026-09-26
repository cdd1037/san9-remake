using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class FacilityDemolitionTests
{
    private static readonly CommandContext Player = new("player");
    private static SimulationEngine Engine(bool allowed = true, bool friendlyCity = true, uint soldiers = 1000)
    {
        var military = new MilitaryRulebook(new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
            [new(1, 5, 0, 10000, 1, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 2), new(1, 1)), new(2, new(12, 2), new(1, 1)) { CanDemolish = allowed },
                new(3, new(27, 2), new(1, 1))]) { TransportFormations = new(1, null) });
        var geography = new PersonnelRulebook([new(1, new(2, 2)), new(2, new(12, 2)), new(3, new(27, 2))],
            new Dictionary<int, int> { [1] = 1, [2] = 1, [3] = 3 });
        return new(WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 21 }],
            [new(1, friendlyCity ? 1 : 2, soldiers, 100, 100, 80, 0, 0, 100, 0, 100) { Wounded = 49990 },
                new(2, 1, 100, 100, 100, 50, 0, 0, 100, 0, 100) { Kind = SettlementKind.Facility, Wounded = 20 },
                new(3, 2, 100, 100, 100, 100, 0, 0, 100, 0, 100)],
            [new(11, 1, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
                new(12, 2, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Captive },
                new(21, 2, 3, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
                new(13, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, new(7, 2), 100) { FormationId = 1, Morale = 100, ServicePlaceId = 2,
                CargoFood = 100, Order = new(MilitaryOrderKind.Transport, null, 2) }],
            controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, 11, new(10000, 10000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 21, new(10000, 10000)) { IsRoot = true }]), EconomyRulebook.Empty, geography,
            TreasureRulebook.Empty, military: military);
    }

    [Theory]
    [InlineData(1000u)]
    [InlineData(500000u)]
    public void DemolitionReturnsSoldiersAndWoundedToDraftPopulationWithoutDeliveringCargo(uint initial)
    {
        var engine = Engine(soldiers: initial);
        var command = new DemolishFacilityCommand(Guid.NewGuid(), 1, 0, 2);
        var result = engine.Execute(Player, command);
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<FacilityDemolishedEvent>());
        var city = engine.Snapshot.Settlements.Single(s => s.Id == 1);
        Assert.Equal(initial, city.Soldiers);
        Assert.Equal(49990u, city.Wounded);
        Assert.Equal(120u, city.DraftPopulation);
        Assert.Equal(80, city.Morale);
        Assert.All(engine.Snapshot.Officers.Where(p => p.Id is 11 or 12), p => Assert.Equal(1, p.SettlementId));
        Assert.Equal(OfficerStatus.Captive, engine.Snapshot.Officers.Single(p => p.Id == 12).Status);
        var facility = engine.Snapshot.Settlements.Single(s => s.Id == 2);
        Assert.True(facility.IsDestroyed);
        Assert.Null(facility.OwnerId);
        Assert.Equal(0u, facility.Soldiers + facility.Wounded);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Null(unit.Order);
        Assert.Equal(100u, unit.CargoFood);
        Assert.DoesNotContain(result.Events, e => e is UnitAbsorbedEvent);
        Assert.All(result.Events.OfType<ResourceChangedEvent>().Where(e => e.Reason == "facility-demolished"), e => Assert.Equal(0, e.SaturationLoss));
        string hash = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(Player, command).Submission.Accepted);
        Assert.False(engine.Execute(Player, command with { CommandId = Guid.NewGuid(), Sequence = 1 }).Submission.Accepted);
        Assert.Equal(hash, engine.Snapshot.StateHash);
    }

    [Theory]
    [InlineData(false, true)]
    [InlineData(true, false)]
    public void ProtectedFacilityOrMissingFriendlyReceivingCityRejectsAtomically(bool allowed, bool friendlyCity)
    {
        var engine = Engine(allowed, friendlyCity);
        string hash = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(Player, new DemolishFacilityCommand(Guid.NewGuid(), 1, 0, 2)).Submission.Accepted);
        Assert.Equal(hash, engine.Snapshot.StateHash);
    }

    [Fact]
    public void TravelingOfficerGetsARealReturnLegWhenServiceFacilityIsDemolished()
    {
        var engine = Engine();
        Assert.True(engine.Execute(Player, new SearchCommand(Guid.NewGuid(), 1, 0, 11, 2)).Submission.Accepted);
        var result = engine.Execute(Player, new DemolishFacilityCommand(Guid.NewGuid(), 1, 1, 2));
        Assert.True(result.Submission.Accepted);
        Assert.Single(result.Events.OfType<TaskCancelledEvent>());
        Assert.Equal("personnel-return", Assert.Single(engine.Snapshot.Simulation.Tasks).Kind);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
        engine.EndControl(Player);
        engine.AdvanceXun();
        Assert.Empty(engine.Snapshot.Simulation.Tasks);
        Assert.Equal(1, engine.Snapshot.Officers.Single(p => p.Id == 11).SettlementId);
    }
}
