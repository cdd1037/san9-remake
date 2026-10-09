using San9.Application.Presentation;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Application.Tests;

public sealed class SpecialUnitCommandTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SpecialUnitRetargetingPreservesMembersResourcesAndCargo(bool transport)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "special-orders", 190, 1, 1, 1,
            [new(1, "force", 1, 100) { LeaderId = 11 }],
            [new(1, "city", 1, 0, 0, 20000, 600, 600, 100, []),
                new(2, "destination", 1, 5, 0, 100, 600, 600, 100, [])],
            new() { [1] = "city", [2] = "destination", [8000] = "region" },
            new() { [1] = 1, [2] = 2, [8000] = 1 })
        {
            Officers = [new("officer", new(11, 1, null, new(100, 100, 100, 100), false)
                { Status = OfficerStatus.Ruler, UnitId = 0 })],
            Units = [new(0, 1, new(7, 2), 1000) { CommanderId = 11, ServicePlaceId = 1,
                FormationId = transport ? 3 : 2, Morale = 100, CargoFood = 500, CargoMoney = 200,
                Order = transport ? new(MilitaryOrderKind.Transport, null, 2)
                    : new(MilitaryOrderKind.Construct, null, null) { Construction = new(8000, 9000, new(17, 2), 11) } }],
            UnbuiltPlaces = [new(8000, new(17, 2))],
            Military = new(6, 1, [0, 0, 0, 0, 0, 0], [new(0, 1, null)],
                [new(2, 5, 0, 100000, 1, new(1, 1), TravelMode.Land),
                    new(3, 5, 0, 100000, 1, new(1, 1), TravelMode.Land)],
                [new(1, new(2, 2), new(1, 1)) { FacilityTypeId = 0 }, new(2, new(27, 2), new(1, 1)) { FacilityTypeId = 0 }])
            {
                ConstructionFormations = new(2, null), TransportFormations = new(3, null),
                Facilities = [new(0, 0, 600, new(1, 1), 15, false, true, true, 500000, null, null, null, []),
                    new(9000, 1000, 600, new(3, 3), 15, true, true, true, 250000, null, null, null, [])],
                ConstructionSites = [new(8000, new(17, 2), [9000])]
            },
            Information = new([], [], [], [new CorpsState(1, 1, 11, new(10000, 1000000)) { IsRoot = true }], [])
        };
        var session = GameSession.Start(definition, 42);
        int id = 0;
        var before = session.Snapshot;
        var oldUnit = before.Units.Single(u => u.Id == id);
        var targets = new UnitCommandTargets(session.View, id, transport ? UnitCommandKind.Transport : UnitCommandKind.March);
        Assert.All(targets.Candidates, c => { Assert.False(c.Key.IsUnit); Assert.Equal(MilitaryMarker.Enter, c.Value); });
        Assert.Throws<ArgumentException>(() => new UnitCommandTargets(session.View, id, UnitCommandKind.Attack));
        Assert.True(targets.Select(new(1, false)));
        Assert.True(targets.BeginPolicies(), $"{targets.Error}; deployed at {oldUnit.Position}");
        Assert.False(targets.SetDepartureDelay(1));
        targets.SetPolicies(targets.Policies with { AutonomousRetreat = false });
        targets.ReturnToTargets();
        Assert.Equal(before.StateHash, session.Snapshot.StateHash);
        Assert.True(targets.BeginPolicies());
        var command = targets.CreateCommand(Guid.NewGuid(), session.Snapshot.Simulation.NextSequence);
        Assert.Equal(transport ? MilitaryOrderKind.Transport : MilitaryOrderKind.Return, command.Order.Kind);
        var result = session.Execute(new("player"), command);
        Assert.True(result.Submission.Accepted, result.Submission.Rejection.ToString());
        var unit = session.Snapshot.Units.Single(u => u.Id == id);
        Assert.Equal(oldUnit.Position, unit.Position);
        Assert.Equal(oldUnit.CommanderId, unit.CommanderId);
        Assert.Equal(oldUnit.Soldiers, unit.Soldiers);
        Assert.Equal(oldUnit.CargoFood, unit.CargoFood);
        Assert.Equal(oldUnit.CargoMoney, unit.CargoMoney);
        Assert.Equal(before.Corps, session.Snapshot.Corps);
        Assert.False(unit.Policies.AutonomousRetreat);
        Assert.Equal(1, unit.Order!.SettlementId);
    }
}
