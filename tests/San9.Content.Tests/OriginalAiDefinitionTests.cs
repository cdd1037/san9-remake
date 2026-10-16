using System.Collections.Immutable;
using San9.Content.Scenarios;
using San9.Core.Domain;

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

    [Fact]
    public void MilitaryPolicyValidatesSemanticReferencesAndTravelRoles()
    {
        var military = new MilitaryDefinition(1, 1, [0], [new(0, 1, 1)],
            [new(7001, 1, 0, 10000, 5, new(1, 1), TravelMode.Land),
             new(8001, 1, 0, 10000, 5, new(1, 1), TravelMode.Water)], []);
        var policy = new OriginalAiMilitaryDefinition { Formations = ImmutableDictionary<OriginalAiFormationRole, int>.Empty
            .Add(OriginalAiFormationRole.March, 7001), NavalPriority = [8001], AggressiveRulerIds = [9001] };
        policy.Validate(new HashSet<int> { 9001 }, military);
        Assert.Throws<InvalidDataException>(() => policy.Validate(new HashSet<int>(), military));
        Assert.Throws<InvalidDataException>(() => (policy with { NavalPriority = [7001] }).Validate(new HashSet<int> { 9001 }, military));
        Assert.Throws<InvalidDataException>(() => (policy with { OrdinaryFacilityIds = [123] }).Validate(new HashSet<int> { 9001 }, military));
        Assert.Throws<InvalidDataException>(() => (policy with { HarborReliefTacticId = 123 }).Validate(new HashSet<int> { 9001 }, military));
    }
}
