using System.Collections.Immutable;
using System.Text.Json;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class AttackNavigationCacheTests
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
        return step;
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void UnchangedAttackBlockageIsReusedAfterSerializationAndReleaseWakesImmediately(bool settlement)
    {
        var (world, rules) = Fixture(settlement);
        var first = Advance(world, rules);
        Assert.Equal(NavigationStatus.Waiting, first.Status);
        Assert.NotNull(first.Plan!.BlockedSearch!.Approach);
        var restored = JsonSerializer.Deserialize<MovementPlan>(JsonSerializer.Serialize(first.Plan))!;
        Assert.True(restored.IsValidFrom(world.Units[0].Position));
        world.Replace(world.Units[0] with { MovementPlan = restored });
        using var diagnostics = new PathSearchDiagnostics();
        for (int day = 0; day < 10; day++) Assert.Equal(NavigationStatus.Waiting, Advance(world, rules).Status);
        Assert.Equal(0, diagnostics.Queries);
        world.RemoveUnit(9);
        Assert.Equal(NavigationStatus.Moving, Advance(world, rules).Status);
        Assert.True(diagnostics.Queries > 0);
    }

    [Theory]
    [InlineData("reply")]
    [InlineData("position")]
    public void TargetChangesInvalidateAttackBlockage(string change)
    {
        var (world, rules) = Fixture();
        Assert.NotNull(Advance(world, rules).Plan!.BlockedSearch);
        world.Replace(change == "reply" ? world.Units[1] with { FormationId = 3 } : world.Units[1] with { Position = new(40, 50) });
        using var diagnostics = new PathSearchDiagnostics();
        var result = Advance(world, rules);
        Assert.True(diagnostics.Queries > 0);
        if (change == "position") Assert.Equal(NavigationStatus.Moving, result.Status);
        else Assert.Equal(20, result.Plan!.BlockedSearch!.Approach!.ReplyAttack!.Range);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void SiegeThresholdChangesInvalidateButUnrelatedTroopChangesDoNot(bool durability)
    {
        var (world, rules) = Fixture(true);
        Assert.NotNull(Advance(world, rules).Plan!.BlockedSearch);
        world.Replace(world.Settlements[2] with { Soldiers = 1999 });
        using var diagnostics = new PathSearchDiagnostics();
        Advance(world, rules);
        Assert.Equal(0, diagnostics.Queries);
        world.Replace(durability ? world.Settlements[2] with { Durability = 19 } : world.Settlements[2] with { Soldiers = 999 });
        var result = Advance(world, rules);
        Assert.True(diagnostics.Queries > 0);
        Assert.True(result.Plan!.BlockedSearch!.Approach!.CloseSiege);
    }

    [Fact]
    public void RockfallBlockerOutsideMovementOccupancyInvalidatesAttackBlockage()
    {
        var (world, rules) = Fixture(true);
        Assert.NotNull(Advance(world, rules).Plan!.BlockedSearch);
        var area = new MapOccupancy(new(50, 50), new(100, 100));
        var before = world.MovementOccupancy(rules, 0, null, area).ToArray();
        world.Replace(world.Settlements[3] with { IsDestroyed = true });
        Assert.Equal(before, world.MovementOccupancy(rules, 0, null, area).ToArray());
        using var diagnostics = new PathSearchDiagnostics();
        var result = Advance(world, rules);
        Assert.True(diagnostics.Queries > 0);
        Assert.Empty(result.Plan!.BlockedSearch!.Approach!.RockfallBlockers);
    }
}
