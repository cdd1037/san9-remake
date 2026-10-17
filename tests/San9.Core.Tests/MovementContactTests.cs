using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class MovementContactTests
{
    private static MilitaryRulebook Rules() => new(new(100, 30, new int[3000].ToImmutableArray(), [new(0, 10, null)],
        [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
            { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }],
        [new(1, new(42, 52), new(10, 10)) { Combat = new(10, 10, 10, 10, 50) }]));

    private static UnitState Unit(int id, WorldPosition position) => new(id, id + 1, position, 1000)
        { FormationId = 0, Morale = 90, Order = new(MilitaryOrderKind.Move, new(452, 52), null) };

    private static WorldMutation World(params UnitState[] units) => WorldState.Create(new(190, 1, 1), 42,
        [new(1, 100), new(2, 100)], [new(1, 2, 100, 100, 100, 70, 0, 0, 0, 0, 0)], [], units,
        corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]).BeginMutation();

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void CollisionReturnsLargestObjectAndPreservesEqualAreaOrder(bool equal)
    {
        var map = new BattlefieldMap(100, 30, new int[3000], [new(0, 10, null)]);
        var small = new MapOccupancy(new(45, 52), new(10, 100)) { Object = new(1, true) };
        var large = new MapOccupancy(new(equal ? 45 : 50, 52), new(equal ? 10 : 20, 100)) { Object = new(2, true) };
        var target = new MapOccupancy(new(50, 52), new(1, 1));
        var step = map.Navigate(new(32, 52), target, false, new(1, 1), TravelMode.Land, [small, large], 1000, mobility: 100);
        Assert.Equal(equal ? small.Object : large.Object, step.Collision?.Object);
        Assert.DoesNotContain(step.Steps, p => new MapOccupancy(p, new(1, 1)).Overlaps(small));
    }

    [Theory]
    [InlineData(1, 2)]
    [InlineData(2, 1)]
    public void EqualBuildingCollisionUsesSpatialOrderAndSetsTheMatchingContact(int lowerId, int upperId)
    {
        var rules = new MilitaryRulebook(new(30, 30, new int[900].ToImmutableArray(), [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, true), CaptureRanks = [BattleRank.Front] }],
            [new(lowerId, new(42, 57), new(10, 10)) { Combat = new(10, 10, 10, 10, 50) },
             new(upperId, new(42, 47), new(10, 10)) { Combat = new(10, 10, 10, 10, 50) }]));
        var mover = Unit(0, new(32, 52)) with { Order = new(MilitaryOrderKind.Move, new(42, 52), null) };
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            [new(lowerId, 2, 100, 100, 100, 70, 0, 0, 0, 0, 0), new(upperId, 2, 100, 100, 100, 70, 0, 0, 0, 0, 0)], [], [mover],
            corps: [new(1, 1, null, new(0, 0)) { IsRoot = true }, new(2, 2, null, new(0, 0)) { IsRoot = true }]).BeginMutation();
        var occupancy = world.MovementOccupancy(rules, mover.OwnerId, mover.Policies, mover.Id, null);
        var step = rules.Map.Navigate(mover.Position, new(mover.Order.Position!.Value, new(1, 1)), false,
            new(4, 4), TravelMode.Land, occupancy, 1000, mobility: 100, randomSeed: 1);
        Assert.Equal(new MapObjectReference(upperId, true), step.Collision?.Object);
        Assert.All(step.Steps, p => Assert.DoesNotContain(occupancy, o => o.Hard && new MapOccupancy(p, new(4, 4)).Overlaps(o)));
        var contacted = MilitaryContacts.AfterMovement(world, mover with { Position = step.Position }, step.Collision, rules);
        Assert.Equal(mover.Order, contacted.Order);
        Assert.Equal(upperId, contacted.EffectiveOrder!.SettlementId);
    }

    [Fact]
    public void EqualBuildingAndUnitCollisionPrefersTheBuildingRegardlessOfInputOrder()
    {
        var map = new BattlefieldMap(100, 30, new int[3000], [new(0, 10, null)]);
        var unit = new MapOccupancy(new(45, 52), new(10, 100)) { Object = new(1, false) };
        var building = unit with { Object = new(2, true) };
        foreach (var objects in new[] { new[] { unit, building }, new[] { building, unit } })
        {
            var step = map.Navigate(new(32, 52), new(new(50, 52), new(1, 1)), false,
                new(1, 1), TravelMode.Land, objects, 1000, mobility: 100);
            Assert.Equal(building.Object, step.Collision?.Object);
        }
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void IntendedAttackTargetWinsOverCollidingBuilding(bool existingContact)
    {
        var attacker = Unit(0, new(32, 52)) with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 }, Lookahead = new(new(102, 52)) };
        if (existingContact) attacker = attacker with { ContactTarget = new(1, true) };
        var world = World(attacker, Unit(1, new(36, 52)));
        var result = MilitaryContacts.AfterMovement(world, attacker,
            new MapOccupancy(new(42, 52), new(10, 10)) { Object = new(1, true) }, Rules());
        Assert.Equal(new MapObjectReference(1, false), result.ContactTarget);
        Assert.Equal(attacker.Order, result.Order);
        Assert.Null(result.Lookahead);
    }

    [Fact]
    public void LeavingContactRangeResumesMainOrderAndClearsLookahead()
    {
        var unit = Unit(0, new(100, 52)) with { ContactTarget = new(1, true), Lookahead = new(new(42, 52)) };
        var result = MilitaryContacts.AfterMovement(World(unit), unit, null, Rules());
        Assert.Null(result.ContactTarget);
        Assert.Null(result.Lookahead);
        Assert.Equal(unit.Order, result.EffectiveOrder);
    }

    [Fact]
    public void BuildingContactDoesNotOverwriteIntentAndUnitCollisionDoesNotRetarget()
    {
        var unit = Unit(0, new(32, 52));
        var world = World(unit, Unit(1, new(38, 52)));
        var collision = new MapOccupancy(new(42, 52), new(10, 10)) { Object = new(1, true) };
        var contact = MilitaryContacts.AfterMovement(world, unit, collision, Rules());
        Assert.Equal(unit.Order, contact.Order);
        Assert.Equal(MilitaryOrderKind.AttackSettlement, contact.EffectiveOrder!.Kind);
        Assert.Null(MilitaryContacts.AfterMovement(world, unit, collision with { Object = new(1, false) }, Rules()).ContactTarget);
        world.Replace(contact);
        MilitaryPolicies.RemoveTarget(world, 1, true, []);
        Assert.Equal(unit.Order, world.Units[0].EffectiveOrder);
    }

    [Fact]
    public void ReissuingSameCommandClearsContactAndLookaheadButMovingTargetDoesNot()
    {
        var unit = Unit(0, new(32, 52)) with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 },
            ContactTarget = new(1, false), Lookahead = new(new(102, 52)) };
        var world = World(unit, Unit(1, new(36, 52)));
        world.Replace(world.Units[1] with { Position = new(90, 90), Soldiers = 50000 });
        Assert.Equal(unit.Lookahead, world.Units[0].Lookahead);
        world.AssignMilitaryOrder(0, unit.Order!, unit.Policies, null);
        Assert.Null(world.Units[0].Lookahead);
        Assert.Null(world.Units[0].ContactTarget);
    }

    [Fact]
    public void EngagementStartsAtCombatAndExpiresOnlyAfterMovement()
    {
        var unit = Unit(0, new(32, 52)) with { Order = new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 } };
        var world = World(unit, Unit(1, new(36, 52)));
        var rules = Rules();
        Assert.Equal(100, MilitaryNavigation.Budget(world.Units[0], rules));
        Assert.Equal(100, MilitaryNavigation.Budget(world.Units[1], rules));
        MilitaryEngagements.ClearExpired(world, rules);
        Assert.False(world.Units[0].ActiveEngagement);
        MilitaryEngagements.Open(world, new(0, false), new(1, false), rules);
        Assert.True(world.Units[0].ActiveEngagement);
        Assert.True(world.Units[1].PassiveEngagement);
        Assert.Equal(50, MilitaryNavigation.Budget(world.Units[0], rules));
        world.Replace(world.Units[1] with { Position = new(100, 52) });
        Assert.Equal(50, MilitaryNavigation.Budget(world.Units[0], rules));
        Assert.Equal(50, MilitaryNavigation.Budget(world.Units[1], rules));
        MilitaryEngagements.ClearExpired(world, rules);
        Assert.Equal(100, MilitaryNavigation.Budget(world.Units[0], rules));
        Assert.Equal(100, MilitaryNavigation.Budget(world.Units[1], rules));
    }

    [Fact]
    public void ExpiryUsesContactGeometryRatherThanCurrentAttackEligibility()
    {
        var attacker = Unit(0, new(32, 52)) with { ContactTarget = new(1, false), ActiveEngagement = true, Morale = 0 };
        var defender = Unit(1, new(40, 52)) with { PassiveEngagement = true };
        var world = World(attacker, defender);
        Assert.False(MilitaryCombat.CanInitiate(attacker));
        MilitaryEngagements.ClearExpired(world, Rules());
        Assert.True(world.Units[0].ActiveEngagement);
        Assert.True(world.Units[1].PassiveEngagement);
    }

    [Fact]
    public void FacilityContactKeepsPassiveEngagementUntilGeometricSeparation()
    {
        var world = World(Unit(0, new(34, 52)) with { PassiveEngagement = true });
        world.Replace(world.Settlements[1] with { CombatTargetUnitId = 0, ConfusionDaysRemaining = 3 });
        MilitaryEngagements.ClearExpired(world, Rules());
        Assert.True(world.Units[0].PassiveEngagement);
        world.Replace(world.Units[0] with { Position = new(12, 52) });
        MilitaryEngagements.ClearExpired(world, Rules());
        Assert.False(world.Units[0].PassiveEngagement);
        Assert.Null(world.Settlements[1].CombatTargetUnitId);
    }

    [Fact]
    public void ContactAndEngagementStateRoundTripWithoutTargetGeometry()
    {
        var unit = Unit(0, new(32, 52)) with { ContactTarget = new(1, true), ActiveEngagement = true,
            PassiveEngagement = true, Lookahead = new(new(102, 52)) };
        var restored = JsonSerializer.Deserialize<UnitState>(JsonSerializer.Serialize(unit))!;
        Assert.Equal(unit.ContactTarget, restored.ContactTarget);
        Assert.Equal(unit.Lookahead, restored.Lookahead);
        Assert.Equal(unit.EffectiveOrder, restored.EffectiveOrder);
        Assert.Equal(MilitaryNavigation.Budget(unit, Rules()), MilitaryNavigation.Budget(restored, Rules()));
    }
}
