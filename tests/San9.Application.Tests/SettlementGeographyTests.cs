using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class SettlementGeographyTests
{
    [Theory]
    [InlineData(1000, 5)]
    [InlineData(50000, 5)]
    [InlineData(50000, 3)]
    public void UnitWaypointMaskUsesPhysicalSizeWithoutBuildingScale(uint soldiers, int scale)
    {
        var source = G01ScenarioTests.Fixture();
        var definition = source with { Military = new(30, 30, [.. new int[900]], [new(0, 10, null)],
            [new(0, 100, 0, 100000, 1, new(4, 4), TravelMode.Land)
                { SoldierFootprints = [new(50000, new(10, 10))] }],
            [new(1, new(12, 12), new(1, 1)), new(2, new(132, 132), new(1, 1))])
            { Geography = new(scale, []) } };
        var unit = new UnitState(0, 1, new(52, 52), soldiers) { FormationId = 0, CorpsId = 1 };
        var snapshot = GameSession.Start(source, 1).Snapshot with { Units = [unit] };
        var view = new StrategySnapshot(definition, snapshot);
        var targets = new UnitCommandTargets(view, unit.Id, UnitCommandKind.March);
        var rules = view.MilitaryRules!;
        var actualArea = view.BattleArea(unit.Id, false)!.Value;
        Assert.True(rules.GeographicFootprint(unit).SetEquals(rules.GeographicCells(actualArea)));
        Assert.Equal(new Footprint(scale, scale), rules.SettlementArea(snapshot.Settlements.Single(s => s.Id == 1)).Footprint);
        Assert.True(targets.Select(new(1, false)));
        Assert.False(targets.SetWaypoint(unit.Cell));
        var nearby = new MapCell(12, 10);
        Assert.DoesNotContain(nearby, targets.ClearCells);
        Assert.True(targets.SetWaypoint(nearby));
        Assert.Equal(nearby, targets.Waypoint);
    }

    [Theory]
    [InlineData(5)]
    [InlineData(3)]
    public void MapAndBattleCoverageUseTheSameAuthoredScale(int scale)
    {
        var source = G01ScenarioTests.Fixture();
        var definition = source with { Military = new(20, 20, [.. new int[400]], [new(0, 1, null)], [],
            [new(1, MapCoordinates.Center(new(10, 10)), new(5, 5)),
             new(2, MapCoordinates.Center(new(15, 15)), new(3, 3))]) { Geography = new(scale, []) } };
        var world = GameSession.Start(source, 1).Snapshot;
        var view = new StrategySnapshot(definition, world);
        Assert.Equal(new Footprint(5 * scale, 5 * scale), view.SettlementGeographicArea(1)!.Value.Footprint);
        Assert.Equal(new Footprint(3 * scale, 3 * scale), view.SettlementGeographicArea(2)!.Value.Footprint);
        Assert.Equal(new Footprint(5 * scale, 5 * scale), view.BattleArea(1, true)!.Value.Footprint);
        Assert.Null(view.SettlementGeographicArea(999));
        var removed = new StrategySnapshot(definition, world with { Settlements = world.Settlements
            .Select(s => s.Id == 1 ? s with { IsDestroyed = true } : s).ToArray() });
        Assert.Null(removed.SettlementGeographicArea(1));
    }
}
