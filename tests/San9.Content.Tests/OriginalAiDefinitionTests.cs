using System.Collections.Immutable;
using San9.Content.Scenarios;

namespace San9.Content.Tests;

public sealed class OriginalAiDefinitionTests
{
    [Fact]
    public void StandalonePolicyUsesAuthoredReferencesAndRejectsIncompleteOrInvalidRules()
    {
        var policy = OriginalAiDefinition.Default with { PreferredStrategyRulerIds = [9001],
            FamilyRankBonuses = ImmutableDictionary<int, int>.Empty.Add(7001, 200) };
        policy.Validate(new HashSet<int> { 9001 });
        Assert.Throws<InvalidDataException>(() => policy.Validate(new HashSet<int>()));
        Assert.Throws<InvalidDataException>(() => (policy with { WorkRules = policy.WorkRules.Remove(OriginalAiWork.Trade) }).Validate(new HashSet<int> { 9001 }));
        Assert.Throws<InvalidDataException>(() => (policy with { WorkRules = policy.WorkRules.SetItem(OriginalAiWork.Trade, new(0, 0, 0, false, true)) }).Validate(new HashSet<int> { 9001 }));
        Assert.Throws<InvalidDataException>(() => (policy with { SecondaryBatches = policy.SecondaryBatches.Remove(OriginalAiMode.TargetForce) }).Validate(new HashSet<int> { 9001 }));
    }
}
