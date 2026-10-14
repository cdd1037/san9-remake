using San9.Tests;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class OfficerAbilityRulesTests
{
    private static readonly OfficerAbilityRules Rules = new(new([
        new(10, new(10, 2, 0, 0), 0), new(11, new(6, 7, 0, 0), 0), new(12, new(99, 99, 99, 99), 0)]),
        new([new(1, 10000, 0) { AbilityBonuses = new(3, 0, 4, 0) }]));

    [Fact]
    public void BatchAndSingleQueriesAgreeAcrossOrderAndUseIndependentItemMaxima()
    {
        OfficerState[] people = [new(1, 1, 1, new(90, 80, 70, 60), false) { OfficeId = 1, Injury = 2 },
            new(2, 1, 1, new(50, 50, 50, 50), false)];
        TreasureState[] items = [new(10, 1, TreasureDiscovery.Discovered, null),
            new(11, 1, TreasureDiscovery.Discovered, null), new(12, null, TreasureDiscovery.Buried, 1)];
        var batch = Rules.EvaluateMany(people, items);
        Assert.Equal(new OfficerAbilities(85, 71, 60, 48), batch[1]);
        Assert.Equal(new OfficerAbilities(50, 50, 50, 50), batch[2]);
        foreach (var person in people)
        {
            Assert.Equal(Rules.Evaluate(person, items), batch[person.Id]);
            Assert.Equal(batch[person.Id], Rules.EvaluateMany(people.Reverse(), items.Reverse())[person.Id]);
        }
    }

    [Fact]
    public void TransactionReadsChangedSourcesWithoutPublishingOrRefreshingOfficerBonuses()
    {
        var world = WorldState.Create(new(190, 1, 1), 190, [new(1, 0)],
            [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
            [new(1, 1, 1, new(50, 50, 50, 50), false), new(2, 1, 1, new(50, 50, 50, 50), false)],
            treasures: [new(10, 1, TreasureDiscovery.Discovered, null)], corps: [new San9.Core.Domain.CorpsState(1, 1, null, new(100, 100)) { IsRoot = true }]);
        var before = world.CreateSnapshot();
        var mutation = world.BeginMutation(Rules);
        mutation.Replace(mutation.Treasures[10] with { HolderId = 2 });
        mutation.Replace(mutation.Officers[2] with { OfficeId = 1, Injury = 2, BaseAbilities = new(60, 50, 50, 50) });
        Assert.Equal(new OfficerAbilities(50, 50, 50, 50), mutation.Abilities(mutation.Officers[1]));
        Assert.Equal(new OfficerAbilities(61, 42, 44, 40), mutation.Abilities(mutation.Officers[2]));
        Assert.Equal(60, Rules.EvaluateMany(before.Officers, before.Treasures)[1].War);
        StateAssert.Equal(before, world.CreateSnapshot());
        world.Commit(mutation);
        var after = world.CreateSnapshot();
        Assert.Equal(61, Rules.EvaluateMany(after.Officers, after.Treasures)[2].War);
        Assert.Equal(50, Rules.EvaluateMany(before.Officers, before.Treasures)[2].War);
    }

    [Fact]
    public void DerivedQueriesDoNotChangeCanonicalStateOrSerializeResults()
    {
        var person = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { OfficeId = 1 };
        string before = CanonicalJson.Text(person);
        Assert.Equal(53, Rules.Evaluate(person, []).War);
        Assert.Equal(before, CanonicalJson.Text(person));
        using var json = System.Text.Json.JsonDocument.Parse(before);
        Assert.False(json.RootElement.TryGetProperty("abilities", out _));
        Assert.False(json.RootElement.TryGetProperty("itemBonuses", out _));
        Assert.False(json.RootElement.TryGetProperty("officeBonuses", out _));
    }
}
