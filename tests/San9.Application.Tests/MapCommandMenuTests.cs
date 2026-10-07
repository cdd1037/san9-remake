using San9.Application.Presentation;
using San9.Core.Domain;
using System.Collections.Immutable;

namespace San9.Application.Tests;

public sealed class MapCommandMenuTests
{
    [Fact]
    public void AuthoredPurposeAndDemolitionCapabilityControlMenuWithoutSourceSlotAssumptions()
    {
        var source = G01ScenarioTests.Fixture();
        var definition = source with { Military = new(4, 4, Enumerable.Repeat(0, 16).ToImmutableArray(), [new(0, 1, null)],
            [new(701, 1, 50, 10000, 5, new(1, 1), TravelMode.Land), new(903, 1, 50, 10000, 5, new(1, 1), TravelMode.Land)],
            [new(1, new(7, 7), new(1, 1)) { CanDemolish = true }, new(2, new(12, 12), new(1, 1))]) {
                ConstructionFormations = new(701, null), TransportFormations = new(903, null)
            } };
        var world = GameSession.Start(source, 1).Snapshot;
        world = world with { Settlements = world.Settlements.Select(s => s.Id == 1 ? s with { Kind = SettlementKind.Facility } : s).ToArray(),
            Units = [new(81, 1, new(12, 12), 1000) { FormationId = 701 }, new(82, 1, new(12, 12), 1000) { FormationId = 903 }],
            Simulation = world.Simulation with { NextUnitId = 83 } };
        var view = new StrategySnapshot(definition, world);
        Assert.Contains(MapCommandMenu.Settlement(view, 1).Single(e => e.Id == "facilities").Children, e => e.Id == "demolish");
        Assert.Equal("march", Assert.Single(MapCommandMenu.Unit(view, 81)).Id);
        Assert.Equal("redirect-transport", Assert.Single(MapCommandMenu.Unit(view, 82)).Id);
    }

    [Fact]
    public void NonCityFacilityDoesNotOfferCityDomesticCommands()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var city = new StrategySnapshot(definition, world);
        var facility = new StrategySnapshot(definition, world with { Settlements = world.Settlements
            .Select(s => s.Id == 1 ? s with { Kind = SettlementKind.Facility } : s).ToArray() });
        var domestic = MapCommandMenu.Settlement(facility, 1).Single(e => e.Id == "facilities");
        Assert.Equal(new[] { "repair", "train" }, domestic.Children.Select(c => c.Id));
        Assert.Contains(MapCommandMenu.Settlement(city, 1).Single(e => e.Id == "facilities").Children, c => c.Id == "commerce");
        Assert.Equal(MapCommandMenu.Settlement(city, 1).Skip(1).SelectMany(g => g.Children).Select(c => (c.Id, c.UnavailableReasonKey)),
            MapCommandMenu.Settlement(facility, 1).Skip(1).SelectMany(g => g.Children).Select(c => (c.Id, c.UnavailableReasonKey)));
    }

    [Fact]
    public void ObjectKindsWithSameIdHaveIndependentMenusAndQueriesDoNotChangeWorld()
    {
        var definition = G01ScenarioTests.Fixture();
        var session = GameSession.Start(definition, 1);
        var world = session.Snapshot with { Units = [new UnitState(1, 1, new(12, 12), 1000) { FormationId = 1 }] };
        var view = new StrategySnapshot(definition, world);
        var hash = world.StateHash;
        var selection = new StrategySelection(session);
        selection.SelectBuilding(2);
        Assert.NotEmpty(MapCommandMenu.Settlement(view, 1).SelectMany(e => e.Children));
        Assert.All(MapCommandMenu.Unit(view, 1), entry => Assert.Empty(entry.Children));
        Assert.NotEmpty(MapCommandMenu.Unit(view, 1));
        Assert.Equal(2, selection.BuildingId);
        Assert.Equal(hash, world.StateHash);
    }

    [Fact]
    public void MenusRequireCurrentOwnedVisibleObjects()
    {
        var definition = G01ScenarioTests.Fixture();
        var world = GameSession.Start(definition, 1).Snapshot;
        var place = world.Settlements.Single(s => s.Id == 1);
        foreach (var unavailable in new[] { place with { OwnerId = 2 }, place with { OwnerId = null },
            place with { IsDormant = true }, place with { IsDestroyed = true }, place with { CanHostPersons = false } })
            Assert.Empty(MapCommandMenu.Settlement(new(definition, world with {
                Settlements = world.Settlements.Select(s => s.Id == unavailable.Id ? unavailable : s).ToArray() }), 1));
        Assert.Empty(MapCommandMenu.Settlement(new(definition, world), 999));
        var unit = new UnitState(1, 1, new(12, 12), 1000) { FormationId = 1 };
        foreach (var unavailable in new[] { unit with { OwnerId = 2 }, unit with { Soldiers = 0 },
            unit with { FormationId = null }, unit with { IsEmbedded = true }, unit with { IsPersonnel = true } })
            Assert.Empty(MapCommandMenu.Unit(new(definition, world with { Units = [unavailable] }), 1));
        Assert.Empty(MapCommandMenu.Unit(new(definition, world with { Units = [] }), 1));
    }
}
