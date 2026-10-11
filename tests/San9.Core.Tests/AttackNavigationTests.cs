using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackNavigationTests
{
    private static (WorldMutation World, MilitaryRulebook Rules) Fixture(bool settlement = false)
    {
        FormationRule Formation(int id, int range, Footprint shape) => new(id, 60, 0, 10000, 1, shape, TravelMode.Land)
        { Combat = new(10, 10, 10, 10, 50), Attack = new(range, true)
            { RangedSiege = id == 0, ElevationBonusPerLevel = id == 3 ? 5 : 0, MaximumElevationBonus = id == 3 ? 10 : 0 },
            CaptureRanks = [BattleRank.Front] };
        var rules = new MilitaryRulebook(new(20, 20, new int[400].ToImmutableArray(), [new(0, 10, null)],
            [Formation(0, 10, new(1, 1)), Formation(1, 1, new(1, 1)), Formation(2, 1, new(1, 100)), Formation(3, 20, new(1, 1))],
            [new(2, new(70, 50), new(1, 1)) { FacilityTypeId = 100, Combat = new(10, 10, 10, 10, 50), Attack = new(1, false) },
             new(3, new(70, 80), new(1, 1)) { FacilityTypeId = 100 }])
        {
            Rockfall = new(5, 25), Elevations = Enumerable.Range(0, 400).Select(i => i % 20 < 13 ? 3 : 1).ToImmutableArray(),
            Facilities = [new(100, 0, 100, new(1, 1), 0, false, true, true, 10000, null, null, null, [])
                { Combat = new(10, 10, 10, 10, 50), Attack = new(1, false), InnateAttack = new(true, 0) }]
        });
        var order = settlement ? new MilitaryOrder(MilitaryOrderKind.AttackSettlement, null, 2) :
            new MilitaryOrder(MilitaryOrderKind.AttackUnit, null, null) { UnitId = 1 };
        var units = new List<UnitState>
        {
            new(0, 1, new(10, 50), 1000) { FormationId = 0, Order = order },
            new(9, 1, new(50, 50), 1000) { FormationId = 2 }
        };
        if (!settlement) units.Add(new(1, 2, new(70, 50), 1000) { FormationId = 1 });
        var world = WorldState.Create(new(190, 1, 1), 42, [new(1, 100), new(2, 100)],
            settlement ? [new(2, 2, 2000, 100, 100, 70, 0, 0, 100, 0, 100), new(3, null, 0, 100, 100, 70, 0, 0, 100, 0, 100)] : [],
            [], units, corps: [new(1, 1, null, new(0, 10000)) { IsRoot = true }, new(2, 2, null, new(0, 10000)) { IsRoot = true }]);
        return (world.BeginMutation(), rules);
    }

    private static NavigationStep Advance(WorldMutation world, MilitaryRulebook rules)
    {
        var unit = world.Units[0];
        var step = MilitaryNavigation.Advance(world, unit, unit.Id, rules, MilitaryNavigation.Budget(unit, rules));
        world.AdvanceNavigation(unit.Id, step);
        return step.Navigation;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void FriendlyWallIsSoftAndCannotPermanentlyBlockAnAttack(bool settlement)
    {
        var (world, rules) = Fixture(settlement);
        for (int day = 0; day < 12; day++) Advance(world, rules);
        Assert.True(world.Units[0].Position.X > 50);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void HostileWallIsHardWithEngagementPolicyAndRemovalResumesMovement(bool settlement)
    {
        var (world, rules) = Fixture(settlement);
        world.Replace(world.Units[0] with { Policies = new(AttackApproachingEnemies: true) });
        world.Replace(world.Units[9] with { OwnerId = 2, CorpsId = 2 });
        Assert.Equal(NavigationStatus.Waiting, Advance(world, rules).Status);
        using var diagnostics = new PathSearchDiagnostics();
        Assert.Equal(NavigationStatus.Waiting, Advance(world, rules).Status);
        Assert.True(diagnostics.Queries > 0);
        world.RemoveUnit(9);
        Assert.Equal(NavigationStatus.Moving, Advance(world, rules).Status);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiegeThresholdReevaluatesTheCurrentApproach(bool durability)
    {
        var (world, rules) = Fixture(true);
        var unit = world.Units[0];
        var p = new WorldPosition(60, 50);
        Assert.Equal(10, MilitaryCombat.ApproachRange(world, unit, unit.Order!, rules, p));
        world.Replace(durability ? world.Settlements[2] with { Durability = 19 } : world.Settlements[2] with { Soldiers = 999 });
        Assert.Equal(1, MilitaryCombat.ApproachRange(world, unit, unit.Order!, rules, p));
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void OrdinaryFormationKeepsApproachingFromUnansweredHighGroundAttack(bool settlement)
    {
        var (world, rules) = Fixture(settlement);
        var unit = world.Units[0] with { FormationId = 3, Position = new(60, 50) };
        world.Replace(unit);
        var target = MilitaryCombat.TargetArea(world, unit.Order!, rules);
        Assert.True(MilitaryCombat.Area(unit, rules).IntersectsExpanded(target, MilitaryCombat.UnitRange(world, unit, unit.Order!, rules)));
        Assert.False(target.IntersectsExpanded(MilitaryCombat.Area(unit, rules), 1));
        Assert.True(Advance(world, rules).Position.X > unit.Position.X);
        NavigationStep step = Advance(world, rules);
        for (int day = 0; day < 20 && !step.Arrived; day++) step = Advance(world, rules);
        Assert.True(step.Arrived);
        var area = MilitaryCombat.Area(world.Units[0], rules);
        Assert.False(area.Overlaps(target));
        Assert.True(area.IntersectsExpanded(target, 1));
    }

    [Theory]
    [InlineData(false, false, false)]
    [InlineData(false, false, true)]
    [InlineData(true, false, false)]
    [InlineData(true, true, false)]
    [InlineData(true, false, true)]
    public void FrozenPreviewMatchesEveryDailyStepAndEndpoint(bool catapult, bool weakened, bool relay)
    {
        var (world, rules) = Fixture(true);
        world.Replace(world.Units[0] with { FormationId = catapult ? 0 : 3, Waypoint = relay ? new(22, 72) : null });
        if (weakened) world.Replace(world.Settlements[2] with { Durability = 19 });
        var snapshot = world.CapturePresentation();
        string hash = snapshot.StateHash;
        var plan = MilitaryRouteQuery.Plan(snapshot, rules, world.Units[0], 0);
        Assert.Equal(NavigationStatus.Arrived, plan.Status);
        Assert.Equal(hash, snapshot.StateHash);
        var actual = new List<WorldPosition> { world.Units[0].Position };
        NavigationStep step;
        int days = 0;
        do
        {
            step = Advance(world, rules);
            actual.AddRange(step.Steps);
            Assert.True(++days < 100);
        } while (!step.Arrived);
        Assert.Equal(actual, plan.Steps);
        Assert.Equal(world.Units[0].Position, plan.Steps[^1]);
        if (relay) Assert.Contains(new WorldPosition(22, 72), plan.Steps);
        var area = MilitaryCombat.Area(world.Units[0], rules);
        var target = rules.SettlementArea(world.Settlements[2]);
        Assert.False(area.Overlaps(target));
        Assert.Equal(!catapult || weakened, area.IntersectsExpanded(target, 1));
    }

    [Fact]
    public void PreviewSeparatesSearchLimitsFromBlockedRoutesAndReplansAfterObstacleRemoval()
    {
        var (world, rules) = Fixture(true);
        var unit = world.Units[0] with { Policies = new(AttackApproachingEnemies: true) };
        world.Replace(unit);
        Assert.Equal(NavigationStatus.Planning, MilitaryRouteQuery.Plan(world.CapturePresentation(), rules, unit, 0, maximumExpanded: 1).Status);
        world.Replace(world.Units[9] with { OwnerId = 2, CorpsId = 2 });
        Assert.Equal(NavigationStatus.Waiting, MilitaryRouteQuery.Plan(world.CapturePresentation(), rules, unit, 0).Status);
        world.RemoveUnit(9);
        Assert.Equal(NavigationStatus.Arrived, MilitaryRouteQuery.Plan(world.CapturePresentation(), rules, unit, 0).Status);
    }
}
