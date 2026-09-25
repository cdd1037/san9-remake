using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class UnitTerminationTests
{
    private static WorldState World(bool home = true, uint homeWounded = 0, uint winnerWounded = 0, uint draft = 0) =>
        WorldState.Create(new(190, 1, 1), 42,
            [new(1, 0), new(2, 0)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100) { Wounded = homeWounded, DraftPopulation = draft }], [],
            units: [new(0, 1, new(2, 2), 3) { Wounded = 2, ServicePlaceId = home ? 1 : null, CargoMoney = 100 },
                new(1, 2, new(3, 2), 10) { Wounded = winnerWounded, Morale = 95 }], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(1000, 1000)) { IsRoot = true }, new San9.Core.Domain.CorpsState(2, 2, null, new(0, 0)) { IsRoot = true }]);

    [Fact]
    public void VictoryTransfersTreasuryAndCargoAndDeletesDefeatedIdentityOnce()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        UnitTerminationService.Complete(mutation, 0, new(1, false), 1, new([]), events);
        Assert.Throws<InvalidOperationException>(() => UnitTerminationService.Complete(mutation, 0, new(1, false), 1, new([]), events));
        world.Commit(mutation);
        var state = world.CreateSnapshot();
        var winner = Assert.Single(state.Units);
        Assert.Equal(1, winner.Id);
        Assert.Equal(2u, winner.Wounded);
        Assert.Equal(100, winner.Morale);
        Assert.Equal(2u, Assert.Single(state.Settlements).Wounded);
        Assert.Equal(500u, state.Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        Assert.Equal(600u, state.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money);
        Assert.Equal(500u, state.Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Food);
        Assert.Equal(1, Assert.Single(events.OfType<UnitTerminatedEvent>()).UnrecoveredSoldiers);
    }

    [Theory]
    [InlineData(true, 2u, 2u, 1L)]
    [InlineData(false, 0u, 5u, 0L)]
    public void DefeatWithoutWinnerReturnsToWoundedAndPopulationPools(bool home, uint wounded, uint draft, long missing)
    {
        var world = World(home);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        UnitTerminationService.Complete(mutation, 0, null, 1, new([]), events);
        world.Commit(mutation);
        var city = Assert.Single(world.CreateSnapshot().Settlements);
        Assert.Equal(wounded, city.Wounded);
        Assert.Equal(draft, city.DraftPopulation);
        Assert.Equal(missing, Assert.Single(events.OfType<UnitTerminatedEvent>()).UnrecoveredSoldiers);
        Assert.Equal(1000u, world.CreateSnapshot().Corps.Single(f => f.IsRoot && f.OwnerId == 1).Treasury.Money);
        Assert.Equal(0u, world.CreateSnapshot().Corps.Single(f => f.IsRoot && f.OwnerId == 2).Treasury.Money);
    }

    [Theory]
    [InlineData(true)]
    [InlineData(false)]
    public void CapacityLossNeverFlowsBackIntoDefeatedUnit(bool winner)
    {
        var world = World(homeWounded: 49999, winnerWounded: 49999, draft: 499999);
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        UnitTerminationService.Complete(mutation, 0, winner ? new BattleSource(1, false) : null, 1, new([]), events);
        world.Commit(mutation);
        var state = world.CreateSnapshot();
        Assert.Equal(50000u, state.Settlements[0].Wounded);
        Assert.Equal(winner ? 3 : 2, Assert.Single(events.OfType<UnitTerminatedEvent>()).UnrecoveredSoldiers);
        Assert.DoesNotContain(state.Units, u => u.Id == 0);
        if (winner) Assert.Equal(50000u, Assert.Single(state.Units).Wounded);
        else Assert.Equal(500001u, state.Settlements[0].DraftPopulation);
    }

    [Fact]
    public void InvalidWinnerRejectsBeforeResourceWrites()
    {
        var world = World();
        var mutation = world.BeginMutation();
        var events = new List<DomainEvent>();
        var original = mutation.Units[0];
        Assert.Throws<ArgumentException>(() => UnitTerminationService.Complete(mutation, 0, new(0, false), 1, new([]), events));
        Assert.Throws<ArgumentException>(() => UnitTerminationService.Complete(mutation, 0, new(900, false), 1, new([]), events));
        Assert.Equal(original, mutation.Units[0]);
        Assert.Equal(1000u, mutation.Corps.Values.Single(c => c.IsRoot && c.OwnerId == 1).Treasury.Money);
        Assert.Empty(events);
    }
}
