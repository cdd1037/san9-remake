using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class WorldInvariantTests
{
    private static WorldState Create() => WorldState.Create(new(190, 1, XunIndex.Early), 17,
        [new(1, 100, 100, 50), new(2, 100, 100, 50)],
        [new(1, 1, 100, 10, 100, 50, 500, 10, 100, 10, 100)],
        [new(1, 1, 1, new(), false)],
        [new(1, 1, MapCoordinates.Center(new(10, 10)), 100)], [new(1, 1, 1, null)],
        [new(1, 1, TreasureDiscovery.Discovered, null)], [new(1, 2, 50)]);

    private static WorldState Recreate(WorldSnapshot state) => WorldState.Create(state.Clock, state.Simulation.InitialSeed,
        state.Forces, state.Settlements, state.Officers, state.Units, state.Corps, state.Treasures, state.Relations);

    public static IEnumerable<object[]> DuplicateCases =>
        from collection in Enumerable.Range(0, 7)
        from different in new[] { false, true }
        select new object[] { collection, different };

    [Theory]
    [MemberData(nameof(DuplicateCases))]
    public void InitializationRejectsDuplicateIdentitiesEvenWhenValuesMatch(int collection, bool different)
    {
        var state = Create().CreateSnapshot();
        state = collection switch
        {
            0 => state with { Forces = [.. state.Forces, different ? state.Forces[0] with { Money = 99 } : state.Forces[0]] },
            1 => state with { Settlements = [.. state.Settlements, different ? state.Settlements[0] with { Commerce = 11 } : state.Settlements[0]] },
            2 => state with { Officers = [.. state.Officers, different ? state.Officers[0] with { Acted = true } : state.Officers[0]] },
            3 => state with { Units = [.. state.Units, different ? state.Units[0] with { Position = new(11, 10) } : state.Units[0]] },
            4 => state with { Corps = [.. state.Corps, different ? state.Corps[0] with { Treasury = new(10, 10) } : state.Corps[0]] },
            5 => state with { Treasures = [.. state.Treasures, different ? state.Treasures[0] with { HolderId = null } : state.Treasures[0]] },
            _ => state with { Relations = [.. state.Relations, different ? state.Relations[0] with { Value = 51 } : state.Relations[0]] }
        };
        Assert.Throws<ArgumentException>(() => Recreate(state));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    [InlineData(6)]
    [InlineData(7)]
    public void InitializationAndCommitRejectInvalidStatesWithoutPublishing(int violation)
    {
        var world = Create();
        var before = world.CreateSnapshot();
        var mutation = world.BeginMutation();
        mutation.SetClock(before.Clock.Advance());
        mutation.NextRandom("invariant-test");
        mutation.Replace(before.Forces[0] with { Money = 1 });
        switch (violation)
        {
            case 0: mutation.Replace(before.Forces[0] with { BaseTrust = -1 }); break;
            case 1: mutation.Replace(before.Settlements[0] with { Commerce = 101 }); break;
            case 2: mutation.Replace(before.Officers[0] with { OwnerId = 2 }); break;
            case 3: mutation.Replace(before.Units[0] with { OwnerId = 99 }); break;
            case 4: mutation.Replace(before.Corps[0] with { LeaderId = 99 }); break;
            case 5: mutation.Replace(before.Treasures[0] with { HolderId = 99 }); break;
            case 6: mutation.Replace(before.Relations[0] with { Value = 101 }); break;
            case 7: mutation.SetClock(default); break;
        }

        Assert.ThrowsAny<ArgumentException>(() => WorldState.Create(mutation.Clock, mutation.Simulation.InitialSeed,
            mutation.Forces.Values, mutation.Settlements.Values, mutation.Officers.Values,
            mutation.Units.Values, mutation.Corps.Values, mutation.Treasures.Values, mutation.Relations.Values));
        Assert.ThrowsAny<ArgumentException>(() => world.Commit(mutation));
        Assert.Same(before, world.CreateSnapshot());
        Assert.Equal(before.Revision, world.Revision);
        Assert.Equal(before.Clock, world.Clock);
        Assert.Equal(before.Simulation.RandomStreams, world.Simulation.RandomStreams);
    }

    [Fact]
    public void RelatedOwnershipChangesAreValidatedTogetherAtCommit()
    {
        var world = Create();
        var before = world.CreateSnapshot();
        var mutation = world.BeginMutation();
        mutation.Replace(before.Settlements[0] with { OwnerId = 2 });
        mutation.Replace(before.Officers[0] with { OwnerId = 2 });
        mutation.Replace(before.Corps[0] with { OwnerId = 2 });

        world.Commit(mutation);

        var after = world.CreateSnapshot();
        Assert.Equal(before.Revision + 1, after.Revision);
        Assert.Equal(2, after.Settlements[0].OwnerId);
        Assert.Equal(2, after.Officers[0].OwnerId);
        Assert.Equal(2, after.Corps[0].OwnerId);
        Assert.Equal(1, before.Settlements[0].OwnerId);
        Assert.Equal(1, before.Officers[0].OwnerId);
    }
}
