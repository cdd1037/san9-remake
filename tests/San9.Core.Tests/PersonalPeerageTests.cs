using San9.Core.Domain;
using System.Collections.Immutable;

namespace San9.Core.Tests;

public sealed class PersonalPeerageTests
{
    [Fact]
    public void PeriodPoliciesRejectPartialGradeSetsAndSelectYearSpecificRules()
    {
        var allowed = ImmutableDictionary<PersonalPeerageGrade, ImmutableArray<int>>.Empty;
        var grades = PersonalPeeragePolicy.Default.Grades.Values.OrderBy(r => r.Grade).ToImmutableArray();
        var early = new PersonalPeeragePeriod(180, 219, allowed, []) { GradeRules = grades };
        var late = new PersonalPeeragePeriod(220, 280, allowed, [])
        { GradeRules = grades.Select(r => r.Grade == PersonalPeerageGrade.CountyMarquis ? r with { Limit = 2, Salary = 9 } : r).ToImmutableArray() };
        var regime = new PersonalPeerageRegime([early, late]);
        Assert.Equal(4, regime.Period(219)!.Policy(PersonalPeeragePolicy.Default).Grades[PersonalPeerageGrade.CountyMarquis].Limit);
        Assert.Equal(2, regime.Period(220)!.Policy(PersonalPeeragePolicy.Default).Grades[PersonalPeerageGrade.CountyMarquis].Limit);
        Assert.Throws<ArgumentException>(() => new PersonalPeerageRegime([early with { GradeRules = [grades[0]] }]));
        Assert.Throws<ArgumentException>(() => new PersonalPeerageRegime([early, late with { StartYear = 219 }]));
    }

    private static PersonalPeeragePolicy Policy() => new([
        new(PersonalPeerageGrade.VillageMarquis, 2, 4, 12),
        new(PersonalPeerageGrade.CountyMarquis, 6, 8, 4),
        new(PersonalPeerageGrade.King, 15, 12, 1)]);

    [Fact]
    public void PersonalTitleIsIndependentOfOfficeAndValidated()
    {
        var person = new OfficerState(1, 1, 1, new(50, 50, 50, 50), false)
        { OfficeId = 7, PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, "汉寿亭侯") };
        Assert.True(person.IsValid());
        Assert.Equal(7, person.OfficeId);
        Assert.False((person with { PersonalPeerage = new(PersonalPeerageGrade.VillageMarquis, " ") }).IsValid());
        Assert.Null((person with { PersonalPeerage = null }).PersonalPeerage);
    }

    [Fact]
    public void GrantRequiresAuthorityCapacityAndRoyalBlood()
    {
        var policy = Policy();
        Assert.False(policy.CanGrant(PersonalPeerageGrade.VillageMarquis, 0, false, false));
        Assert.False(policy.CanGrant(PersonalPeerageGrade.VillageMarquis, 12, true, false));
        Assert.False(policy.CanGrant(PersonalPeerageGrade.King, 0, true, false));
        Assert.True(policy.CanGrant(PersonalPeerageGrade.King, 0, true, true));
    }

    [Fact]
    public void LoyaltyRewardsOnlyFirstGrantAndPromotion()
    {
        var policy = Policy();
        Assert.Equal(4, policy.LoyaltyChange(null, PersonalPeerageGrade.VillageMarquis));
        Assert.Equal(8, policy.LoyaltyChange(PersonalPeerageGrade.VillageMarquis, PersonalPeerageGrade.CountyMarquis));
        Assert.Equal(0, policy.LoyaltyChange(PersonalPeerageGrade.CountyMarquis, PersonalPeerageGrade.VillageMarquis));
        Assert.Equal(0, policy.LoyaltyChange(PersonalPeerageGrade.King, PersonalPeerageGrade.King));
        Assert.Equal(-10, policy.LoyaltyChange(PersonalPeerageGrade.King, null));
    }
}
