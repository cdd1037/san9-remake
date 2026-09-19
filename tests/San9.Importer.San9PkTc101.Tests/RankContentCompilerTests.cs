using San9.Importer.San9PkTc101.Scenarios;

namespace San9.Importer.San9PkTc101.Tests;

public sealed class RankContentCompilerTests
{
    [Fact]
    public void EveryRankIncludingLowestKeepsIdentityValuesAndTextReference()
    {
        var source = Enumerable.Range(0, San9ScenarioRankArchive.RecordCount)
            .Select(id => new San9ScenarioRankRecord(id, (uint)(60000 - id * 5000),
                999, 123, 456, (ushort)(8 - id), (ushort)((8 - id) * 100))).ToArray();
        List<int> requestedNames = [];
        var ranks = RankContentCompiler.Compile(source, id => { requestedNames.Add(id); return $"rank.{id}"; });
        Assert.Equal(Enumerable.Range(0, 9), ranks.Select(r => r.Id));
        Assert.Equal(Enumerable.Range(0, 9), requestedNames);
        foreach (var original in source)
        {
            var rank = ranks.Single(r => r.Id == original.Id);
            Assert.Equal(8 - original.Id, rank.Precedence);
            Assert.Equal(original.CommandCapacity, rank.CommandCapacity);
            Assert.Equal(original.PrestigeRequirement, rank.RequiredTrust);
            Assert.Equal(original.PromotionLoyaltyBonus, rank.LoyaltyBonus);
            Assert.Equal($"rank.{original.Id}", rank.NameKey);
        }
        Assert.Equal(20000u, ranks.Single(r => r.Id == 8).CommandCapacity);
    }
}
