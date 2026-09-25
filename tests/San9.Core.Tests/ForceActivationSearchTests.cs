using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceActivationSearchTests
{
    private static ForceActivationSearchEventDefinition Definition(int participants = 5) =>
        new(5, 2, 3, [4], Enumerable.Range(2, participants).ToImmutableArray(), 700, 2, 500000, 10, new("penglai"))
        { Id = "penglai", Priority = -3, Consumption = EventConsumption.OnSuccess };
    private static readonly PersonPlace[] Places = Enumerable.Range(1, 6)
        .Select(id => new PersonPlace(id, MapCoordinates.Center(new(id - 1, 0)))).ToArray();
    private static SimulationRules Rules(int participants = 5) => new()
    {
        Personnel = new(Places, new Dictionary<int, int> { [5] = 1, [4] = 3 },
            cityProvinces: new Dictionary<int, int> { [1] = 1, [2] = 2, [6] = 3 }, searchEvents: [Definition(participants)]),
        Military = new(new(6, 1, Enumerable.Repeat(0, 6).ToImmutableArray(), [new(0, 1, 1)], [],
            Places.Where(p => p.Id != 5).Select(p => new BattlefieldPlace(p.Id, p.Position, new(1, 1))).ToImmutableArray())
            { RegionIds = Enumerable.Range(1, 6).Select(i => (int?)i).ToImmutableArray() })
    };
    private static WorldState World(int participants = 5) => WorldState.Create(new(190, 2, 1), 17,
        [new(1, 700) { BaseTrust = 700, LeaderId = 1 },
         new(2, 100) { BaseTrust = 100, LeaderId = 2, IsDormant = true, IsBarbarian = true },
         new(3, 100) { BaseTrust = 100, LeaderId = 10 }],
        Places.Where(p => p.Id != 5).Select(p => new SettlementState(p.Id, p.Id switch { 3 => 2, 4 => null, 6 => 3, _ => 1 },
            p.Id == 3 ? 100000u : 0u, 100, 100, 60, 100, 0, 100, 0, 100)
            { IsDormant = p.Id is 3 or 4, Kind = p.Id == 4 ? SettlementKind.Facility : SettlementKind.City, ConsumesFood = false }),
        new[] { new OfficerState(1, 1, 1, new(60, 60, 60, 60), false), new OfficerState(10, 3, 6, new(), false),
            new OfficerState(11, 1, 1, new(60, 60, 60, 60), false) }
            .Concat(Enumerable.Range(2, participants).Select(id => new OfficerState(id, 2, 3, new(70, 70, 70, 70), false)
                { IsDormant = true, Status = id == 2 ? OfficerStatus.Ruler : OfficerStatus.Serving })),
        controls: [new("player", 1, ControllerKind.Player), new("ai:3", 3, ControllerKind.Automatic)], corps: [new San9.Core.Domain.CorpsState(1, 1, 1, new(100, 100)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, 2, new(10000, 300000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(3, 3, 10, new(100, 100)) { IsRoot = true }]);
    private static PersistentTask Task() => new(Guid.NewGuid(), "personnel-search", "player", 1, 11, 1,
        new(190, 2, 1), new(190, 2, 1), 0, 0)
        { Journey = new(5, null, 0) { Route = new(Places[0].Position, Places[4].Position, false) } };

    [Theory]
    [InlineData(5)]
    [InlineData(8)]
    public void ActivatesCompleteAuthoredGroupOnceWithoutSpendingMoneyOrChangingPersonalStats(int count)
    {
        var state = World(count); var m = state.BeginMutation(); var before = state.CreateSnapshot();
        m.SetSimulation(m.Simulation with { Boundary = SimulationBoundary.Stable, ActiveControl = 1 });
        List<DomainEvent> events = [];
        Assert.True(ForceActivationSearch.TryResolve(m, Task(), Definition(count), Rules(count), events));
        Assert.Equal(500000u, m.Settlements[3].Soldiers);
        Assert.Equal(0u, m.Settlements[4].Soldiers);
        Assert.Equal(2, m.Settlements[4].OwnerId);
        Assert.Equal(100, m.Settlements[3].Durability);
        Assert.Equal(10000u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Money); Assert.Equal(300000u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 2).Treasury.Food);
        Assert.Equal(100u, m.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money); Assert.Equal(710, m.Forces[1].BaseTrust);
        foreach (var person in before.Officers.Where(p => p.IsDormant))
            Assert.Equal(CanonicalJson.Hash(person with { IsDormant = false }), CanonicalJson.Hash(m.Officers[person.Id]));
        Assert.Equal(new[] { 1, 2, 3 }, m.Simulation.Controls.Select(c => c.ForceId));
        Assert.Equal(3, m.Simulation.Controls[m.Simulation.ActiveControl].ForceId);
        Assert.Empty(m.Simulation.RandomStreams);
        Assert.Equal(count, Assert.Single(events.OfType<ForceActivatedBySearchEvent>()).OfficerIds.Length);
        Assert.False(ForceActivationSearch.TryResolve(m, Task(), Definition(count), Rules(count), events));
        state.Commit(m);
        Assert.Equal(state.CreateSnapshot().StateHash, WorldState.Restore(state.CreateSnapshot()).CreateSnapshot().StateHash);
    }

    [Theory]
    [InlineData("trust")]
    [InlineData("province")]
    [InlineData("place")]
    [InlineData("control")]
    [InlineData("consumed")]
    public void IneligibleSearchHasNoEffectsOrRandomDraw(string failure)
    {
        var m = World().BeginMutation(); var task = Task();
        switch (failure)
        {
            case "trust": m.Replace(m.Forces[1] with { BaseTrust = 699 }); break;
            case "province": m.Replace(m.Settlements[2] with { OwnerId = 3 }); break;
            case "place": task = task with { Journey = task.Journey! with { DestinationPlaceId = 1 } }; break;
            case "control": m.SetSimulation(m.Simulation with { Controls = [new("ai:1", 1, ControllerKind.Automatic)] }); break;
            case "consumed": m.SetSimulation(m.Simulation with { ConsumedEventKeys = [Definition().ConsumptionKey] }); break;
        }
        List<DomainEvent> events = [];
        Assert.False(ForceActivationSearch.TryResolve(m, task, Definition(), Rules(), events));
        Assert.True(m.AllForces[2].IsDormant); Assert.Empty(events); Assert.Empty(m.Simulation.RandomStreams);
    }

    [Theory]
    [InlineData(3, UnitOrderClass.Military, false)]
    [InlineData(4, UnitOrderClass.Military, true)]
    [InlineData(1, UnitOrderClass.Military, true)]
    [InlineData(3, UnitOrderClass.NonMilitary, true)]
    public void OccupancyUsesCurrentCityRegionAndMilitaryClass(int place, UnitOrderClass orderClass, bool occurs)
    {
        var m = World().BeginMutation();
        m.AddUnit(new(50, 3, Places[place - 1].Position, 100) { OrderClass = orderClass,
            FormationId = 1, Order = new(MilitaryOrderKind.AttackSettlement, null, 3) });
        List<DomainEvent> events = [];
        Assert.Equal(occurs, ForceActivationSearch.TryResolve(m, Task(), Definition(), Rules(), events));
    }

    [Fact]
    public void DormantPlacesCannotBeSearchedEvenWithoutRegionAssociation()
    {
        var m = World().BeginMutation();
        Assert.False(PersonnelMissions.CanSearch(m, 1, 3, new(Places)));
        Assert.False(PersonnelMissions.CanSearch(m, 1, 4, new(Places)));
    }

    [Fact]
    public void PartialProvinceCannotQualifyAndSuccessPreemptsLaterChanceCandidates()
    {
        var m = World().BeginMutation(); var rules = Rules(); List<DomainEvent> events = [];
        var provinces = new Dictionary<int, int> { [1] = 1, [2] = 2, [6] = 2 };
        var incomplete = new SimulationRules { Military = rules.Military, Personnel = new(Places, cityProvinces: provinces, searchEvents: [Definition()]) };
        Assert.False(ForceActivationSearch.TryResolve(m, Task(), Definition(), incomplete, events));
        m.Replace(m.Forces[1] with { BaseTrust = 999 });
        var ordered = new SimulationRules { Military = rules.Military, Personnel = new(Places, rules.Personnel.RegionCities, cityProvinces: rules.Personnel.CityProvinces,
            searchEvents: [new HealingSearchEventDefinition(10, [], 1) { Id = "healer", Priority = -7 }, Definition()]) };
        Assert.Null(new PersonnelMissionRule(ordered, "personnel-search").Resolve(m, Task(), null, events));
        Assert.Equal(1000, m.Forces[1].BaseTrust);
        Assert.Single(events.OfType<ForceActivatedBySearchEvent>());
        Assert.DoesNotContain(events, e => e is SearchResolvedEvent or SearchHealingResolvedEvent);
        Assert.Empty(m.Simulation.RandomStreams);
        Assert.False(ForceActivationSearch.TryResolve(m, Task() with { ForceId = 3, OfficerId = 10 }, Definition(), rules, events));
    }
}
