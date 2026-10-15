using System.Collections.Immutable;
using San9.Application.AI;
using San9.Core.Domain;

namespace San9.Application.Tests;

public sealed class CampaignEquipmentTests
{
    private static readonly FormationRule Formation = new(7, 10, 10, 20000, 5, new(1, 1), TravelMode.Land)
    { Tactics = new(100, 0, 2, [0], []) };
    private static readonly TacticRule Martial = new(10, 0, TacticEffect.Martial, 10, 3, 0, true, true, [7]);
    private static OfficerState Person(int id, int affinity = 0) => new(id, 2, 2, new(80, 60, 20, 50), false)
    { Affinity = affinity, LearnedTactics = [10], TacticProficiencies = [500, 0, 0, 0, 0, 0, 0, 0, 0] };

    private static CampaignTacticEstimate Estimate(ImmutableArray<OfficerState> people, TacticRule? tactic = null,
        BattleRank rank = BattleRank.Front) => CampaignEquipmentSearch.Estimate(people,
            people.ToImmutableDictionary(p => p.Id, p => p.BaseAbilities),
            people.ToImmutableDictionary(p => p.Id, _ => ImmutableArray.Create(tactic ?? Martial)),
            Formation, TacticLinkRules.Empty,
            people.ToImmutableDictionary(p => p.Id, _ => (int?)10), people.ToImmutableDictionary(p => p.Id, _ => rank));

    [Fact]
    public void MartialEstimateUsesProficiencyCooldownAffinityAndAvailability()
    {
        var person = Person(1);
        long baseline = Estimate([person]).Damage;
        Assert.True(Estimate([person with { TacticProficiencies = [1000, 0, 0, 0, 0, 0, 0, 0, 0] }]).Damage > baseline);
        Assert.True(Estimate([person], Martial with { MinimumInterval = 10 }).Damage < baseline);
        Assert.Equal(0, Estimate([person with { Injury = 1 }]).Damage);
        Assert.True(Estimate([person, Person(2)]).Damage > Estimate([person, Person(2, 50)]).Damage);
        Assert.True(Estimate([person], rank: BattleRank.Front).OfficerExposure > Estimate([person], rank: BattleRank.Rear).OfficerExposure);
    }

    [Fact]
    public void NoOffensiveContributionKeepsPeopleInTheSaferRank()
    {
        ImmutableArray<OfficerState> people = [Person(1), Person(2)];
        var search = new CampaignEquipmentSearch(people, people.ToImmutableDictionary(p => p.Id, p => p.BaseAbilities),
            people.ToImmutableDictionary(p => p.Id, _ => ImmutableArray<TacticRule>.Empty),
            Formation, TacticLinkRules.Empty, 10000, 5000);
        foreach (var step in search.Steps()) step();
        Assert.All(search.Best.Ranks.Values, rank => Assert.Equal(BattleRank.Rear, rank));
        Assert.Equal(0, search.Best.Estimate.Damage);
    }

    [Fact]
    public void AutomaticTacticContributesWithoutBecomingAnUnlearnedEquipmentChoice()
    {
        ImmutableArray<OfficerState> people = [Person(1) with { LearnedTactics = [] }];
        var search = new CampaignEquipmentSearch(people, people.ToImmutableDictionary(p => p.Id, p => p.BaseAbilities),
            people.ToImmutableDictionary(p => p.Id, _ => ImmutableArray<TacticRule>.Empty),
            Formation with { Tactics = Formation.Tactics! with { AutomaticTactics = [10] } },
            TacticLinkRules.Empty, 10000, 5000, [Martial]);
        foreach (var step in search.Steps()) step();
        Assert.Null(search.Best.Tactics[1]);
        Assert.True(search.Best.Estimate.Damage > 0);
    }
}
