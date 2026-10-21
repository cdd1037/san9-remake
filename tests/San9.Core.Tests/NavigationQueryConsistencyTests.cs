using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class NavigationQueryConsistencyTests
{
    [Fact]
    public void ForecastContinuesPastIdleDaysWhenDailyRandomnessCanChangeTheRoute()
    {
        var rules = new MilitaryRulebook(new(20, 20, new int[400].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)], []));
        var mover = new UnitState(0, 1, new(42, 42), 1000)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(52, 52), null),
            Policies = new(AttackApproachingEnemies: true)
        };
        WorldPosition[] positions = [new(57, 52), new(56, 49), new(42, 46), new(61, 54)];
        var obstacles = positions.Select((p, i) => new UnitState(i + 1, 2, p, 1000) { FormationId = 0 }).ToArray();
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [], [], [mover, .. obstacles],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var snapshot = state.CreateSnapshot();
        var forecast = new MilitaryArrivalForecast(snapshot, rules);
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), forecast.Estimate(0, maximumDays: 4));
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), forecast.Estimate(0, maximumExpanded: 1));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 10), forecast.Estimate(0));

        var world = state.BeginMutation();
        for (int day = 1; day <= 10; day++)
        {
            world.SetClock(world.Clock.NextDay());
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
            if (day <= 4) Assert.Equal(mover.Position, world.Units[0].Position);
            if (day == 5) Assert.NotEqual(mover.Position, world.Units[0].Position);
            Assert.Equal(day == 10, world.Units[0].Position == mover.Order.Position);
        }
        Assert.All(obstacles, obstacle => Assert.Equal(obstacle.Position, world.Units[obstacle.Id].Position));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 10), forecast.Estimate(0));
        Assert.Equal(mover.Position, snapshot.Units.Single(u => u.Id == 0).Position);
    }

    [Fact]
    public void DeploymentForecastPredictsFacilityEngagementLikeAnExistingUnit()
    {
        var rules = new MilitaryRulebook(new(40, 30, new int[1200].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }],
            [new(1, new(22, 52), new(4, 4)), new(2, new(62, 58), new(40, 4))
                { Combat = new(10, 10, 10, 10, 50), Attack = new(10, true) }]));
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            [new(1, 1, 10000, 100, 100, 80, 50, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 80, 50, 0, 100, 0, 100)], [], [],
            corps: [new(1, 1, null, new(1000, 10000)) { IsRoot = true }, new(2, 2, null, new(1000, 10000)) { IsRoot = true }]);
        var snapshot = state.CreateSnapshot();
        var request = new DeploymentArrivalRequest(1, 0, null, new(MilitaryOrderKind.Move, new(122, 52), null), new(), Soldiers: 1000);
        var forecast = new MilitaryArrivalForecast(snapshot, rules);
        var estimate = forecast.EstimateDeployment(request);
        var world = state.BeginMutation();
        world.AddUnit(new(0, 1, new(22, 52), request.Soldiers)
            { FormationId = 0, Order = request.Order, Policies = request.Policies, ServicePlaceId = 1, Morale = 80 });
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        Assert.Equal(estimate, new MilitaryArrivalForecast(world.CapturePresentation(), rules).Estimate(0));
        Assert.True(estimate.Days > 10); // Unopposed travel takes ten days on this terrain.
        bool slowed = false;
        for (int day = 1; day <= estimate.Days; day++)
        {
            world.SetClock(world.Clock.NextDay());
            var unit = world.Units[0];
            slowed |= unit.PassiveEngagement;
            var step = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules));
            world.AdvanceNavigation(unit.Id, step);
            Assert.Equal(day == estimate.Days, step.Arrived);
            MilitaryEngagements.ClearExpired(world, rules);
            if (MilitaryEngagements.FacilityTarget(world, world.Settlements[2], rules) is { } victim)
                MilitaryEngagements.Open(world, new(2, true), new(victim.Id, false), rules);
        }
        Assert.True(slowed);
        Assert.Equal(estimate, forecast.EstimateDeployment(request));
        Assert.Empty(snapshot.Units);
        Assert.Equal(0, snapshot.Simulation.NextUnitId);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void RouteAndMovementAgreeOnWhetherTheWaypointIsActive(int interruption)
    {
        var rules = new MilitaryRulebook(new(40, 30, new int[1200].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }],
            [new(1, new(172, 52), new(4, 4))]));
        var unit = new UnitState(0, 1, new(22, 52), 1000)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(172, 52), null), Waypoint = new(22, 122), ServicePlaceId = 1,
            EngagementTargetId = interruption == 1 ? 1 : null, ContactTarget = interruption == 2 ? new(1, false) : null,
            Condition = interruption == 3 ? UnitCondition.Disguised : UnitCondition.Normal,
            ConditionDaysRemaining = interruption == 3 ? 3 : 0
        };
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            [new(1, 1, 100, 100, 100, 80, 50, 0, 100, 0, 100)], [],
            [unit, new(1, 2, new(72, 52), 1000) { FormationId = 0 }],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var snapshot = state.CreateSnapshot();
        var plan = MilitaryRouteQuery.Plan(snapshot, rules, unit, unit.Id);
        Assert.Equal(NavigationStatus.Arrived, plan.Status);
        Assert.Equal(interruption == 0, plan.Steps.Contains(unit.Waypoint.Value));
        var world = state.BeginMutation();
        var movement = MilitaryNavigation.Advance(world, unit, unit.Id, rules, 100);
        if (interruption == 0) Assert.True(movement.Position.Y > unit.Position.Y);
        else
        {
            var direct = unit with { Waypoint = null };
            Assert.Equal(MilitaryRouteQuery.Plan(snapshot, rules, direct, unit.Id).Steps.ToArray(), plan.Steps.ToArray());
            Assert.Equal(MilitaryNavigation.Advance(world, direct, unit.Id, rules, 100).Position, movement.Position);
        }
        Assert.Equal(unit.Waypoint, snapshot.Units.Single(u => u.Id == unit.Id).Waypoint);
    }

    private static MilitaryRulebook Rules() => new(new(30, 20,
        Enumerable.Range(0, 600).Select(i => (i % 30 + i / 30) % 3 == 0 ? 1 : 0).ToImmutableArray(),
        [new(0, 10, null), new(1, 12, null)],
        [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)], []));

    [Theory]
    [InlineData(false, 11)]
    [InlineData(true, 12)]
    public void ForecastExpiresEngagementDuringDaysWithoutMovement(bool departure, int expectedDays)
    {
        var rules = new MilitaryRulebook(new(50, 30, new int[1500].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)], []));
        var unit = new UnitState(0, 1, new(22, 22), 1000)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(122, 22), null), PassiveEngagement = true,
            DepartureDaysRemaining = departure ? 2 : 0,
            Condition = departure ? UnitCondition.Normal : UnitCondition.Confused,
            ConditionDaysRemaining = departure ? 0 : 2
        };
        var state = World(unit);
        var snapshot = state.CreateSnapshot();
        Assert.Equal(new(ArrivalEstimateStatus.Known, expectedDays), new MilitaryArrivalForecast(snapshot, rules).Estimate(0));
        var world = state.BeginMutation();
        for (int day = 1; day <= expectedDays; day++)
        {
            MilitaryConditions.Tick(world, []);
            world.SetClock(world.Clock.NextDay());
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
            MilitaryEngagements.ClearExpired(world, rules);
            MilitaryMovement.TickDepartureDelays(world);
            Assert.Equal(day == expectedDays, world.Units[0].Position == unit.Order.Position);
        }
        Assert.Equal(unit, snapshot.Units.Single());
    }

    [Fact]
    public void ForecastOpensEngagementBeforeDepartureDelayExpires()
    {
        var rules = new MilitaryRulebook(new(50, 30, new int[1500].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(10, true), CaptureRanks = [BattleRank.Front] }], []));
        var unit = new UnitState(0, 1, new(22, 22), 1000)
        {
            FormationId = 0, Morale = 90, DepartureDaysRemaining = 2,
            Order = new(MilitaryOrderKind.Move, new(122, 22), null)
        };
        var enemy = new UnitState(1, 2, new(28, 22), 1000)
        {
            FormationId = 0, Morale = 90, Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 0 }
        };
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [], [], [unit, enemy],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var estimate = new MilitaryArrivalForecast(state.CreateSnapshot(), rules).Estimate(0);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 16), estimate);
        var world = state.BeginMutation();
        // Freeze external positions and damage, matching the forecast's external-world contract.
        for (int day = 1; day <= estimate.Days; day++)
        {
            world.SetClock(world.Clock.NextDay());
            var current = world.Units[0];
            if (current.DepartureDaysRemaining == 0)
                world.AdvanceNavigation(0, MilitaryNavigation.Advance(world, current, 0, rules, MilitaryNavigation.Budget(current, rules)));
            MilitaryEngagements.ClearExpired(world, rules);
            if (MilitaryCombat.Area(enemy, rules).IntersectsExpanded(MilitaryCombat.Area(world.Units[0], rules), 10))
                MilitaryEngagements.Open(world, new(1, false), new(0, false), rules);
            MilitaryMovement.TickDepartureDelays(world);
            if (day == 2) Assert.True(world.Units[0].PassiveEngagement);
            Assert.Equal(day == estimate.Days, world.Units[0].Position == unit.Order.Position);
        }
        Assert.Equal(enemy.Position, world.Units[1].Position);
    }

    private static WorldState World(params UnitState[] units) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100)], [], [], units, corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }]);

    [Theory]
    [InlineData(0, 0)]
    [InlineData(9, 0)]
    [InlineData(0, 3)]
    public void ForecastUsesTheFormalMovementDateIncludingWaitingDays(int departure, int confusion)
    {
        var rules = Rules();
        var unit = new UnitState(0, 1, new(22, 22), 1000)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(122, 92), null),
            DepartureDaysRemaining = departure, ConditionDaysRemaining = confusion,
            Condition = confusion > 0 ? UnitCondition.Confused : UnitCondition.Normal
        };
        var state = World(unit);
        var forecast = new MilitaryArrivalForecast(state.CreateSnapshot(), rules).Estimate(0);
        Assert.Equal(ArrivalEstimateStatus.Known, forecast.Status);
        var world = state.BeginMutation();
        for (int day = 1; day <= forecast.Days; day++)
        {
            MilitaryConditions.Tick(world, []);
            world.SetClock(world.Clock.NextDay());
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
            MilitaryMovement.TickDepartureDelays(world);
            Assert.Equal(day == forecast.Days, world.Units[0].Position == unit.Order.Position);
        }
    }

    [Fact]
    public void ForecastingOneUnitCannotMoveAnObstacleForTheNextQuery()
    {
        var rules = new MilitaryRulebook(new(20, 1, new int[20].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(1, 5), TravelMode.Land)], []));
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [], [],
            [new(0, 1, new(2, 2), 1000) { FormationId = 0, Policies = new(AttackApproachingEnemies: true),
                Order = new(MilitaryOrderKind.Move, new(22, 2), null) },
             new(1, 2, new(42, 2), 1000) { FormationId = 0, Policies = new(AttackApproachingEnemies: true),
                Order = new(MilitaryOrderKind.Move, new(7, 2), null) }],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var snapshot = state.CreateSnapshot();
        var forecast = new MilitaryArrivalForecast(snapshot, rules);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 2), forecast.Estimate(0));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), forecast.Estimate(1));
        Assert.Equal(new(ArrivalEstimateStatus.Known, 2), forecast.Estimate(0));
        Assert.Equal(new WorldPosition(2, 2), snapshot.Units[0].Position);
        Assert.Equal(new WorldPosition(42, 2), snapshot.Units[1].Position);
    }

    [Theory]
    [InlineData(UnitCondition.HighSpirits)]
    [InlineData(UnitCondition.NoFormation)]
    public void ConditionExpiryClearsOldLookaheadInForecastAndFormalMovement(UnitCondition condition)
    {
        var rules = new MilitaryRulebook(new(50, 30, new int[1500].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)], []));
        var unit = new UnitState(0, 1, new(22, 22), 1000)
        {
            FormationId = 0, Order = new(MilitaryOrderKind.Move, new(122, 22), null),
            Condition = condition, ConditionDaysRemaining = 1, Lookahead = new(new(22, 102))
        };
        var state = World(unit);
        var snapshot = state.CreateSnapshot();
        var estimate = new MilitaryArrivalForecast(snapshot, rules).Estimate(0);
        Assert.Equal(new(ArrivalEstimateStatus.Known, 10), estimate);
        var world = state.BeginMutation();
        for (int day = 1; day <= estimate.Days; day++)
        {
            MilitaryConditions.Tick(world, []);
            if (day == 1) Assert.Null(world.Units[0].Lookahead);
            world.SetClock(world.Clock.NextDay());
            MilitaryMovement.AdvanceDay(world, rules, new([]), PersonnelRulebook.Empty, []);
            Assert.Equal(day == estimate.Days, world.Units[0].Position == unit.Order.Position);
        }
        Assert.Equal(unit, snapshot.Units.Single());
    }

    [Fact]
    public void ExpiringConditionPreservesContactDuringForecastAndExecution()
    {
        var rules = new MilitaryRulebook(new(50, 30, new int[1500].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }], []));
        var unit = new UnitState(0, 1, new(22, 22), 1000)
        {
            FormationId = 0, Morale = 80, Order = new(MilitaryOrderKind.Move, new(122, 22), null),
            Condition = UnitCondition.HighSpirits, ConditionDaysRemaining = 1, ContactTarget = new(1, false)
        };
        var state = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [], [],
            [unit, new(1, 2, new(72, 22), 1000) { FormationId = 0, Morale = 80 }],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]);
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), new MilitaryArrivalForecast(state.CreateSnapshot(), rules).Estimate(0));
        var world = state.BeginMutation();
        MilitaryConditions.Tick(world, []);
        Assert.Equal(unit.ContactTarget, world.Units[0].ContactTarget);
        Assert.Equal(1, world.Units[0].EffectiveOrder!.UnitId);
        Assert.Equal(unit.Order, world.Units[0].Order);
    }

    [Fact]
    public void ForecastOccupancyPreservesExcludedSubjectQueriesAndInvalidatesObservers()
    {
        var unit = new UnitState(0, 1, new(22, 22), 1000) { FormationId = 0 };
        var other = unit with { Id = 1, Position = new(82, 82) };
        var query = new WorldQuery(World(unit, other).CreateSnapshot());
        var rules = Rules();
        var self = query.MovementOccupancy(rules, 1, new(), 0, null);
        var observer = query.MovementOccupancy(rules, 1, new(), 1, null);
        var deployment = query.MovementOccupancy(rules, 1, new(), null, null);
        var moved = unit with { Position = new(42, 42) };
        query.SetForecastUnit(moved);
        Assert.Same(self, query.MovementOccupancy(rules, 1, new(), 0, null));
        var updated = query.MovementOccupancy(rules, 1, new(), 1, null);
        Assert.NotSame(observer, updated);
        Assert.Equal(moved.Position, Assert.Single(updated).Position);
        Assert.NotSame(deployment, query.MovementOccupancy(rules, 1, new(), null, null));
        Assert.Equal(unit.Position, Assert.Single(observer).Position);
    }
}
