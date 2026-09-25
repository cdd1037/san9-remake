using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class TacticAvailabilityTests
{
    [Fact]
    public void SourcesAreCombinedByIdentityWithoutLosingOverlappingGrants()
    {
        var person = new OfficerState(1, 10, 1, default, false) { LearnedTactics = [9000, 9001] };
        var force = new ForceState(10, 0) { ProvidedTactics = [9001, 9002] };
        var rules = new TreasureRulebook([new(20, default, 0) { TacticId = 9001 },
            new(21, default, 0) { TacticId = 9003 }, new(22, default, 0) { TacticId = 9003 }]);
        TreasureState[] holdings = [new(20, 1, TreasureDiscovery.Discovered, null),
            new(21, 1, TreasureDiscovery.Discovered, null), new(22, 1, TreasureDiscovery.Discovered, null)];

        var result = TacticAvailability.ForOfficer(person, force, holdings, rules);

        Assert.Equal(4, result.Count);
        Assert.Equal(TacticSource.Learned, result[9000]);
        Assert.Equal(TacticSource.Learned | TacticSource.Force | TacticSource.Treasure, result[9001]);
        Assert.Equal(TacticSource.Force, result[9002]);
        Assert.Equal(TacticSource.Treasure, result[9003]);
        Assert.All(result.Keys, id => Assert.True(TacticAvailability.HasForMembers(id, [person], force, holdings, rules)));
        Assert.False(TacticAvailability.HasForMembers(9004, [person], force, holdings, rules));
        Assert.Equal([9000, 9001], person.LearnedTactics.ToArray());
    }

    [Fact]
    public void OnlyDiscoveredHoldingsOfParticipatingPeopleProvideTactics()
    {
        var person = new OfficerState(1, 10, 1, default, false);
        var other = person with { Id = 2, LearnedTactics = [99] };
        var force = new ForceState(10, 0);
        var rules = new TreasureRulebook(Enumerable.Range(20, 4).Select(id => new TreasureRule(id, default, 0) { TacticId = id }));
        TreasureState[] holdings = [new(20, 1, TreasureDiscovery.Buried, 1), new(21, 1, TreasureDiscovery.Disabled, null),
            new(22, 2, TreasureDiscovery.Discovered, null), new(23, null, TreasureDiscovery.Discovered, null)];
        Assert.Empty(TacticAvailability.ForOfficer(person, force, holdings, rules));
        Assert.False(TacticAvailability.HasForMembers(22, [person], force, holdings, rules));
        Assert.False(TacticAvailability.HasForMembers(99, [person], force, holdings, rules));
        Assert.True(TacticAvailability.HasForMembers(22, [person, other], force, holdings, rules));
        Assert.True(TacticAvailability.HasForMembers(99, [person, other], force, holdings, rules));
    }

    [Fact]
    public void ForceChangeAndTreasureTransferRecomputeSourcesWithoutChangingLearningOrOldResults()
    {
        var person = new OfficerState(1, 10, 1, default, false) { LearnedTactics = [90] };
        var first = new ForceState(10, 0) { ProvidedTactics = [91] };
        var second = new ForceState(11, 0) { ProvidedTactics = [92] };
        var rules = new TreasureRulebook([new(20, default, 0) { TacticId = 93 }]);
        var item = new TreasureState(20, 1, TreasureDiscovery.Discovered, null);
        var before = TacticAvailability.ForOfficer(person, first, [item], rules);
        var moved = person with { OwnerId = 11 };
        var after = TacticAvailability.ForOfficer(moved, second, [item with { HolderId = 2 }], rules);
        Assert.Equal([90, 91, 93], before.Keys.Order());
        Assert.Equal([90, 92], after.Keys.Order());
        Assert.Equal([90], TacticAvailability.ForOfficer(moved, first, [], rules).Keys);
        Assert.Equal([90], TacticAvailability.ForOfficer(moved with { OwnerId = null }, null, [], rules).Keys);
        Assert.Equal([90], moved.LearnedTactics.ToArray());
        Assert.Equal([90, 91, 93], TacticAvailability.ForOfficer(person, first, [item], rules).Keys.Order());
    }
}
