using San9.Application;
using San9.Application.Presentation;
using San9.Client.Godot;
using San9.Content.Scenarios;
using San9.Core.Domain;

namespace San9.Client.Godot.Tests;

public sealed class InitialMapFocusTests
{
    [Theory]
    [InlineData(false, 9, 12)]
    [InlineData(true, 23, 17)]
    public void StartupUsesRulerServicePlaceInsteadOfFirstCityOrFieldPosition(bool deployed, int x, int y)
    {
        var view = View(deployed);
        var place = Assert.IsType<SettlementView>(view.PlayerRulerServicePlace);
        Assert.Equal(deployed ? 82 : 9, place.Id);
        var options = ClientOptions.Parse([], Environment.CurrentDirectory);
        Assert.Equal(new(x, y), options.ResolveMapCenter(40, 30, new(place.X, place.Y)));
        Assert.Equal(1, options.Zoom);
    }

    [Fact]
    public void MissingServicePlaceOrPlayerUsesMapCenterInsteadOfAnotherOwnedCity()
    {
        var view = View(true);
        var missing = view.ForWorld(view.World with { Units = [view.World.Units[0] with { ServicePlaceId = null }] });
        var destroyed = view.ForWorld(view.World with
        {
            Settlements = view.World.Settlements.Select(s => s.Id == 82 ? s with { IsDestroyed = true } : s).ToArray()
        });
        var options = ClientOptions.Parse([], Environment.CurrentDirectory);
        foreach (var state in new[] { missing, destroyed, View(false, player: false) })
        {
            Assert.Null(state.PlayerRulerServicePlace);
            Assert.Equal(new(20, 15), options.ResolveMapCenter(40, 30));
        }
    }

    private static StrategySnapshot View(bool deployed, bool player = true)
    {
        var definition = new ScenarioInitialState(ScenarioInitialState.CurrentSchemaVersion, "initial-focus", 190, 1, 1, 5,
            [new(5, "force", 0, 100) { LeaderId = 11 }],
            [new(1, "first-city", 5, 2, 3, 100, 100, 100, 50, []) { CorpsId = 5 },
                new(9, "ruler-city", 5, 9, 12, 100, 100, 100, 50, []) { CorpsId = 5 },
                new(82, "service-facility", 5, 23, 17, 100, 100, 100, 50, []) { CorpsId = 5, Kind = SettlementKind.Facility }], [], [])
        {
            Officers = [new("ruler", new(11, 5, 9, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler })],
            Information = new([], [], [], [new(5, 5, 11, new(0, 0)) { IsRoot = true }], [])
        };
        var view = GameSession.Start(definition, 1, playerForceIds: player ? [5] : []).View;
        return deployed ? view.ForWorld(view.World with
        {
            Officers = [view.World.Officers[0] with { SettlementId = null, UnitId = 70 }],
            Units = [new(70, 5, MapCoordinates.Center(new(4, 5)), 100) { CorpsId = 5, ServicePlaceId = 82 }]
        }) : view;
    }
}
