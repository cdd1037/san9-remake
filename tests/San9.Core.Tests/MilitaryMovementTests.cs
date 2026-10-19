using San9.Tests;
using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MilitaryMovementTests
{
    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ImpassableWaypointIsRejectedByDeploymentAndReassignment(bool water)
    {
        var rules = new MilitaryRulebook(new(8, 3,
            Enumerable.Range(0, 24).Select(i => water && i == 2 ? 1 : 0).ToImmutableArray(),
            [new(0, 1, null), new(1, null, 1) { IsWater = true }],
            [new(7, 10, 0, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))])
            { BlockedCells = water ? [] : [new(2, 0)] });
        var engine = Engine(World(), rules);
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null)) { Waypoint = new(12, 2) };
        var before = engine.Snapshot;
        Assert.Equal(CommandRejection.NoDestination, engine.Execute(Player, command).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Execute(Player, command with { CommandId = Guid.NewGuid(), Waypoint = null }).Submission.Accepted);
        before = engine.Snapshot;
        var unit = Assert.Single(before.Units);
        var redirect = new OrderUnitCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, unit.Id,
            new(MilitaryOrderKind.Return, null, 1)) { Waypoint = command.Waypoint };
        Assert.Equal(CommandRejection.InvalidTask, engine.Execute(Player, redirect).Submission.Rejection);
        StateAssert.Equal(before, engine.Snapshot);
        StateAssert.Equal(before, Engine(WorldState.Restore(engine.CaptureStableState()), rules).Snapshot);
    }

    [Fact]
    public void OrderAtomicallyReplacesTargetPoliciesAndWaypointWithoutRedeployment()
    {
        var engine = Engine(World(), Rules());
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var before = engine.Snapshot;
        var unit = Assert.Single(before.Units);
        var policies = new UnitPolicies(true, true, false, true);
        var command = new OrderUnitCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, unit.Id,
            new(MilitaryOrderKind.Return, null, 1)) { Policies = policies, Waypoint = new(12, 2) };
        Assert.Equal(CommandRejection.None, engine.GetPreparationEligibility(Player, command));
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(Player, command);
        Assert.True(result.Submission.Accepted);
        var ordered = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(unit.Id, ordered.Id); Assert.Equal(unit.Position, ordered.Position);
        Assert.Equal(unit.Soldiers, ordered.Soldiers); Assert.Equal(unit.CargoMoney, ordered.CargoMoney);
        Assert.Equal(command.Order, ordered.Order); Assert.Equal(policies, ordered.Policies);
        Assert.Equal(command.Waypoint, ordered.Waypoint); Assert.Equal(0, ordered.DepartureDaysRemaining);
        Assert.Equal(CanonicalJson.Text(before.Settlements), CanonicalJson.Text(engine.Snapshot.Settlements));
        Assert.Equal(CanonicalJson.Text(before.Officers), CanonicalJson.Text(engine.Snapshot.Officers));
        Assert.Single(result.Events.OfType<UnitPoliciesChangedEvent>());
        Assert.Single(result.Events.OfType<UnitOrderChangedEvent>());
    }

    [Fact]
    public void InvalidOrderWaypointRejectsPolicyAndTargetChangesTogether()
    {
        var engine = Engine(World(), Rules()); Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var before = engine.Snapshot; var unit = Assert.Single(before.Units);
        var invalid = new OrderUnitCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, unit.Id,
            new(MilitaryOrderKind.Return, null, 1)) { Policies = new(true, true, true, true), Waypoint = Rules().Places[2].Position };
        Assert.Equal(engine.Validate(Player, invalid).Rejection, engine.GetPreparationEligibility(Player, invalid));
        StateAssert.Equal(before, engine.Snapshot);
        var result = engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, before.Simulation.NextSequence, unit.Id,
            new(MilitaryOrderKind.Return, null, 1)) { Policies = new(true, true, true, true), Waypoint = Rules().Places[2].Position });
        Assert.False(result.Submission.Accepted); Assert.Empty(result.Events);
        StateAssert.Equal(before, engine.Snapshot);
    }

    private static readonly CommandContext Player = new("player");
    private static MilitaryRulebook Rules(int speed = 1) => new(new(8, 3,
        Enumerable.Repeat(0, 24).ToImmutableArray(), [new(0, 1, null)],
        [new(7, speed, 50, 10000, 5, new(1, 1), TravelMode.Land),
         new(70, speed, 50, 10000, 5, new(1, 1), TravelMode.Land)],
        [new(1, new(2, 7), new(1, 1)), new(2, new(37, 7), new(1, 1))]) { TransportFormations = new(70, null) });

    private static WorldState World() => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100)],
        [new(1, 1, 1000, 100, 100, 70, 500, 0, 100, 0, 100), new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100)],
        [new(11, 1, 1, new(70, 70, 70, 70), false)], controls: [new("player", 1, ControllerKind.Player)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(10000, 10000)) { IsRoot = true }]);
    private static SimulationEngine Engine(WorldState world, MilitaryRulebook rules) =>
        new(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
    private static TransportUnitCommand Deploy(long sequence = 0, uint soldiers = 400) =>
        new(Guid.NewGuid(), 1, sequence, 1, [11], soldiers, 2, 200, 300);

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(9)]
    public void DelayedDeploymentCreatesRealUnitImmediatelyAndWaitsExactDays(int delay)
    {
        var rules = Rules(); var engine = Engine(World(), rules);
        var command = new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null))
            { DepartureDelayDays = delay, Waypoint = new(12, 2) };
        Assert.True(engine.Execute(Player, command).Submission.Accepted);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.Equal(rules.Places[1].Position, unit.Position);
        Assert.Equal(delay, unit.DepartureDaysRemaining);
        Assert.False(unit.IsEmbedded);
        Assert.Equal(command.Waypoint, unit.Waypoint);
        Assert.Equal(600u, engine.Snapshot.Settlements.Single(s => s.Id == 1).Soldiers);
        Assert.Equal(unit.Id, engine.Snapshot.Officers.Single(p => p.Id == 11).UnitId);
        var state = WorldState.Restore(engine.CaptureStableState());
        var world = state.BeginMutation();
        for (int day = 0; day < delay; day++)
        {
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
            Assert.Equal(unit.Position, world.Units[unit.Id].Position);
            MilitaryMovement.TickDepartureDelays(world);
            Assert.Equal(delay - day - 1, world.Units[unit.Id].DepartureDaysRemaining);
        }
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
        Assert.NotEqual(unit.Position, world.Units[unit.Id].Position);
    }

    [Theory]
    [InlineData(-1, false)]
    [InlineData(10, false)]
    [InlineData(0, true)]
    public void InvalidDepartureOrWaypointIsRejectedAtomically(int delay, bool blockedWaypoint)
    {
        var engine = Engine(World(), Rules());
        var hash = engine.Snapshot;
        var result = engine.Execute(Player, new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null))
            { DepartureDelayDays = delay, Waypoint = blockedWaypoint ? new(2, 7) : null });
        Assert.False(result.Submission.Accepted);
        StateAssert.Equal(hash, engine.Snapshot); Assert.Empty(result.Events);
    }

    [Fact]
    public void DailyCycleEntryIsIndependentOfDepartureDelayAndSubsequentOrders()
    {
        var rules = Rules();
        var engine = Engine(World(), rules);
        Assert.True(engine.Execute(Player, new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null)) { DepartureDelayDays = 9 }).Submission.Accepted);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.False(unit.HasEnteredDailyCycle);
        engine = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.False(Assert.Single(engine.Snapshot.Units).HasEnteredDailyCycle);

        var world = WorldState.Restore(engine.CaptureStableState());
        var mutation = world.BeginMutation();
        MilitaryMovement.BeginDailyCycle(mutation);
        world.Commit(mutation);
        engine = Engine(world, rules);
        var entered = Assert.Single(engine.Snapshot.Units);
        Assert.True(entered.HasEnteredDailyCycle);
        Assert.Equal(unit.Position, entered.Position);
        Assert.Equal(9, entered.DepartureDaysRemaining);

        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence,
            unit.Id, new(MilitaryOrderKind.Move, new(27, 7), null))).Submission.Accepted);
        Assert.True(Assert.Single(engine.Snapshot.Units).HasEnteredDailyCycle);
        Assert.True(Assert.Single(engine.Snapshot.Units).CommandedThisXun);
        engine = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.True(Assert.Single(engine.Snapshot.Units).HasEnteredDailyCycle);
    }

    [Fact]
    public void CommandMarkerSurvivesRestoreAndResetsAtNewXun()
    {
        var rules = Rules();
        var engine = Engine(World(), rules);
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        Assert.True(Assert.Single(engine.Snapshot.Units).CommandedThisXun);
        engine = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        Assert.True(Assert.Single(engine.Snapshot.Units).CommandedThisXun);
        engine.EndControl(Player);
        engine.AdvanceXun();
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.False(unit.CommandedThisXun);
        var invalid = new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, unit.Id,
            new(MilitaryOrderKind.Move, new(900, 900), null));
        Assert.False(engine.Execute(Player, invalid).Submission.Accepted);
        Assert.False(Assert.Single(engine.Snapshot.Units).CommandedThisXun);
        Assert.True(engine.Execute(Player, invalid with { CommandId = Guid.NewGuid(), Order = new(MilitaryOrderKind.Return, null, 1) }).Submission.Accepted);
        Assert.True(Assert.Single(engine.Snapshot.Units).CommandedThisXun);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, false)]
    [InlineData(true, true)]
    public void ConfusedSourceRevalidatesDeploymentWithoutSideEffectsAndRecovers(bool facility, bool transport)
    {
        var world = World();
        if (facility)
        {
            var setup = world.BeginMutation();
            setup.Replace(setup.Settlements[1] with { Kind = SettlementKind.Facility });
            world.Commit(setup);
        }
        var rules = Rules(); var engine = Engine(world, rules);
        GameCommand command = transport ? Deploy() : new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 7,
            new(MilitaryOrderKind.Move, new(32, 7), null));
        Assert.True(engine.Validate(Player, command).Accepted);
        var mutation = world.BeginMutation();
        MilitaryConditions.Confuse(mutation, new(MilitaryOrderKind.AttackSettlement, null, 1), 12, []);
        world.Commit(mutation);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), rules);
        foreach (var current in new[] { engine, restored })
        {
            var before = current.Snapshot;
            Assert.Equal(CommandRejection.SettlementConfused, current.Validate(Player, command).Rejection);
            var result = current.Execute(Player, command);
            Assert.Equal(CommandRejection.SettlementConfused, result.Submission.Rejection);
            Assert.Empty(result.Events); StateAssert.Equal(before, current.Snapshot);
            Advance(current);
            Assert.Equal(2, current.Snapshot.Settlements.Single(s => s.Id == 1).ConfusionDaysRemaining);
            Assert.Equal(CommandRejection.SettlementConfused, current.Validate(Player, command with
                { Sequence = current.Snapshot.Simulation.NextSequence }).Rejection);
            Advance(current);
            Assert.Equal(0, current.Snapshot.Settlements.Single(s => s.Id == 1).ConfusionDaysRemaining);
            Assert.True(current.Execute(Player, command with { Sequence = current.Snapshot.Simulation.NextSequence }).Submission.Accepted);
            Assert.Single(current.Snapshot.Units);
        }
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
    }

    [Fact]
    public void ConfusedDestinationDoesNotPreventTransportOrExistingUnitOrders()
    {
        var world = World(); var engine = Engine(world, Rules());
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { ConfusionDaysRemaining = 12 }); world.Commit(mutation);
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[1] with { ConfusionDaysRemaining = 12 }); world.Commit(mutation);
        var unit = Assert.Single(engine.Snapshot.Units);
        Assert.True(engine.Execute(Player, new OrderUnitCommand(Guid.NewGuid(), 1,
            engine.Snapshot.Simulation.NextSequence, unit.Id, new(MilitaryOrderKind.Return, null, 1))).Submission.Accepted);
    }

    private static AdvanceXunResult Advance(SimulationEngine engine)
    {
        engine.EndControl(Player);
        return engine.AdvanceXun();
    }

    [Theory]
    [InlineData(30, 1)]
    [InlineData(31, 2)]
    [InlineData(115, 4)] // Tong Pass (332,322) to Luoyang (447,322).
    public void TransportReturnConsumesNextDailyStageWithoutAnExtraWaitingDay(int distance, int days)
    {
        var destination = new WorldPosition(332, 322);
        var home = new WorldPosition(332 + distance, 322);
        var rules = new MilitaryRulebook(new(100, 80,
            Enumerable.Repeat(0, 8000).ToImmutableArray(), [new(0, 1, null)],
            [new(70, 10, 50, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, home, new(1, 1)), new(2, destination, new(1, 1))])
            { TransportFormations = new(70, null) });
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100)],
            [new(1, 1, 1000, 100, 100, 70, 500, 0, 100, 0, 100),
             new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100)],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0 }],
            units: [new(0, 1, destination, 400) { ServicePlaceId = 1, FormationId = 70,
                Order = new(MilitaryOrderKind.Transport, null, 2) }],
            corps: [new(1, 1, null, new(10000, 10000)) { IsRoot = true }],
            controls: [new("player", 1, ControllerKind.Player)]);
        var engine = Engine(world, rules);
        engine.EndControl(Player);
        var arrival = engine.AdvanceDay();
        Assert.Single(arrival.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(new GameClock(190, 1, 2), engine.Snapshot.Clock);
        Assert.Equal(days, Assert.Single(engine.Snapshot.Simulation.Tasks).Journey!.ReturnLeg!.CountdownDays);
        for (int day = 1; day <= days; day++)
        {
            var result = engine.AdvanceDay();
            if (day < days)
            {
                Assert.Single(engine.Snapshot.Simulation.Tasks);
                Assert.Empty(result.Events.OfType<PersonnelReturnedEvent>());
            }
            else
            {
                Assert.Empty(engine.Snapshot.Simulation.Tasks);
                Assert.Equal(1, Assert.Single(result.Events.OfType<PersonnelReturnedEvent>()).DestinationId);
            }
        }
    }

    [Theory]
    [InlineData(MilitaryOrderKind.Return)]
    [InlineData(MilitaryOrderKind.Transport)]
    public void DeliveryOrdersRevalidateTargetsAndRejectDuplicateSubmissions(MilitaryOrderKind kind)
    {
        var world = World();
        var engine = Engine(world, Rules(10));
        var deploy = Deploy();
        Assert.True(engine.Execute(Player, deploy).Submission.Accepted);
        var order = new OrderUnitCommand(Guid.NewGuid(), 1, engine.Snapshot.Simulation.NextSequence, 0, new(kind, null, 2));
        Assert.True(engine.Validate(Player, order).Accepted);
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { OwnerId = null });
        world.Commit(mutation);
        var before = engine.Snapshot;
        var rejected = engine.Execute(Player, order);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        StateAssert.Equal(before, engine.Snapshot);
        mutation = world.BeginMutation();
        mutation.Replace(mutation.Settlements[2] with { OwnerId = 1 });
        world.Commit(mutation);
        Assert.True(engine.Execute(Player, order).Submission.Accepted);
        before = engine.Snapshot;
        rejected = engine.Execute(Player, order);
        Assert.False(rejected.Submission.Accepted);
        Assert.Empty(rejected.Events);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.Single(Advance(engine).Events.OfType<UnitAbsorbedEvent>());
        Assert.Empty(Advance(engine).Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(500u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
    }

    [Theory]
    [InlineData(100u, 400u, 500u, 0L)]
    [InlineData(49900u, 400u, 50000u, 300L)]
    public void WoundedReturnSurvivesRestoreAndDemobilizesOverflow(uint initial, uint cargo, uint expected, long loss)
    {
        var world = World();
        var engine = Engine(world, Rules(10));
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var mutation = world.BeginMutation();
        var unit = Assert.Single(mutation.Units.Values);
        mutation.Replace(unit with { Wounded = cargo });
        mutation.Replace(mutation.Settlements[2] with { Wounded = initial });
        world.Commit(mutation);
        var json = CanonicalJson.Text(engine.CaptureStableState());
        var snapshot = System.Text.Json.JsonSerializer.Deserialize<WorldSnapshot>(json, CanonicalJson.Options)!;
        var restored = Engine(WorldState.Restore(snapshot), Rules(10));
        var first = Advance(engine);
        EventAssert.Equal(first.Events, Advance(restored).Events);
        StateAssert.Equal(engine.Snapshot, restored.Snapshot);
        Assert.Equal(expected, engine.Snapshot.Settlements.Single(s => s.Id == 2).Wounded);
        Assert.Equal((uint)loss, engine.Snapshot.Settlements.Single(s => s.Id == 2).DraftPopulation);
        Assert.Equal(0, first.Events.OfType<ResourceChangedEvent>().Where(e => e.Resource == ResourceKind.Wounded).Sum(e => e.SaturationLoss));
        Assert.Empty(Advance(engine).Events.OfType<UnitAbsorbedEvent>());
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(true, false, false)]
    [InlineData(false, true, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, true)]
    [InlineData(false, true, true)]
    public void CrossCorpsArrivalUsesPersonnelDestinationToRepairLeadership(bool successor, bool overflow, bool transport)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 0) { LeaderId = 12 }],
            [new(1, 1, 100, 100, 100, 70, 500, 0, 100, 0, 100) { CorpsId = 11 },
             new(2, 1, 100, 100, 100, 30, 500, 0, 100, 0, 100) { CorpsId = 10 }],
            [new(11, 1, null, new(70, 70, 70, 70), false) { UnitId = 0, Status = OfficerStatus.Governor },
             new(12, 1, 2, new(70, 70, 70, 70), false) { Status = OfficerStatus.Ruler },
             new(13, 1, successor ? 1 : 2, new(60, 60, 60, 60), false)],
            units: [new(0, 1, new(36, 7), 400) { CorpsId = 11, ServicePlaceId = 1, FormationId = 70,
                Morale = 70, CargoMoney = 200, CargoFood = 300,
                Order = new(transport ? MilitaryOrderKind.Transport : MilitaryOrderKind.Return, null, 2) }],
            corps: [new(10, 1, 12, new(overflow ? uint.MaxValue : 10000, 10000)) { IsRoot = true }, new(11, 1, 11, new(2000, 3000))]);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        Assert.Equal(CommandRejection.None, GovernanceMaintenance.RepairCorps(mutation, 11, new GovernanceRulebook([]), events));
        Assert.Equal(11, mutation.Corps[11].LeaderId);
        Assert.Empty(events);
        MilitaryMovement.AdvanceDay(mutation, Rules(10), new GovernanceRulebook([]), PersonnelRulebook.Empty, events);
        world.Commit(mutation);
        var state = world.CreateSnapshot();
        if (overflow && !transport)
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
            Assert.Equal(transport ? 1 : 2, state.Officers.Single(p => p.Id == 11).SettlementId);
            Assert.Equal(transport ? OfficerStatus.Governor : OfficerStatus.Serving, state.Officers.Single(p => p.Id == 11).Status);
            Assert.Equal(500u, state.Settlements.Single(s => s.Id == 2).Soldiers);
            Assert.Single(events.OfType<UnitAbsorbedEvent>());
            if (transport)
            {
                Assert.Equal(2, state.Corps.Count);
                var source = state.Corps.Single(c => c.Id == 11);
                Assert.Equal(11, source.LeaderId);
                Assert.Equal(new CorpsTreasury(2000, 3000), source.Treasury);
                Assert.Equal(overflow ? uint.MaxValue : 10200u, state.Corps.Single(c => c.IsRoot).Treasury.Money);
                var returning = Assert.Single(state.Simulation.Tasks);
                Assert.Equal("personnel-return", returning.Kind);
                Assert.Equal(1, returning.Journey!.DestinationPlaceId);
                Assert.Equal(11, returning.Journey.ReturnLeg!.CorpsId);
                Assert.Empty(events.OfType<CorpsLeaderChangedEvent>());
                Assert.Empty(events.OfType<CorpsDissolvedEvent>());
            }
            else if (successor)
            {
                Assert.Equal(13, state.Corps.Single(c => c.Id == 11).LeaderId);
                Assert.Equal(10200u, state.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
            }
            else
            {
                Assert.Single(state.Corps);
                Assert.Equal(12200u, state.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
                Assert.Equal(13300u, state.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food);
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
        Assert.Equal(before.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money - 250, result.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Money);
        Assert.Equal(before.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food - 300, result.Snapshot.Corps.Where(c => c.IsRoot).OrderBy(c => c.OwnerId).ElementAt(0).Treasury.Food);
        Assert.Equal(DevelopmentRejection.OfficerUnavailable,
            engine.Execute(Player, new DevelopCommand(Guid.NewGuid(), 1, 1, new(1, 1, [11], DomesticActivity.Commerce))).Development!.Value.Rejection);
        var advanced = Advance(engine);
        var absorbed = Assert.Single(advanced.Events.OfType<UnitAbsorbedEvent>());
        Assert.Equal(engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers, absorbed.Result.Soldiers);
        Assert.Equal(60, absorbed.Result.Morale);
        Assert.Empty(engine.Snapshot.Units);
        Assert.Equal(1, engine.Snapshot.Officers[0].SettlementId);
        Assert.Null(engine.Snapshot.Officers[0].UnitId);
        Assert.Equal(500u, engine.Snapshot.Settlements.Single(s => s.Id == 2).Soldiers);
        Assert.Equal(60, engine.Snapshot.Settlements.Single(s => s.Id == 2).Morale);
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
            EventAssert.Equal(originalEvents, restoredEvents);
            StateAssert.Equal(first.Snapshot, restored.Snapshot);
        }
        Assert.Empty(first.Snapshot.Units);
    }

    [Fact]
    public void RejectedDeploymentDoesNotSpendMoneySoldiersPersonnelOrRandom()
    {
        var engine = Engine(World(), Rules());
        var before = engine.Snapshot;
        Assert.False(engine.Execute(Player, Deploy(soldiers: 1001)).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.False(engine.Execute(Player, Deploy() with { OfficerIds = [11, 11] }).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.False(engine.Execute(Player, new DeployUnitCommand(Guid.NewGuid(), 1, 0, 1, [11], 400, 999, new(MilitaryOrderKind.Move, new(32, 7), null))).Submission.Accepted);
        StateAssert.Equal(before, engine.Snapshot);
        Assert.True(engine.Validate(Player, Deploy()).Accepted);
        StateAssert.Equal(before, engine.Snapshot);
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
    public void FriendlyUnitsAtDestinationDoNotBlockTransport()
    {
        var world = World();
        var mutation = world.BeginMutation();
        WorldPosition[] entrances = [new(37, 6), new(38, 7), new(37, 8), new(36, 7)];
        for (int i = 0; i < entrances.Length; i++) mutation.AddUnit(new(i, 1, entrances[i], 100) { FormationId = 7 });
        mutation.SetSimulation(mutation.Simulation with { NextUnitId = 4 });
        world.Commit(mutation);
        var engine = Engine(world, Rules(10));
        Assert.True(engine.Execute(Player, Deploy()).Submission.Accepted);
        var result = Advance(engine);
        Assert.DoesNotContain(engine.Snapshot.Units, u => u.Id == 4);
        Assert.Contains(result.Events.OfType<UnitAbsorbedEvent>(), e => e.UnitId == 4);
        mutation = world.BeginMutation();
        foreach (int id in Enumerable.Range(0, 4)) mutation.RemoveUnit(id);
        world.Commit(mutation);
        var restored = Engine(WorldState.Restore(engine.CaptureStableState()), Rules(10));
        EventAssert.Equal(Advance(engine).Events, Advance(restored).Events);
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
