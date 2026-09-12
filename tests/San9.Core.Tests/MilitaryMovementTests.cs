using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryMovementTests
{
    private static readonly CommandContext Player = new("player");
    private static MilitaryRulebook Rules(int speed = 1) => new(new(8, 3,
        Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
        [new(7, speed, 50, 10000, 5, new(1, 1), TravelMode.Land)],
        [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]));

    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 10000, 10000, 100)],
        [new(1, 1, 1000, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100)],
        [new(11, 1, 1, new(70, 70, 70, 70), false)], controls: [new("player", 1, ControllerKind.Player)]);
    private static SimulationEngine Engine(WorldState world, MilitaryRulebook rules) =>
        new(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
    private static DeployUnitCommand Deploy(long sequence = 0, uint soldiers = 400, MilitaryOrder? order = null) =>
        new(Guid.NewGuid(), 1, sequence, 1, [11], soldiers, 7, order ?? new(MilitaryOrderKind.Transport, null, 2), 200, 300);

    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        engine.EndControl(Player);
        return engine.AdvanceXun();
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(true, false)]
    [InlineData(false, true)]
    public void CrossCorpsArrivalRepairsLeadershipOrWaitsForTreasuryCapacity(bool successor, bool overflow)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, overflow ? uint.MaxValue : 10000, 10000, 0) { LeaderId = 12 }],
            [new(1, 1, 100, 100, 100, 70, 500, 0, 100, 0, 100) { CorpsId = 11 },
             new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100) { CorpsId = 10 }],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0, Status = OfficerStatus.Governor },
             new(12, 1, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
             new(13, 1, successor ? 1 : 2, new(60, 60, 60, 60), false)],
            units: [new(0, 1, new(36, 7), 400) { CorpsId = 11, ServicePlaceId = 1, FormationId = 7,
                Morale = 70, CargoMoney = 200, CargoFood = 300, Order = new(MilitaryOrderKind.Transport, null, 2) }],
            corps: [new(10, 1, 12, null), new(11, 1, 11, new(2000, 3000))]);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Equal(CommandRejection.None, GovernanceMaintenance.RepairCorps(mutation, 11, new GovernanceRulebook([]), events));
        Assert.Equal(11, mutation.Corps[11].LeaderId);
        Assert.Empty(events);
        MilitaryMovement.AdvanceDay(mutation, Rules(10), new GovernanceRulebook([]), events);
        world.Commit(mutation);
        var state = world.CreateSnapshot();
        if (overflow)
        {
            var unit = Assert.Single(state.Units);
            Assert.Equal(200u, unit.CargoMoney);
            Assert.Equal(400u, unit.Soldiers);
            Assert.Equal(0, state.Officers.Single(p => p.Id == 11).UnitId);
            Assert.Equal(2, state.Corps.Count);
            Assert.Empty(events.OfType<UnitAbsorbedEvent>());
            Assert.Contains(events.OfType<UnitOrderChangedEvent>(), e => e.Reason == "absorption-blocked");
        }
        else
        {
            Assert.Empty(state.Units);
            Assert.Equal(2, state.Officers.Single(p => p.Id == 11).SettlementId);
            Assert.Equal(OfficerStatus.Serving, state.Officers.Single(p => p.Id == 11).Status);
            Assert.Equal(500u, state.Settlements.Single(s => s.Id == 2).Soldiers);
            Assert.Single(events.OfType<UnitAbsorbedEvent>());
            if (successor)
            {
                Assert.Equal(13, state.Corps.Single(c => c.Id == 11).LeaderId);
                Assert.Equal(10200u, state.Forces[0].Money);
            }
            else
            {
                Assert.Single(state.Corps);
                Assert.Equal(12200u, state.Forces[0].Money);
                Assert.Equal(13300u, state.Forces[0].Food);
            }
        }
    }

    [Fact]
    public void DeploymentMovesPersonnelAndResourcesThenTransportAbsorbsExactlyOnce()
    {
        var engine = Engine(World(), Rules(10));
        var before = engine.Snapshot;
        var result = engine.Execute(Player, Deploy());
        Assert.True(result.Submission.Accepted);
        var unit = Assert.Single(result.Snapshot.Units);
        Assert.Equal(600u, result.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        var person = Assert.Single(result.Snapshot.Officers);
        Assert.Null(person.SettlementId);
        Assert.Equal(unit.Id, person.UnitId);
        Assert.Equal(before.Forces[0].Money - 250, result.Snapshot.Forces[0].Money);
        Assert.Equal(before.Forces[0].Food - 300, result.Snapshot.Forces[0].Food);
        Assert.Equal(DevelopmentRejection.OfficerUnavailable,
            engine.Execute(Player, new DevelopCommand(Guid.NewGuid(), 1, 1, new(1, 1, [11], DomesticActivity.Commerce))).Development!.Value.Rejection);
        var advanced = Advance(engine);
        Assert.Single(advanced.Events.OfType<UnitAbsorbedEvent>());
        Assert.Empty(engine.Snapshot.Units);
        Assert.Equal(2, engine.Snapshot.Officers[0].SettlementId);
        Assert.Null(engine.Snapshot.Officers[0].UnitId);
        Assert.Equal(500u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.Equal(62, engine.Snapshot.Settlements.Single(s => s.Id == 2).Morale);
        Assert.Empty(Advance(engine).Events.OfType<UnitAbsorbedEvent>());
    }

    [Fact]
    public void InFlightWorldRestoreContinuesWithIdenticalMovementAndEvents()
    {
        var rules = Rules();
        var first = Engine(World(), rules);
        Assert.True(first.Execute(Player, Deploy()).Submission.Accepted);
        Advance(first);
        Assert.NotEmpty(first.Snapshot.Units);
        var json = CanonicalJson.Text(first.CaptureStableState());
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(json, CanonicalJson.Options)!;
        var restored = Engine(WorldState.Restore(snapshot), rules);
        for (int xun = 0; xun < 4; xun++)
        {
            var originalEvents = Advance(first).Events;
            var restoredEvents = Advance(restored).Events;
            Assert.Equal(CanonicalJson.Text(originalEvents), CanonicalJson.Text(restoredEvents));
            Assert.Equal(first.Snapshot.StateHash, restored.Snapshot.StateHash);
        }
        Assert.Empty(first.Snapshot.Units);
    }

    [Fact]
    public void RejectedDeploymentDoesNotSpendMoneySoldiersPersonnelOrRandom()
    {
        var engine = Engine(World(), Rules());
        string before = engine.Snapshot.StateHash;
        Assert.False(engine.Execute(Player, Deploy(soldiers: 1001)).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.False(engine.Execute(Player, Deploy() with { OfficerIds = [11, 11] }).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.False(engine.Execute(Player, Deploy() with { FormationId = 999 }).Submission.Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
        Assert.True(engine.Validate(Player, Deploy()).Accepted);
        Assert.Equal(before, engine.Snapshot.StateHash);
    }

    [Fact]
    public void LostTransportDestinationReturnsToHomeWithoutUnloadingToNewOwner()
    {
        var world = World();
        var rules = Rules();
        var engine = Engine(world, rules);
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { OwnerId = null });
        world.Commit(mutation);
        var events = Advance(engine).Events;
        Assert.Contains(events.OfType<UnitOrderChangedEvent>(), e => e.Reason == "target-invalid" && e.Order?.SettlementId == 1);
        Assert.Single(events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(100u, world.CreateSnapshot().Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.Equal(1000u, world.CreateSnapshot().Settlements.Single(s => s.Id == 1).Soldiers);
    }

    [Fact]
    public void DynamicBlockerWaitsAcrossBoundaryAndMovementResumesAfterItLeaves()
    {
        var world = World();
        var mutation = world.BeginMutation();
        mutation.AddUnit(new(0, 1, new(37, 7), 100) { FormationId = 7 });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 1 });
        world.Commit(mutation);
        var engine = Engine(world, Rules(10));
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var departed = engine.Snapshot.Units.Single(u => u.Id == 1).Position;
        var blocked = Advance(engine);
        Assert.Equal(departed, engine.Snapshot.Units.Single(u => u.Id == 1).Position);
        Assert.Equal(10, blocked.Events.OfType<UnitMovedEvent>().Count(e => e.UnitId == 1 && !e.Reachable));
        mutation = world.BeginMutation();
        mutation.RemoveUnit(0);
        world.Commit(mutation);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), Rules(10));
        Assert.Equal(CanonicalJson.Text(Advance(engine).Events), CanonicalJson.Text(Advance(restored).Events));
        Assert.Empty(engine.Snapshot.Units);
    }

    [Fact]
    public void LosingBothDestinationAndHomeClearsOrderAndRetainsCargoAndMembers()
    {
        var world = World();
        var engine = Engine(world, Rules());
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { OwnerId = null });
        mutation.Replace(mutation.Settlements[2] with { OwnerId = null });
        world.Commit(mutation);
        var events = Advance(engine).Events;
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Null(unit.Order);
        Assert.Equal(200u, unit.CargoMoney);
        Assert.Equal(300u, unit.CargoFood);
        Assert.Equal(unit.Id, engine.Snapshot.Officers[0].UnitId);
        Assert.Single(events.OfType<UnitOrderChangedEvent>());
        Assert.Empty(events.OfType<UnitAbsorbedEvent>());
    }
}
