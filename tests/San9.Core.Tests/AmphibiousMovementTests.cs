using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AmphibiousMovementTests
{
    private static MilitaryRulebook Rules(Footprint? shipShape = null, int shipSpeed = 9) => new(new(8, 3,
        Enumerable.Range(0, 24).Select(i => i % 8 is >= 2 and <= 4 ? 1 : 0).ToImmutableArray(),
        [new(0, 2, null), new(1, null, 3) { IsWater = true }],
        [new(100, 6, 0, 10000, 5, new(1, 1), TravelMode.Land) { Combat = new(10, 10, 10, 10, 50),
            Attack = new(1, false), CaptureRanks = [BattleRank.Front, BattleRank.Middle, BattleRank.Middle, BattleRank.Rear, BattleRank.Rear] },
         new(200, shipSpeed, 0, 10000, 5, shipShape ?? new(1, 1), TravelMode.Water) { Combat = new(20, 20, 20, 20, 50),
            Attack = new(4, false) { RequiresWaterContact = true }, CaptureRanks = Enumerable.Repeat(BattleRank.Middle, 5).ToImmutableArray() }], []));

    [Fact]
    public void AttackRangeAndRanksResolveFromTheActualOrCandidateShore()
    {
        var rules = Rules();
        var source = new UnitState(1, 1, new(15, 7), 1000) { FormationId = 100, NavalFormationId = 200,
            MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(10, BattleRank.Rear) };
        var target = new UnitState(2, 2, new(27, 7), 1000) { FormationId = 100 };
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)], [],
            [new OfficerState(10, 1, null, new(70, 70, 70, 70), false) { UnitId = 1 }], [source, target],
            [new CorpsState(1, 1, null, new(0, 0)) { IsRoot = true }, new CorpsState(2, 2, null, new(0, 0)) { IsRoot = true }]);
        var order = new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 2 };
        Assert.Equal(0, MilitaryCombat.UnitRange(world.BeginMutation(), source, order, rules));
        Assert.Equal(1, MilitaryCombat.UnitRange(world.BeginMutation(), source, order, rules, new(25, 7)));
        Assert.Equal(BattleRank.Middle, rules.Rank(source, 10));
        Assert.Equal(BattleRank.Rear, rules.Rank(source with { Position = new(25, 7) }, 10));
    }

    [Fact]
    public void CrossesBothBanksAndReturnsWithoutLosingChoicesOrFractionalTime()
    {
        var rules = Rules();
        var unit = new UnitState(1, 1, new(8, 7), 1000) { FormationId = 100, NavalFormationId = 200 };
        var travel = rules.Travel(unit);
        Assert.Equal(100, rules.Formation(unit).Id);
        var step = travel.Map.Navigate(unit.Position, new(new(27, 7), new(1, 1)), false, travel.Shape, travel.Mode, [], travel.DailyBudget);
        Assert.Equal(new WorldPosition(11, 7), step.Position);
        unit = unit with { Position = step.Position };
        Assert.Equal(200, rules.Formation(unit).Id);
        Assert.Equal(20, rules.CombatFormation(unit)!.Defense);
        for (int i = 0; i < 10 && !step.Arrived; i++)
            step = travel.Map.Navigate(step.Position, new(new(27, 7), new(1, 1)), false, travel.Shape, travel.Mode,
                [], travel.DailyBudget + step.Remainder);
        Assert.True(step.Arrived);
        unit = unit with { Position = step.Position };
        Assert.Equal(100, rules.Formation(unit).Id);
        Assert.Equal(10, rules.CombatFormation(unit)!.Defense);
        Assert.Equal(200, unit.NavalFormationId);
        var back = travel.Map.FindRoute(unit.Position, new(8, 7), travel.Shape, travel.Mode, []);
        Assert.True(back.Reachable);
        var returned = travel.Map.Navigate(unit.Position, new(new(8, 7), new(1, 1)), false,
            travel.Shape, travel.Mode, [], 1000, mobility: 200);
        Assert.True(returned.Arrived);
        Assert.Equal(19 * 6, returned.Spent);
        Assert.False(rules.Map.FindRoute(new(8, 7), new(27, 7), new(1, 1), TravelMode.Land, []).Reachable);
    }

    [Theory]
    [InlineData(3, 1)]
    [InlineData(6, 2)]
    public void WaterSpeedControlsProgressWithoutChangingLandChoice(int speed, int distance)
    {
        var rules = Rules(shipSpeed: speed);
        var travel = rules.Travel(100, 200);
        var step = travel.Map.Navigate(new(15, 7), new(new(22, 7), new(1, 1)), false,
            travel.Shape, travel.Mode, [], travel.DailyBudget);
        Assert.Equal(new WorldPosition(15 + distance, 7), step.Position);
    }

    [Fact]
    public void ShipFootprintAppliesOnWaterIncludingCoarseEdgeObstacleChecks()
    {
        var rules = Rules(new(5, 5));
        var travel = rules.Travel(100, 200);
        var obstacle = new MapOccupancy(new(12, 7), new(1, 1));
        Assert.Contains(new WorldPosition(2, 6), travel.Entrances(new(new(2, 7), new(1, 1))));
        Assert.NotNull(travel.Map.PassageCost(new(9, 7), travel.Shape, travel.Mode, [obstacle]));
        Assert.Null(travel.Map.PassageCost(new(10, 7), travel.Shape, travel.Mode, [obstacle]));
        var step = travel.Map.Navigate(new(9, 7), new(new(20, 7), new(1, 1)), false, travel.Shape, travel.Mode,
            [obstacle], 200);
        Assert.False(step.Arrived);
        Assert.NotNull(step.Collision);
        Assert.All(step.Steps, p => Assert.False(new MapOccupancy(p, rules.Map.IsWater(p) ? new(5, 5) : new(1, 1)).Overlaps(obstacle)));
        var route = travel.Map.FindRoute(new(9, 7), new(20, 7), travel.Shape, travel.Mode, [obstacle]);
        Assert.True(route.Reachable);
        Assert.Equal(MapCoordinates.Cell(new(20, 7)), MapCoordinates.Cell(route.Steps[^1]));
        var changed = travel.Map.Navigate(new(9, 7), new(new(20, 7), new(1, 1)), false,
            travel.Shape, travel.Mode, [obstacle], 6);
        Assert.NotEqual(new WorldPosition(10, 7), changed.Position);
        Assert.NotEqual(NavigationStatus.Unreachable, changed.Status);
    }
}
