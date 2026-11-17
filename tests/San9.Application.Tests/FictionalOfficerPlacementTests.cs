using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;
using San9.Core.Simulation;
using San9.Tests;

namespace San9.Application.Tests;

public sealed class FictionalOfficerPlacementTests
{
    [Fact]
    public void PlacesExcludeBarbarianAndDormantAreasAndIgnoreInputOrder()
    {
        var definition = Fixture();
        var reordered = definition with
        {
            Settlements = definition.Settlements.Reverse().ToArray(),
            UnbuiltPlaces = definition.UnbuiltPlaces.Reverse().ToImmutableArray(),
            RegionCityIds = definition.RegionCityIds.Reverse().ToDictionary(p => p.Key, p => p.Value)
        };
        var settings = new NewGameSettings { Mode = GameMode.Fictional };
        var world = new ScenarioSessionDefinition(definition, [2]).CreateWorld(123, settings: settings).CreateSnapshot();
        var repeated = new ScenarioSessionDefinition(reordered, [2]).CreateWorld(123, settings: settings).CreateSnapshot();
        var places = world.Officers.Where(p => p.Id >= 1000)
            .Select(p => p.SettlementId ?? p.RegionId).Distinct().Order().ToArray();

        Assert.Equal(new int?[] { 1, 300, 501, 601, 604 }, places);
        StateAssert.Equal(world, repeated);
    }

    [Theory]
    [InlineData(false, false)]
    [InlineData(false, true)]
    [InlineData(true, true)]
    public void ReservedCityAndItsLocationsFollowActivationWhileKeepingInitialBarbarianIdentity(bool barbarian, bool selected)
    {
        var basis = Fixture();
        var definition = basis with
        {
            Forces = basis.Forces.Select(f => f.Id == 2 ? f with { IsBarbarian = barbarian, IsDormant = true } : f).ToArray(),
            Settlements = basis.Settlements.Select(s => s.Id is 2 or 504 ? s with { IsDormant = true } : s).ToArray(),
            Officers = basis.Officers.Select(o => o.State.OwnerId == 2 ? o with { State = o.State with { IsDormant = true } } : o).ToArray(),
            Military = new(3, 3, Enumerable.Repeat(0, 9).ToImmutableArray(), [new(0, 1, 1)], [],
                basis.Settlements.Select(s => new BattlefieldPlace(s.Id, s.Position, new(1, 1))).ToImmutableArray())
                { RegionIds = Enumerable.Repeat<int?>(501, 9).ToImmutableArray() },
            SearchEvents = [new ForceActivationSearchEventDefinition(1, 2, 2, [504], [4, 5, 6], 0, 0, 100, 0, new("test.activation"))
                { Id = "test.activation", Consumption = EventConsumption.OnSuccess }]
        };
        var session = GameSession.Start(definition, 123, playerForceIds: selected ? [2] : [1],
            settings: new() { Mode = GameMode.Fictional });
        var places = session.Snapshot.Officers.Where(p => p.Id >= 1000)
            .Select(p => (p.SettlementId ?? p.RegionId)!.Value).ToHashSet();

        foreach (int id in new[] { 2, 504, 602 })
            Assert.Equal(selected && !barbarian, places.Contains(id));
        Assert.Equal(!selected, session.Snapshot.Settlements.Single(s => s.Id == 2).IsDormant);
    }

    [Fact]
    public void EmptyCandidatePoolRejectsFictionalPlacement()
    {
        var basis = Fixture();
        var definition = basis with { Forces = basis.Forces.Select(f => f with { IsBarbarian = true }).ToArray(),
            Settlements = basis.Settlements.Select(s => s.Id == 300 ? s with { IsDormant = true } : s).ToArray() };
        var owned = new ScenarioSessionDefinition(definition);
        var error = Assert.Throws<ArgumentException>(() => owned.CreateWorld(123, settings: new() { Mode = GameMode.Fictional }));
        Assert.Equal("Fictional mode requires officer starting places.", error.Message);
    }

    private static ScenarioInitialState Fixture()
    {
        var basis = G01ScenarioTests.Fixture();
        var city = basis.Settlements[0];
        var regionCities = new Dictionary<int, int>
        {
            [501] = 1, [502] = 1, [503] = 1, [504] = 2, [505] = 400,
            [601] = 1, [602] = 2, [603] = 400, [604] = 300
        };
        return basis with
        {
            Forces = basis.Forces.Select(f => f with { IsBarbarian = f.Id == 2 }).ToArray(),
            Settlements = [.. basis.Settlements,
                city with { Id = 300, OwnerId = null, CorpsId = null },
                city with { Id = 400, OwnerId = null, CorpsId = null, IsDormant = true },
                city with { Id = 501, Kind = SettlementKind.Facility },
                city with { Id = 502, Kind = SettlementKind.Facility, IsDormant = true },
                city with { Id = 503, Kind = SettlementKind.Facility, CanHostPersons = false },
                city with { Id = 504, Kind = SettlementKind.Facility },
                city with { Id = 505, Kind = SettlementKind.Facility },
                city with { Id = 506, Kind = SettlementKind.Facility }],
            RegionCityIds = regionCities,
            RegionNameKeys = regionCities.Keys.Append(605).ToDictionary(id => id, id => $"region.{id}"),
            UnbuiltPlaces = Enumerable.Range(601, 5).Select(id => new PersonPlace(id, city.Position)).ToImmutableArray(),
            Officers = [.. basis.Officers, .. Enumerable.Range(1000, 32).Select(id => new OfficerInitialState("officer.free",
                new(id, null, 1, new(50, 50, 50, 50), false)
                { Status = OfficerStatus.Free }))]
        };
    }
}
