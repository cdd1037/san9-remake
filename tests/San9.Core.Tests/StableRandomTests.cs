using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class StableRandomTests
{
    [Fact]
    public void FixedConditionsSeparateDatesActorsTargetsPurposesAndGames()
    {
        int[] Draw(ulong seed, ConditionalRandomPurpose purpose, int day, int actor, int target) =>
            Enumerable.Range(0, 32).Select(i => ConditionalRandom.Percentile(seed, purpose,
                new(190, 1, day), actor, target + i)).ToArray();
        var baseline = Draw(190, ConditionalRandomPurpose.Recruitment, 1, 11, 20);
        Assert.Equal(baseline, Draw(190, ConditionalRandomPurpose.Recruitment, 1, 11, 20));
        Assert.False(baseline.SequenceEqual(Draw(191, ConditionalRandomPurpose.Recruitment, 1, 11, 20)));
        Assert.False(baseline.SequenceEqual(Draw(190, ConditionalRandomPurpose.SearchDiscovery, 1, 11, 20)));
        Assert.False(baseline.SequenceEqual(Draw(190, ConditionalRandomPurpose.Recruitment, 2, 11, 20)));
        Assert.False(baseline.SequenceEqual(Draw(190, ConditionalRandomPurpose.Recruitment, 1, 12, 20)));
        Assert.False(baseline.SequenceEqual(Draw(190, ConditionalRandomPurpose.Recruitment, 1, 11, 21)));
        Assert.All(baseline, roll => Assert.InRange(roll, 0, 99));
    }

    [Fact]
    public void RunStreamsUseRunSeedAndKeepPurposesIsolated()
    {
        var world = WorldState.Create(new(190, 1, 1), 190);
        var first = world.BeginMutation();
        var second = world.BeginMutation();
        second.SetSimulation(second.Simulation with { RunSeed = 777 });
        var third = world.BeginMutation();
        third.SetSimulation(third.Simulation with { RunSeed = 777 });
        third.NextRandom("another-purpose");
        var a = Enumerable.Range(0, 4).Select(_ => first.NextRandom("test-purpose")).ToArray();
        var b = Enumerable.Range(0, 4).Select(_ => second.NextRandom("test-purpose")).ToArray();
        var c = Enumerable.Range(0, 4).Select(_ => third.NextRandom("test-purpose")).ToArray();
        Assert.False(a.SequenceEqual(b));
        Assert.Equal(b, c);
        Assert.Equal(190UL, second.Simulation.InitialSeed);
    }

    [Fact]
    public void NextUInt64_MatchesProjectOwnedReferenceVector()
    {
        StableRandom random = new(0UL);

        Assert.Equal(0xE220A8397B1DCDAFUL, random.NextUInt64());
        Assert.Equal(0x6E789E6AA1B965F4UL, random.NextUInt64());
        Assert.Equal(0x06C45D188009454FUL, random.NextUInt64());
        Assert.Equal(0xF88BB8A8724C81ECUL, random.NextUInt64());
    }
}
