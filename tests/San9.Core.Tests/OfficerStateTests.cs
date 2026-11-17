using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class OfficerStateTests
{
    [Theory]
    [InlineData(0)]
    [InlineData(50)]
    [InlineData(63)]
    [InlineData(255)]
    public void LoyaltyOverridesUseTheWorldDefaultAndDisappearWhenReset(int baseline)
    {
        var person = new OfficerState(1, 5, 15, new(50, 50, 50, 50), false).WithLoyaltyDefault(baseline);
        Assert.Equal(baseline, person.Loyalty);
        Assert.Equal(baseline, person.LoyaltyTo(9000));
        int different = baseline == 0 ? 1 : 0;
        var changed = person.WithLoyalty(5, different);
        Assert.Equal(different, changed.Loyalty);
        Assert.Equal(different, changed.ForceLoyalties[5]);
        Assert.Empty(changed.WithLoyalty(5, baseline).ForceLoyalties);
        Assert.Empty(person.ForceLoyalties);
        Assert.Equal(0, (person with { OwnerId = null }).Loyalty);
    }

    [Fact]
    public void BindingPreservesExplicitFiftyAndCopiesRetainTheirBaseline()
    {
        var raw = new OfficerState(1, 5, 15, new(50, 50, 50, 50), false)
        {
            ForceLoyalties = System.Collections.Immutable.ImmutableDictionary<int, int>.Empty
                .Add(5, 50).Add(6, 63).Add(7, 0)
        };
        var bound = raw.WithLoyaltyDefault(63);
        Assert.Equal(50, bound.Loyalty);
        Assert.Equal(0, bound.LoyaltyTo(7));
        Assert.False(bound.ForceLoyalties.ContainsKey(6));
        Assert.True(raw.ForceLoyalties.ContainsKey(6));

        var copy = bound with { OwnerId = 6 };
        Assert.Equal(63, copy.Loyalty);
        var changed = copy.WithLoyalty(6, 80);
        Assert.Equal(80, changed.Loyalty);
        Assert.False(changed.WithLoyalty(6, 63).ForceLoyalties.ContainsKey(6));
        Assert.Equal(63, copy.Loyalty);

        var replaced = bound with { ForceLoyalties = bound.ForceLoyalties.Remove(5) };
        Assert.Equal(63, replaced.Loyalty);
        Assert.Equal(91, replaced.WithLoyaltyDefault(91).Loyalty);
        Assert.Equal(63, replaced.Loyalty);
    }

    [Fact]
    public void InjuryPrecedesIndependentItemAndOfficeBonuses()
    {
        var officer = new OfficerState(1, 5, 15, new(99, 80, 70, 60), false)
        {
            Injury = 3, OfficeId = 1
        };
        var rules = new OfficerAbilityRules(new([new(10, new(10, 5, 0, 2), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(0, 3, 7, 4) }]));
        TreasureState[] holdings = [new(10, new OfficerTreasurePlacement(1))];
        Assert.True(officer.IsValid());
        Assert.Equal(new OfficerAbilities(79, 64, 56, 48), rules.Evaluate(officer, holdings));
        Assert.Equal(new OfficerAbilities(11, 9, 8, 7), rules.Evaluate(officer with { Injury = 14 }, holdings));
        Assert.Equal(new OfficerAbilities(109, 88, 77, 66), rules.Evaluate(officer with { Injury = 0 }, holdings));
    }

    [Fact]
    public void InvalidSourceStateIsRejected()
    {
        var officer = new OfficerState(1, 5, 15, new(99, 80, 70, 60), false);
        Assert.False((officer with { Injury = 15 }).IsValid());
        Assert.False((officer with { BaseAbilities = new(-1, 0, 0, 0) }).IsValid());
        Assert.False((officer with { SettlementId = -1 }).IsValid());
    }
}
