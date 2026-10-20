using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Core.Tests;

public sealed class CrossCorpsArrivalTests
{
    private static MilitaryRulebook Rules => new(new(10, 3, Enumerable.Repeat(0, 30).ToImmutableArray(),
        [new(0, 1, null)], [new(7, 20, 0, 10000, 5, new(1, 1), TravelMode.Land)],
        Enumerable.Range(1, 4).Select(id => new BattlefieldPlace(id, new(id * 10 + 2, 7), new(1, 1))).ToImmutableArray()));

    private static WorldState World(bool city = true, bool people = true, bool transport = false, bool ruler = true,
        uint rootMoney = 10000, bool service = true, bool facility = false)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100) { LeaderId = 1 }],
        [new(1, 1, 1000, 100, 100, 70, 500, 0, 100, 0, 100) { CorpsId = 10 },
         new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100)
            { CorpsId = 11, Kind = facility ? SettlementKind.Facility : SettlementKind.City },
         new(3, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100) { CorpsId = city ? 11 : 10 },
         new(4, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100) { CorpsId = 11, Kind = SettlementKind.Facility }],
        [new(1, 1, ruler ? null : 1, new(70, 70, 70, 70), false) { UnitId = ruler ? 0 : null, Status = OfficerStatus.Ruler },
         new(2, 1, 2, new(80, 80, 80, 80), false) { Status = OfficerStatus.Governor },
         new(3, 1, people ? 3 : 2, new(60, 60, 60, 60), false),
         new(4, 1, null, new(60, 60, 60, 60), false) { UnitId = 0 }],
        units:
        [new(0, 1, new(21, 7), 400) { CorpsId = 10, ServicePlaceId = service ? 1 : null, CommanderId = ruler ? 1 : 4,
            FormationId = 7, Morale = 70, CargoMoney = 200, CargoFood = 300,
            Order = new(transport ? MilitaryOrderKind.Transport : MilitaryOrderKind.Return, null, 2) },
         new(5, 1, new(25, 7), 100) { CorpsId = 11, ServicePlaceId = 2, FormationId = 7, Order = new(MilitaryOrderKind.Return, null, 3) },
         new(6, 1, new(27, 7), 100) { CorpsId = 11, ServicePlaceId = 2, FormationId = 7 },
         new(7, 1, new(29, 7), 100) { CorpsId = 11, ServicePlaceId = 4, FormationId = 7, Order = new(MilitaryOrderKind.Return, null, 3) },
         new(8, 1, new(31, 7), 100) { CorpsId = 11, ServicePlaceId = 2, IsPersonnel = true }],
        controls: [new("player", 1, ControllerKind.Player)],
        corps: [new(10, 1, 1, new(rootMoney, 10000)) { IsRoot = true },
            new(11, 1, 2, new(2000, 3000)) { ControlMode = CorpsControlMode.Automatic }]);
        var mutation = world.BeginMutation();
        mutation.SetSimulation(mutation.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(mutation);
        return world;
    }

    private static List<DomainEvent> Arrive(WorldState world)
    {
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        MilitaryMovement.Absorb(mutation, mutation.Units[0], 2, new GovernanceRulebook([]), PersonnelRulebook.Empty, Rules, events);
        mutation.MaintainDerivedState(events);
        world.Commit(mutation);
        return events;
    }

    [Theory]
    [InlineData(true, true, false)]
    [InlineData(false, true, false)]
    [InlineData(true, false, false)]
    [InlineData(true, true, true)]
    public void RulerArrivalTransfersOnlyTheDestinationAndFollowingUnitsThenRepairsOldCorps(bool city, bool people, bool facility)
    {
        var world = World(city, people, facility: facility);
        var events = Arrive(world);
        var state = world.CreateSnapshot();
        bool survives = city && people;
        Assert.Equal(10, state.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.Equal(2, state.Officers.Single(p => p.Id == 1).SettlementId);
        Assert.Equal(OfficerStatus.Serving, state.Officers.Single(p => p.Id == 2).Status);
        Assert.DoesNotContain(state.Units, u => u.Id == 0);
        Assert.All(state.Units.Where(u => u.Id is 5 or 8), u => Assert.Equal(10, u.CorpsId));
        Assert.All(state.Units.Where(u => u.Id is 6 or 7), u => Assert.Equal(survives ? 11 : 10, u.CorpsId));
        Assert.Equal(survives ? 11 : 10, state.Settlements.Single(s => s.Id == 4).CorpsId);
        Assert.Equal(10, Assert.Single(events.OfType<UnitAbsorbedEvent>()).Result!.CorpsId);
        Assert.True(CorpsControl.CanControlSettlement(state, Assert.Single(state.Simulation.Controls), new("player"), 2));
        if (survives)
        {
            Assert.Equal(3, state.Corps.Single(c => c.Id == 11).LeaderId);
            Assert.Single(events.OfType<CorpsLeaderChangedEvent>());
            Assert.Empty(events.OfType<CorpsDissolvedEvent>());
        }
        else
        {
            Assert.Single(state.Corps);
            Assert.Single(events.OfType<CorpsDissolvedEvent>());
            Assert.Empty(events.OfType<CorpsLeaderChangedEvent>());
        }
        Assert.Equal(survives ? 10200u : 12200u, state.Corps.Single(c => c.IsRoot).Treasury.Money);
        StateAssert.Equal(state, WorldState.Restore(state).CreateSnapshot());
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, true)]
    public void OrdinaryCommanderAndReturningTransportKeepTheReceivingCorps(bool transport, bool ruler)
    {
        var world = World(transport: transport, ruler: ruler);
        Arrive(world);
        var state = world.CreateSnapshot();
        Assert.Equal(11, state.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.Equal(transport ? 1 : 2, state.Officers.Single(p => p.Id == 4).SettlementId);
        Assert.All(state.Units, u => Assert.Equal(11, u.CorpsId));
        Assert.Equal(transport ? 2 : 0, state.Simulation.Tasks.Length);
    }

    [Fact]
    public void TransportWithoutAServiceBuildingStationsItsRulerAndTransfersTheDestination()
    {
        var world = World(transport: true, service: false);
        Arrive(world);
        var state = world.CreateSnapshot();
        Assert.Equal(10, state.Settlements.Single(s => s.Id == 2).CorpsId);
        Assert.Equal(2, state.Officers.Single(p => p.Id == 1).SettlementId);
        Assert.Empty(state.Simulation.Tasks);
    }

    [Fact]
    public void DissolvingTheOldCorpsRedirectsExistingPersonnelReturnReferences()
    {
        var world = World(city: false, people: false);
        var mutation = world.BeginMutation();
        PersonnelReturns.Schedule(mutation, new("system", 1, 0), 3, 4, 2, 3, true, [],
            new(new(42, 7), new(22, 7), false), true, 11);
        mutation.SetSimulation(mutation.Simulation with { NextSequence = 1 });
        world.Commit(mutation);
        Arrive(world);
        var state = world.CreateSnapshot();
        Assert.DoesNotContain(state.Corps, c => c.Id == 11);
        Assert.Equal(10, Assert.Single(state.Simulation.Tasks).Journey!.ReturnLeg!.CorpsId);
        Assert.Equal(2, Assert.Single(state.Simulation.Tasks).Journey!.DestinationPlaceId);
    }

    [Fact]
    public void RulerArrivalThatWouldOverflowADissolvedTreasuryLeavesTheWorldUntouched()
    {
        var world = World(city: false, rootMoney: uint.MaxValue);
        var before = world.CreateSnapshot();
        var events = Arrive(world);
        StateAssert.Equal(before, world.CreateSnapshot());
        Assert.Equal("absorption-blocked", Assert.Single(events.OfType<UnitOrderChangedEvent>()).Reason);
        Assert.Empty(events.OfType<UnitAbsorbedEvent>());
    }
}
