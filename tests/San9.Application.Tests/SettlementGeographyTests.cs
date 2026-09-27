using San9.Application.Presentation;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class SettlementGeographyTests
{
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
