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
        { Combat = new(10, 10, 10, 10, 50), Attack = new(range, true) { RangedSiege = id == 0 }, CaptureRanks = [BattleRank.Front] };
        var rules = new MilitaryRulebook(new(20, 20, new int[400].ToImmutableArray(), [new(0, 10, null)],
            [Formation(0, 10, new(1, 1)), Formation(1, 1, new(1, 1)), Formation(2, 1, new(1, 100)), Formation(3, 20, new(1, 1))],
            [new(2, new(70, 50), new(1, 1)) { FacilityTypeId = 100, Combat = new(10, 10, 10, 10, 50), Attack = new(1, false) },
             new(3, new(70, 80), new(1, 1)) { FacilityTypeId = 100 }])
        {
            Rockfall = new(5, 25), Elevations = new int[400].ToImmutableArray(),
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
        var step = MilitaryNavigation.Advance(world, unit, rules, MilitaryNavigation.Budget(unit, rules));
        world.Replace(MilitaryNavigation.Apply(unit, step));
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
        Assert.Equal(5, MilitaryCombat.ApproachRange(world, unit, unit.Order!, rules, p));
    }
}
