using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MovementForecastTests
{
    private static MilitaryRulebook Rules(int budget, bool mixed = false) => new(new(100, 1,
        Enumerable.Range(0, 100).Select(x => mixed && x % 3 == 1 ? 1 : 0).ToImmutableArray(),
        [new(0, 10, null), new(1, 12, null)],
        [new(0, budget, 0, 10000, 5, new(1, 1), TravelMode.Land)],
        [new(1, new(402, 2), new(3, 3))]));

    private static WorldState World(UnitState? unit = null)
    {
        var world = WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100), new(2, 100)], [new(1, 1, 0, 100, 100, 70, 0, 0, 0, 0, 0)], [],
        [unit ?? new(0, 1, new(2, 2), 100) { FormationId = 0, Order = new(MilitaryOrderKind.Move, new(402, 2), null) }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(0, 10000)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var setup = world.BeginMutation();
        setup.SetSimulation(setup.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(setup);
        return world;
    }

    private static UnitState Day(WorldMutation world, MilitaryRulebook rules)
    {
        MilitaryConditions.Tick(world, []);
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
        return world.Units[0];
    }

    [Fact]
    public void RelayArrivalUsesRemainingBudgetWithoutCompletingFinalOrder()
    {
        var rules = Rules(100);
        var unit = new UnitState(0, 1, new(12, 2), 100)
        { FormationId = 0, Order = new(MilitaryOrderKind.Move, new(32, 2), null), Waypoint = new(7, 2) };
        var world = World(unit).BeginMutation();
        var step = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules));
        Assert.Equal(100, step.Spent); Assert.False(step.Arrived);
        var moved = MilitaryNavigation.Apply(unit, step);
        Assert.Equal(unit.Position, moved.Position); Assert.Null(moved.Waypoint); Assert.Equal(unit.Order, moved.Order);
        List<DomainEvent> events = [];
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, events);
        var arrival = Assert.Single(events.OfType<UnitWaypointReachedEvent>());
        Assert.Equal(new WorldPosition(7, 2), arrival.Position);
        Assert.Equal(arrival.Position, arrival.Unit.Position);
        Assert.NotEqual(arrival.Position, world.Units[0].Position);
        events.Clear();
        MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, events);
        Assert.Empty(events.OfType<UnitWaypointReachedEvent>());
    }

    [Fact]
    public void RoutePreviewContinuesAfterRelayExhaustsTheDailyBudget()
    {
        var rules = Rules(100);
        var unit = new UnitState(0, 1, new(12, 2), 100)
        { FormationId = 0, Order = new(MilitaryOrderKind.Move, new(32, 2), null), Waypoint = new(2, 2) };
        var world = World(unit).BeginMutation();
        var plan = MilitaryRouteQuery.Plan(world.CapturePresentation(), rules, unit, unit.Id);
        Assert.Equal(NavigationStatus.Arrived, plan.Status);
        var positions = new List<WorldPosition> { unit.Position };
        for (int day = 0; day < 4; day++)
        {
            List<DomainEvent> events = [];
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, events);
            positions.AddRange(Assert.Single(events.OfType<UnitMovedEvent>()).Steps);
        }
        Assert.Equal(positions, plan.Steps);
        Assert.Equal(new WorldPosition(32, 2), world.Units[0].Position);
    }

    [Fact]
    public void RelayReplacesItsLookaheadWithTheFinalDestinationLookahead()
    {
        var rules = Rules(100);
        var unit = new UnitState(0, 1, new(12, 2), 100)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(352, 2), null), Waypoint = new(7, 2),
            Lookahead = new(new(9, 2), new(new(7, 2), new(1, 1)))
        };
        var world = World(unit).BeginMutation();
        var moved = Day(world, rules);
        Assert.Null(moved.Waypoint);
        Assert.NotNull(moved.Lookahead);
        Assert.Equal(unit.Order.Position, moved.Lookahead.Target.Position);
        Assert.NotEqual(unit.Lookahead, moved.Lookahead);
    }

    [Fact]
    public void FrozenOrderQueriesMatchExecutionAndKeepTheirSnapshotAfterOwnershipChanges()
    {
        var rules = Rules(100);
        var world = World();
        var before = world.CreateSnapshot();
        string hash = before.StateHash;
        var query = new MilitaryOrderQuery(before, rules);
        var mutation = world.BeginMutation();
        MilitaryOrder[] orders =
        [
            new(MilitaryOrderKind.Move, new(32, 2), null),
            new(MilitaryOrderKind.Move, new(-5, 2), null),
            new(MilitaryOrderKind.Return, null, 1),
            new(MilitaryOrderKind.Return, null, 99),
            new(MilitaryOrderKind.AttackSettlement, null, 1),
            new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 99 },
        ];
        foreach (var order in orders)
            Assert.Equal(MilitaryMovement.ValidOrder(mutation, 1, order, rules.Formations[0], rules), query.CanAssign(0, order));
        var home = new MilitaryOrder(MilitaryOrderKind.Return, null, 1);
        Assert.True(query.CanAssign(0, home));
        mutation.Replace(mutation.Settlements[1] with { OwnerId = 2, CorpsId = 2 });
        world.Commit(mutation);
        Assert.True(query.CanAssign(0, home));
        Assert.False(new MilitaryOrderQuery(world.CreateSnapshot(), rules).CanAssign(0, home));
        Assert.Equal(hash, before.StateHash);
    }

    [Fact]
    public void RelayConsumesSharedDailyBudgetAndForecastIncludesDelay()
    {
        var rules = Rules(100);
        var unit = new UnitState(0, 1, new(12, 2), 100)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(32, 2), null),
            Waypoint = new(2, 2), DepartureDaysRemaining = 2
        };
        var world = World(unit); var mutation = world.BeginMutation();
        var forecast = new MilitaryArrivalForecast(world.CreateSnapshot(), rules).Estimate(0);
        Assert.Equal(new ArrivalEstimate(ArrivalEstimateStatus.Known, 6), forecast);
        for (int day = 1; day <= 6; day++)
        {
            var current = Day(mutation, rules);
            if (day <= 2) Assert.Equal(unit.Position, current.Position);
            if (day == 3) { Assert.Equal(new WorldPosition(2, 2), current.Position); Assert.Null(current.Waypoint); Assert.NotNull(current.Order); }
            MilitaryMovement.TickDepartureDelays(mutation);
        }
        Assert.Equal(new WorldPosition(32, 2), mutation.Units[0].Position);
        Assert.Null(mutation.Units[0].Order);
    }

    [Theory]
    [InlineData(95, 19)]
    [InlineData(100, 20)]
    public void FractionalSpeedSurvivesTwoDaysAndPlanInvalidation(int budget, int distance)
    {
        var world = World().BeginMutation(); var rules = Rules(budget);
        var first = Day(world, rules);
        Assert.Equal(budget / 10, first.Position.X - 2);
        Assert.Equal(budget % 10, first.MovementRemainder);
        var second = Day(world, rules);
        Assert.Equal(distance, second.Position.X - 2);
        Assert.Equal(0, second.MovementRemainder);
    }

    [Theory]
    [InlineData(95, false)]
    [InlineData(95, true)]
    [InlineData(7, true)]
    public void ForecastMatchesActualFirstArrivalAcrossShortRoutesAndMixedTerrain(int budget, bool mixed)
    {
        var rules = Rules(budget, mixed); var world = World();
        var mutation = world.BeginMutation();
        var before = world.CreateSnapshot();
        var estimate = new MilitaryArrivalForecast(before, rules).Estimate(0, maximumExpanded: 1000000);
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        Assert.Equal(before, world.CreateSnapshot());
        for (int day = 1; day <= estimate.Days; day++)
        {
            var unit = Day(mutation, rules);
            Assert.Equal(day == estimate.Days, unit.Position == new WorldPosition(402, 2));
            if (day == estimate.Days) Assert.Equal(0, unit.MovementRemainder);
        }
    }

    [Fact]
    public void SlowTerrainConsumesTwelveAndCarriesOnlyUnaffordableStepBudget()
    {
        var rules = Rules(95, true); var mutation = World().BeginMutation();
        var events = new List<DomainEvent>();
        MilitaryMovement.AdvanceDay(mutation, rules, new([]), PersonnelRulebook.Empty, events);
        // Two ordinary steps, five slow steps, then one ordinary step: 20 + 60 + 10.
        Assert.Equal(new WorldPosition(10, 2), mutation.Units[0].Position);
        Assert.Equal(90, Assert.Single(events.OfType<UnitMovedEvent>()).Spent);
        Assert.Equal(5, mutation.Units[0].MovementRemainder);
    }

    [Fact]
    public void BlockedAndIdleUnitsCannotBankDailyBudgets()
    {
        var rules = Rules(95); var mutation = World().BeginMutation();
        Day(mutation, rules);
        mutation.AddUnit(new(1, 2, new(30, 2), 100) { FormationId = 0 });
        mutation.Replace(mutation.Units[0] with { Order = new(MilitaryOrderKind.Move, new(30, 2), null), Policies = new(AttackApproachingEnemies: true) });
        for (int day = 0; day < 4; day++)
        {
            var blocked = Day(mutation, rules);
            Assert.Equal(new WorldPosition(11, 2), blocked.Position);
            Assert.Equal(0, blocked.MovementRemainder);
        }
        mutation.RemoveUnit(1);
        mutation.Replace(mutation.Units[0] with { Order = new(MilitaryOrderKind.Move, new(402, 2), null) });
        Assert.Equal(new WorldPosition(20, 2), Day(mutation, rules).Position);
        mutation.Replace(mutation.Units[0] with { Order = null });
        Assert.Equal(0, Day(mutation, rules).MovementRemainder);
    }

    [Fact]
    public void KnownConfusionUsesPreMovementExpirationAndDoesNotAccumulateBudget()
    {
        var rules = Rules(95);
        var unit = new UnitState(0, 1, new(2, 2), 100) { FormationId = 0,
            Order = new(MilitaryOrderKind.Move, new(21, 2), null), Condition = UnitCondition.Confused, ConditionDaysRemaining = 3 };
        var world = World(unit);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), new MilitaryArrivalForecast(world.CreateSnapshot(), rules).Estimate(0));
        var mutation = world.BeginMutation();
        for (int day = 1; day <= 4; day++)
            Assert.Equal(day == 4, Day(mutation, rules).Position == new WorldPosition(21, 2));
    }

    [Fact]
    public void SettlementArrivalStopsAtEntranceAndForecastDoesNotAbsorbUnit()
    {
        var unit = new UnitState(0, 1, new(380, 2), 100) { FormationId = 0,
            Order = new(MilitaryOrderKind.Return, null, 1) };
        var world = World(unit); var rules = Rules(100);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 2), new MilitaryArrivalForecast(world.CreateSnapshot(), rules).Estimate(0));
        Assert.Single(world.CreateSnapshot().Units);
        var mutation = world.BeginMutation();
        Day(mutation, rules);
        MilitaryMovement.AdvanceDay(mutation, rules, new([]), PersonnelRulebook.Empty, []);
        Assert.Empty(mutation.Units);
    }

    [Fact]
    public void ExhaustedForecastIsNotReportedAsUnreachableOrAPartialArrival()
    {
        var forecast = new MilitaryArrivalForecast(World().CreateSnapshot(), Rules(95));
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), forecast.Estimate(0, maximumExpanded: 1));
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), forecast.Estimate(0, maximumDays: 1));
        Assert.Equal(ArrivalEstimateStatus.Known, forecast.Estimate(0).Status);
    }

    [Fact]
    public void AttackForecastPredictsCloseApproachAndReevaluatesRelocatedTarget()
    {
        var rules = new MilitaryRulebook(new(100, 1, Enumerable.Repeat(0, 100).ToImmutableArray(), [new(0, 10, null)],
            [new(0, 95, 0, 10000, 1, new(1, 1), TravelMode.Land) { Combat = new(10, 10, 10, 10, 50), Attack = new(5, true), CaptureRanks = [BattleRank.Front] }], []));
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [], [],
            [new(0, 1, new(2, 2), 100) { FormationId = 0, Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } },
             new(1, 2, new(26, 2), 100) { FormationId = 0 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var setup = world.BeginMutation();
        setup.SetSimulation(setup.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(setup);
        var snapshot = world.CreateSnapshot();
        Assert.Equal(new(ArrivalEstimateStatus.Known, 3), new MilitaryArrivalForecast(snapshot, rules).Estimate(0));
        var moved = snapshot with { Units = snapshot.Units.Select(u => u.Id == 1 ? u with { Position = new(36, 2) } : u).ToArray() };
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), new MilitaryArrivalForecast(moved, rules).Estimate(0));
        var mutation = world.BeginMutation();
        Day(mutation, rules);
        Assert.Equal(new WorldPosition(21, 2), Day(mutation, rules).Position);
        Assert.Equal(ArrivalEstimateStatus.Known, new MilitaryArrivalForecast(mutation.CapturePresentation(), rules).Estimate(0).Status);
        Assert.Equal(new WorldPosition(25, 2), Day(mutation, rules).Position);
        Assert.Equal(0, mutation.Units[0].MovementRemainder);
    }

    [Fact]
    public void ExpiringForcedOrderDoesNotReportArrivalAtAnotherDestination()
    {
        var unit = new UnitState(0, 1, new(2, 2), 100) { FormationId = 0, ServicePlaceId = 1,
            Order = new(MilitaryOrderKind.Move, new(21, 2), null), Condition = UnitCondition.Disguised, ConditionDaysRemaining = 1 };
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), new MilitaryArrivalForecast(World(unit).CreateSnapshot(), Rules(95)).Estimate(0));
    }
    [Fact]
    public void ProspectiveDeploymentKeepsRealUnitZeroAsAnObstacleAndPoliciesAffectPrediction()
    {
        var rules = new MilitaryRulebook(new(20, 1, new int[20].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 95, 0, 10000, 1, new(1, 1), TravelMode.Land),
             new(1, 95, 0, 10000, 1, new(1, 5), TravelMode.Land)], [new(1, new(2, 2), new(1, 1))]));
        var world = World(new(0, 2, new(27, 2), 100) { FormationId = 1 });
        var before = world.CreateSnapshot();
        var forecast = new MilitaryArrivalForecast(before, rules);
        var request = new DeploymentArrivalRequest(1, 0, null, new(MilitaryOrderKind.Move, new(52, 2), null),
            new(AttackApproachingEnemies: true));
        Assert.Equal(ArrivalEstimateStatus.Waiting, forecast.EstimateDeployment(request).Status);
        Assert.Equal(ArrivalEstimateStatus.Known, forecast.EstimateDeployment(request with { Policies = new() }).Status);
        Assert.Equal(ArrivalEstimateStatus.Waiting, forecast.EstimateDeployment(request).Status);
        Assert.Equal(before, world.CreateSnapshot());
        Assert.Equal(ArrivalEstimateStatus.Incomplete, forecast.EstimateDeployment(request with { Policies = new() }, maximumDays: 1).Status);
        Assert.Equal(ArrivalEstimateStatus.Incomplete, forecast.EstimateDeployment(request with { Policies = new() }, maximumExpanded: 1).Status);
        Assert.Equal(ArrivalEstimateStatus.NotApplicable, forecast.EstimateDeployment(request with { SettlementId = 99 }).Status);
    }

    [Fact]
    public void ProspectiveAttackPredictsCloseApproachAndReevaluatesTargetInNewSnapshot()
    {
        var rules = new MilitaryRulebook(new(20, 1, new int[20].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 95, 0, 10000, 1, new(1, 1), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(5, true), CaptureRanks = [BattleRank.Front] }], [new(1, new(2, 2), new(1, 1))]));
        var snapshot = World(new(0, 2, new(26, 2), 100) { FormationId = 0 }).CreateSnapshot();
        var request = new DeploymentArrivalRequest(1, 0, null, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 }, new());
        Assert.Equal(new(ArrivalEstimateStatus.Known, 3), new MilitaryArrivalForecast(snapshot, rules).EstimateDeployment(request));
        var moved = snapshot with { Units = [snapshot.Units[0] with { Position = new(36, 2) }] };
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), new MilitaryArrivalForecast(moved, rules).EstimateDeployment(request));
    }

}
