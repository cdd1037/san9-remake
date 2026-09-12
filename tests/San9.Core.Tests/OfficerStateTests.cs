using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class OfficerStateTests
{
    [Fact]
    public void InjuryPrecedesIndependentItemAndOfficeBonuses()
    {
        var officer = new OfficerState(1, 5, 15, new(99, 80, 70, 60), false)
        {
            Injury = 3, OfficeId = 1
        };
        var rules = new OfficerAbilityRules(new([new(10, new(10, 5, 0, 2), 0)]),
            new([new(1, 10000, 0) { AbilityBonuses = new(0, 3, 7, 4) }]));
        TreasureState[] holdings = [new(10, 1, TreasureDiscovery.Discovered, null)];
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
