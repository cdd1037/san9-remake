using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class OfficerStateTests
{
    [Fact]
    public void InjuryPrecedesIndependentItemAndOfficeBonuses()
    {
        var officer = new OfficerState(1, 5, 15, new(99, 80, 70, 60), false)
        {
            Injury = 3, ItemBonuses = new(10, 5, 0, 2), OfficeBonuses = new(0, 3, 7, 4)
        };
        Assert.True(officer.IsValid());
        Assert.Equal(new OfficerAbilities(79, 64, 56, 48), officer.Abilities);
        Assert.Equal(new OfficerAbilities(11, 9, 8, 7), (officer with { Injury = 14 }).Abilities);
        Assert.Equal(new OfficerAbilities(109, 88, 77, 66), (officer with { Injury = 0 }).Abilities);
    }

    [Fact]
    public void InvalidInputsAndOverflowAreRejected()
    {
        var officer = new OfficerState(1, 5, 15, new(99, 80, 70, 60), false);
        Assert.False((officer with { Injury = 15 }).IsValid());
        Assert.False((officer with { ItemBonuses = new(-1, 0, 0, 0) }).IsValid());
        Assert.False((officer with { OfficeBonuses = new(int.MaxValue, 0, 0, 0) }).IsValid());
        Assert.False((officer with { SettlementId = -1 }).IsValid());
    }
}
