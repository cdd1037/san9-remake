using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WaterCombatContactTests
{
    private static readonly OrdinaryAttack Melee = new(5, false) { RequiresWaterContact = true };
    private static MapOccupancy Area(int x, int y = 7, int width = 1, int height = 1) => new(new(x, y), new(width, height));
    private static MilitaryRulebook MapRules(int width, int height, Func<int, int, bool> water, IEnumerable<MapCell>? blocked = null) =>
        new(new(width, height, Enumerable.Range(0, width * height).Select(i => water(i % width, i / width) ? 1 : 0).ToImmutableArray(),
            [new(0, 1, null), new(1, null, 1) { IsWater = true }], [], [])
            { BlockedCells = (blocked ?? []).ToImmutableArray() });

    [Fact]
    public void ShorelineBuildingsAllowWaterMeleeWithoutHarborIdentity()
    {
        var rules = MapRules(8, 3, (x, _) => x < 3);
        var shore = Area(17, width: 5, height: 5);
        var inland = Area(32, width: 5, height: 5);
        Assert.True(rules.CanAttackTarget(Melee, shore, true));
        Assert.Equal(5, rules.AttackRange(Melee, Area(12), shore, true));
        Assert.False(rules.CanAttackTarget(Melee, inland, true));
        Assert.Equal(0, rules.AttackRange(Melee, Area(12), inland, true));
        Assert.Equal(20, rules.AttackRange(new(20, false), Area(12), inland, true));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void NearbyWaterCannotBridgeLandOrBlockedWater(bool blocked)
    {
        var rules = MapRules(5, 3, (x, _) => blocked || x != 1,
            blocked ? Enumerable.Range(0, 3).Select(y => new MapCell(1, y)) : null);
        var target = Area(17, width: 5, height: 5);
        Assert.True(rules.CanAttackTarget(Melee, target, true));
        Assert.Equal(0, rules.AttackRange(Melee with { Range = 20 }, Area(2), target, true));
    }

    [Fact]
    public void DiagonalContactCannotCutLandCornersOrReachAnInlandUnit()
    {
        var rules = MapRules(3, 3, (x, y) => x == y);
        Assert.Equal(0, rules.AttackRange(Melee, Area(4, 4), Area(7), false));
        Assert.False(rules.CanAttackTarget(Melee, Area(7, 2), false));
    }

    [Fact]
    public void ReplacementGeometryRecomputesShorelineAfterTheOldGeometryWasCached()
    {
        var rules = MapRules(8, 3, (x, _) => x < 3);
        var narrow = Area(18, width: 3, height: 3);
        var wider = Area(18, width: 7, height: 3);
        Assert.False(rules.CanAttackTarget(Melee, narrow, true));
        Assert.True(rules.CanAttackTarget(Melee, wider, true));
        Assert.Equal(5, rules.AttackRange(Melee, Area(12), wider, true));
        Assert.False(rules.CanAttackTarget(Melee, narrow, true));
    }

    [Theory]
    [InlineData(0, 0)]
    [InlineData(14, 0)]
    [InlineData(0, 14)]
    [InlineData(14, 14)]
    public void BuildingsAtMapEdgesIgnoreOffMapContactPoints(int x, int y)
    {
        var rules = MapRules(3, 3, (_, _) => true);
        Assert.True(rules.CanAttackTarget(Melee, Area(x, y, 5, 5), true));
    }

    private static (WorldState World, MilitaryRulebook Rules) Scenario(bool blocker = false, int blockerX = 17)
    {
        var combat = new CombatProfile(100, 1000, 100, 10, 50);
        var rules = new MilitaryRulebook(new(10, 3, Enumerable.Range(0, 30).Select(i => i % 10 < 5 ? 1 : 0).ToImmutableArray(),
            [new(0, 1, null), new(1, null, 1) { IsWater = true }],
            [new(200, 100, 0, 100000, 1, new(1, 1), TravelMode.Water)
                { Combat = combat, Attack = Melee, CaptureRanks = [BattleRank.Front] }],
            [new(1, new(2, 7), new(1, 1)) { FacilityTypeId = 8000, Combat = combat },
                new(2, new(27, 7), new(5, 5)) { FacilityTypeId = 8000, Combat = combat },
                new(3, new(blockerX, 7), new(5, 15)) { FacilityTypeId = 8000, Combat = combat, DestroyedAtZeroDurability = true }])
            { Facilities = [new(8000, 0, 100, new(5, 5), 0, false, true, true, 100000, null, null, null, [])] });
        var world = WorldState.Create(new(190, 1, 1), 42,
            [new(1, 100) { LeaderId = 11 }, new(2, 100) { LeaderId = 22 }],
            [new(1, 1, 10000, 100, 100, 100, 0, 0, 100, 0, 100), new(2, 2, 10000, 100, 100, 100, 0, 0, 100, 0, 100),
                new(3, blocker ? 2 : null, blocker ? 10000u : 0, blocker ? 100 : 0, 100, 100, 0, 0, 100, 0, 100) { IsDestroyed = !blocker }],
            [new(11, 1, null, new(80, 80, 80, 80), false) { UnitId = 0 },
                new(22, 2, 2, new(80, 80, 80, 80), false)],
            units: [new(0, 1, new(7, 7), 10000) { FormationId = 200, Morale = 100, ServicePlaceId = 1,
                MemberRanks = ImmutableDictionary<int, BattleRank>.Empty.Add(11, BattleRank.Front) }],
            controls: [new("player", 1, ControllerKind.Player)],
            corps: [new(1, 1, 11, new(10000, 1000000)) { IsRoot = true }, new(2, 2, 22, new(10000, 1000000)) { IsRoot = true }]);
        return (world, rules);
    }

    [Fact]
    public void NavigationForecastAndCombatReachTheWaterSideOfANonHarborBuilding()
    {
        var (world, rules) = Scenario();
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var player = new CommandContext("player");
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        Assert.True(engine.Execute(player, new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        var estimate = new MilitaryArrivalForecast(engine.Snapshot, rules).Estimate(0, maximumDays: 5);
        Assert.Equal(ArrivalEstimateStatus.Known, estimate.Status);
        engine.EndControl(player);
        var events = engine.AdvanceXun().Events;
        Assert.Contains(events.OfType<BattleDamageEvent>(), e => e.AttackerId == 0 && e.SettlementId == 2 && e.SoldierLoss > 0);
        var approach = events.TakeWhile(e => e is not BattleDamageEvent).OfType<UnitMovedEvent>().ToArray();
        var position = Assert.Single(approach).To;
        Assert.True(rules.Map.IsWater(position));
        Assert.False(Area(position.X, position.Y).Overlaps(rules.BuildingArea(rules.Places[2])));
        Assert.All(approach.SelectMany(e => e.Steps), p => Assert.True(rules.Map.IsWater(p)));
        Assert.Equal(estimate.Days, events.OfType<BattleDamageEvent>().First(e => e.AttackerId == 0).Clock.Day - 1);
    }

    [Fact]
    public void ABlockedChannelIsNotReportedAsArrivalAtTheTarget()
    {
        var (world, rules) = Scenario(blocker: true);
        var engine = new SimulationEngine(world, EconomyRulebook.Empty, PersonnelRulebook.Empty, TreasureRulebook.Empty, military: rules);
        var order = new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2);
        Assert.True(engine.Execute(new("player"), new OrderUnitCommand(Guid.NewGuid(), 1, 0, 0, order)).Submission.Accepted);
        Assert.NotEqual(ArrivalEstimateStatus.Known, new MilitaryArrivalForecast(engine.Snapshot, rules).Estimate(0, maximumDays: 5).Status);
    }

    [Fact]
    public void OtherBuildingsBlockContactAndDestructionUpdatesTheSpatialIndex()
    {
        var (world, rules) = Scenario(blocker: true);
        var mutation = world.BeginMutation();
        var unit = mutation.Units[0] with { Position = new(12, 7) };
        var attack = Melee with { Range = 20 };
        IWorldQuery query = mutation;
        int Range() => rules.AttackRange(attack, MilitaryCombat.Area(unit, rules), rules.SettlementArea(mutation.Settlements[2]), true,
            p => query.BlocksWaterContact(rules, p, 2));
        Assert.Equal(0, Range());
        mutation.Replace(mutation.Settlements[3] with { IsDestroyed = true, OwnerId = null, CorpsId = null, Soldiers = 0, Durability = 0 });
        Assert.Equal(20, Range());
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void ContactAndPassageQueriesShareTheCurrentRulebookGeometry(bool frozen)
    {
        var (world, original) = Scenario(blocker: true);
        var (_, moved) = Scenario(blocker: true, blockerX: 37);
        IWorldQuery query = frozen ? new WorldQuery(world.CreateSnapshot()) : world.BeginMutation();
        var point = new WorldPosition(17, 7);
        Assert.True(query.BlocksWaterContact(original, point, null));
        var before = query.MovementOccupancy(original, 1, new(), 0, null);
        Assert.False(query.BlocksWaterContact(moved, point, null));
        var after = query.MovementOccupancy(moved, 1, new(), 0, null);
        Assert.Contains(before, a => a.Object == new MapObjectReference(3, true) && a.Contains(point));
        Assert.DoesNotContain(after, a => a.Object == new MapObjectReference(3, true) && a.Contains(point));
        Assert.True(query.BlocksWaterContact(original, point, null));
    }

    [Fact]
    public void ForecastAndCombatBothExpirePassiveContactAcrossABlockedWaterCorner()
    {
        var (world, _) = Scenario();
        var rules = new MilitaryRulebook(new(3, 3, [1, 0, 0, 0, 1, 0, 0, 0, 1],
            [new(0, 1, null), new(1, null, 1) { IsWater = true }],
            [new(200, 100, 0, 100000, 1, new(1, 1), TravelMode.Water)
                { Combat = new(10, 10, 10, 10, 50), Attack = Melee, CaptureRanks = [BattleRank.Front] }], []));
        var mutation = world.BeginMutation();
        mutation.Replace(mutation.Units[0] with { Position = new(4, 4), ActiveEngagement = true, ContactTarget = new(1, false) });
        var defender = mutation.Units[0] with { Id = 1, OwnerId = 2, Position = new(7, 7),
            ActiveEngagement = false, PassiveEngagement = true, ContactTarget = null, MemberRanks = ImmutableDictionary<int, BattleRank>.Empty };
        mutation.AddUnit(defender);
        var predicted = MilitaryEngagements.ForecastNextCombat(mutation, defender, rules);
        MilitaryEngagements.ClearExpired(mutation, rules);
        Assert.False(predicted.PassiveEngagement);
        Assert.Equal(mutation.Units[1].PassiveEngagement, predicted.PassiveEngagement);
    }
}
