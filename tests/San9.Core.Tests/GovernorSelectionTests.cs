using San9.Core.Domain;

namespace San9.Core.Tests;

public sealed class GovernorSelectionTests
{
    private static OfficerState Person(int id, int leadership = 50, int? office = null) =>
        new(id, 1, 1, new(50, 50, 50, leadership), false) { OfficeId = office };

    [Fact]
    public void ExistingGovernorOutranksHigherOfficeAndAbilityWhileRulerIsExcluded()
    {
        var rules = new GovernanceRulebook([new(500, 50000, 90)]);
        var serving = Person(1, 100, 500);
        var governor = Person(2, 10) with { Status = OfficerStatus.Governor };
        Assert.Equal(2, rules.SelectGovernor([serving, governor, Person(3) with { Status = OfficerStatus.Ruler }])!.Id);
    }

    [Fact]
    public void OfficePrecedenceDoesNotDependOnIdentityOrCandidateEnumerationOrder()
    {
        var rules = new GovernanceRulebook([new(9000, 15000, 100), new(1, 15000, 20)]);
        Assert.Equal(2, rules.SelectGovernor([Person(1, 100, 1), Person(2, 10, 9000)])!.Id);
        Assert.Equal(2, rules.SelectGovernor([Person(2, 10, 9000), Person(1, 100, 1)])!.Id);
    }

    [Fact]
    public void CivilOfficeDoesNotGainMilitaryPriorityAndNoOfficeUsesTenThousandCapacity()
    {
        var rules = new GovernanceRulebook([new(1, 10000, 999), new(2, 9000, 9999)]);
        Assert.Equal(2, rules.SelectGovernor([Person(1, 20, 1), Person(2, 80), Person(3, 100, 2)])!.Id);
    }

    [Fact]
    public void SelectionUsesCurrentAbilityAndStableIdentityTieBreak()
    {
        var rules = new GovernanceRulebook([]);
        var injured = Person(1, 100) with { Injury = 5 };
        var equipped = Person(2, 40) with { ItemBonuses = new(0, 0, 0, 20) };
        Assert.Equal(2, rules.SelectGovernor([injured, equipped])!.Id);
        Assert.Equal(3, rules.SelectGovernor([Person(9), Person(3)])!.Id);
        Assert.Null(rules.SelectGovernor([Person(1) with { Status = OfficerStatus.Captive }]));
    }
}
