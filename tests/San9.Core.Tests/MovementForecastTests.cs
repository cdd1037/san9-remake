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
        [new(1, 0, 10000, 100)], [new(1, 1, 0, 100, 100, 70, 0, 0, 0, 0, 0)], [],
        [unit ?? new(0, 1, new(2, 2), 100) { FormationId = 0, Order = new(MilitaryOrderKind.Move, new(402, 2), null) }]);
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

    [Theory]
    [InlineData(95, 19)]
    [InlineData(100, 20)]
    public void FractionalSpeedSurvivesTwoDaysAndPlanInvalidation(int budget, int distance)
    {
        var world = World().BeginMutation(); var rules = Rules(budget);
        var first = Day(world, rules);
        Assert.Equal(budget / 10, first.Position.X - 2);
        Assert.Equal(budget % 10, first.MovementRemainder);
        world.Replace(first with { MovementPlan = null });
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
        var estimate = new MilitaryArrivalForecast(before, rules).Estimate(0);
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        Assert.Equal(before, world.CreateSnapshot());
        Assert.Null(before.Units.Single().MovementPlan);
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
        mutation.AddUnit(new(1, 1, new(30, 2), 100) { FormationId = 0 });
        mutation.Replace(mutation.Units[0] with { Order = new(MilitaryOrderKind.Move, new(30, 2), null), MovementPlan = null });
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
    public void AttackForecastUsesRangeAndReevaluatesRelocatedTarget()
    {
        var rules = new MilitaryRulebook(new(100, 1, Enumerable.Repeat(0, 100).ToImmutableArray(), [new(0, 10, null)],
            [new(0, 95, 0, 10000, 1, new(1, 1), TravelMode.Land) { Combat = new(10, 10, 10, 10, 50), Attack = new(5, true), CaptureRanks = [BattleRank.Front] }], []));
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 0, 0, 100), new(2, 0, 0, 100)], [], [],
            [new(0, 1, new(2, 2), 100) { FormationId = 0, Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } },
             new(1, 2, new(26, 2), 100) { FormationId = 0 }]);
        var setup = world.BeginMutation();
        setup.SetSimulation(setup.Simulation with { Boundary = SimulationBoundary.Stable });
        world.Commit(setup);
        var snapshot = world.CreateSnapshot();
        Assert.Equal(new(ArrivalEstimateStatus.Known, 2), new MilitaryArrivalForecast(snapshot, rules).Estimate(0));
        var moved = snapshot with { Units = snapshot.Units.Select(u => u.Id == 1 ? u with { Position = new(36, 2) } : u).ToArray() };
        Assert.Equal(new(ArrivalEstimateStatus.Known, 4), new MilitaryArrivalForecast(moved, rules).Estimate(0));
        var mutation = world.BeginMutation();
        Day(mutation, rules);
        Assert.Equal(new WorldPosition(21, 2), Day(mutation, rules).Position);
        Assert.Equal(0, mutation.Units[0].MovementRemainder);
    }

    [Fact]
    public void ExpiringForcedOrderDoesNotReportArrivalAtAnotherDestination()
    {
        var unit = new UnitState(0, 1, new(2, 2), 100) { FormationId = 0, ServicePlaceId = 1,
            Order = new(MilitaryOrderKind.Move, new(21, 2), null), Condition = UnitCondition.Disguised, ConditionDaysRemaining = 1 };
        Assert.Equal(new(ArrivalEstimateStatus.Incomplete), new MilitaryArrivalForecast(World(unit).CreateSnapshot(), Rules(95)).Estimate(0));
    }
}
