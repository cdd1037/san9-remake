using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class TacticLinkRulesTests
{
    private static readonly OfficerState First = new(9001, null, 1, new(1, 1, 1, 1), false);
    private static readonly OfficerState Second = new(9002, null, 1, new(1, 1, 1, 1), false);
    private static readonly FormationTactics Ordinary = new(15, 2, [], []);

    [Fact]
    public void RelationshipIsSymmetricButPartnerTraitIsDirectional()
    {
        var rules = new TacticLinkRules(true, 20, [new(9001, 9002, 45, false)]);
        var partner = Second with { TacticLinkBonus = 10 };
        Assert.Equal(105, rules.Chance(First, partner, Ordinary));
        Assert.Equal(95, rules.Chance(partner, First, Ordinary));
        Assert.Equal(130, rules.Chance(First, partner, Ordinary with { LinkMultiplier = 3 }));
        Assert.Equal(60, rules.Chance(First, partner, Ordinary, historical: false));
        Assert.Equal(60, (rules with { RelationshipsEnabled = false }).Chance(First, partner, Ordinary));
    }

    [Theory]
    [InlineData(9100, 9100, false, 70)]
    [InlineData(9100, 9100, true, 95)]
    [InlineData(9002, null, true, 70)]
    [InlineData(null, 9001, true, 70)]
    [InlineData(null, null, false, 95)]
    public void LiveKinshipOverridesPairExceptExplicitSiblingExclusion(int? firstParent, int? secondParent, bool exclude, int expected)
    {
        var rules = new TacticLinkRules(true, 20, [new(9001, 9002, 45, exclude)]);
        var first = First with { ParentId = firstParent };
        var second = Second with { ParentId = secondParent };
        Assert.Equal(expected, rules.Chance(first, second, Ordinary));
        Assert.Equal(expected, rules.Chance(second, first, Ordinary));
    }

    [Theory]
    [InlineData(149, 0, 45)]
    [InlineData(0, 5, 25)]
    [InlineData(0, 75, 25)]
    public void AffinityWrapAndFloorRemainIndependentOfRelationships(int first, int second, int expected)
    {
        Assert.Equal(expected, TacticLinkRules.Empty.Chance(First with { Affinity = first }, Second with { Affinity = second }, Ordinary));
    }

    [Fact]
    public void DefinitionsRejectDuplicateReversedAndInvalidPairs()
    {
        var definition = new MilitaryDefinition(1, 1, [0], [new(0, 1, null)], [], []);
        foreach (var rules in new TacticLinkRules[] {
            new(true, -1, []), new(true, 20, [new(9002, 9001, 20, false)]),
            new(true, 20, [new(9001, 9001, 20, false)]), new(true, 20, [new(9001, 9002, 101, false)]),
            new(true, 20, [new(9001, 9002, 20, false), new(9001, 9002, 45, true)]) })
            Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { LinkRules = rules }));
        Assert.False((First with { TacticLinkBonus = -1 }).IsValid());
        var formation = new FormationRule(9000, 1, 0, 1000, 1, new(1, 1), TravelMode.Land);
        foreach (var categories in new int[][] { [-1], [9], [0, 0] })
            Assert.Throws<ArgumentException>(() => new MilitaryRulebook(definition with { Formations =
                [formation with { MartialTacticVulnerabilities = [.. categories] }] }));
    }
}
