using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class ForceRelationCycleTests
{
    [Theory]
    [InlineData(false, 0, 1)]
    [InlineData(false, 38, 39)]
    [InlineData(false, 39, 39)]
    [InlineData(false, 50, 50)]
    [InlineData(false, 51, 50)]
    [InlineData(false, 100, 99)]
    [InlineData(true, 0, 4)]
    [InlineData(true, 35, 39)]
    [InlineData(true, 36, 36)]
    [InlineData(true, 50, 50)]
    [InlineData(true, 51, 47)]
    [InlineData(true, 100, 96)]
    public void QuarterUsesAsymmetricDeadBandsWithoutRandom(bool barbarian, int before, int after)
    {
        foreach (bool firstIsBarbarian in new[] { false, true })
        {
            var state = WorldState.Create(new(190, 3, 1), 7,
                [new(101, 0, 0, 0) { IsBarbarian = barbarian && firstIsBarbarian },
                 new(205, 0, 0, 0) { IsBarbarian = barbarian && !firstIsBarbarian }], [], [],
                relations: before == 0 ? [] : [new(101, 205, before)]);
            var world = state.BeginMutation();
            world.SetClock(new(190, 4, 1));
            var random = world.Simulation.RandomStreams;
            List<DomainEvent> events = [];
            ForceRelations.AdvanceQuarter(world, events);
            Assert.Equal(after, ForceRelations.Get(world, 101, 205).Value);
            Assert.Equal(after, ForceRelations.Get(world, 205, 101).Value);
            Assert.Equal(before == after ? 0 : 1, events.OfType<ForceRelationChangedEvent>().Count());
            Assert.Equal(random, world.Simulation.RandomStreams);
        }
    }

    [Theory]
    [InlineData(29, 80)]
    [InlineData(30, 79)]
    public void ElapsedGateAndAllianceDoNotChangePairEligibility(int elapsed, int expected)
    {
        var state = WorldState.Create(new(190, 3, 1), 7,
            [new(1, 0, 0, 0), new(2, 0, 0, 0), new(3, 0, 0, 0),
             new(4, 0, 0, 0) { IsEliminated = true }, new(5, 0, 0, 0) { IsDormant = true }], [], [],
            relations: [new(1, 2, 80)], alliance: new(3, 1, [1, 2], 36));
        var world = state.BeginMutation();
        world.SetClock(elapsed == 29 ? new(190, 3, 30) : new(190, 4, 1));
        List<DomainEvent> events = [];
        ForceRelations.AdvanceQuarter(world, events);
        Assert.Equal(expected, ForceRelations.Get(world, 1, 2).Value);
        Assert.Equal(elapsed == 29 ? 0 : 3, events.OfType<ForceRelationChangedEvent>().Count());
        Assert.DoesNotContain(world.Relations.Keys, p => p.FirstId >= 4 || p.SecondId >= 4);
    }
}
