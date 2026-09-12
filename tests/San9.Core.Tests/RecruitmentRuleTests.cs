using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class RecruitmentRuleTests
{
    private static WorldState World(ulong seed = 0) => WorldState.Create(new(190, 1, 1), seed,
        [new(1, 1000, 1000, 100) { LeaderId = 1 }, new(2, 1000, 1000, 100) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 100, 0, 100), new(2, 2, 0, 100, 100, 0, 0, 0, 100, 0, 100)],
        [new(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler, Affinity = 50 },
         new(10, 1, 1, new(50, 50, 100, 50), false) { Affinity = 50 },
         new(20, 2, 2, new(50, 50, 50, 50), false) { Righteousness = 5, ForceLoyalties = ImmutableDictionary<int, int>.Empty.Add(1, 50).Add(2, 90) }],
        relations: [new(1, 2, 80)]);

    [Theory]
    [InlineData(49, 90, 0)]
    [InlineData(50, 99, 19)]
    [InlineData(50, 100, 0)]
    [InlineData(50, 90, 28)]
    public void ThresholdUsesExactLoyaltyGatesAndLeaderAffinity(int incoming, int current, int expected)
    {
        var state = World().BeginMutation();
        state.Replace(state.Officers[20].WithLoyalty(1, incoming).WithLoyalty(2, current));
        Assert.Equal(expected, RecruitmentRules.Threshold(state, 10, 20));
    }

    [Fact]
    public void NamedPoliticsBonusUsesBasePoliticsAndSignedDivisionTruncatesTowardZero()
    {
        var state = World().BeginMutation();
        state.Replace(state.Officers[10] with { BaseAbilities = new(50, 50, 60, 50), Injury = 5, HasRecruitmentPoliticsBonus = true });
        state.Replace(state.Forces[1] with { Trust = 96 });
        Assert.Equal(-2, RecruitmentRules.Threshold(state, 10, 20));
        state.Replace(state.Forces[1] with { Trust = 95 });
        Assert.Equal(-3, RecruitmentRules.Threshold(state, 10, 20));
    }

    [Fact]
    public void FailureFeedbackCoversAllIndependentGatesWithSeparateForceWrites()
    {
        HashSet<(bool Increase, bool Decrease)> combinations = [];
        for (ulong seed = 0; seed < 100 && combinations.Count < 4; seed++)
        {
            var state = World(seed).BeginMutation();
            var expected = new StableRandom(StableRandom.DeriveSeed(seed, "personnel-recruit-feedback"));
            bool increase = expected.NextUInt64() % 100 < 75;
            bool decrease = expected.NextUInt64() % 100 < 50;
            combinations.Add((increase, decrease));
            RecruitmentRules.Failure(state, 1, 20, 50, []);
            Assert.Equal(increase ? 51 : 50, state.Officers[20].LoyaltyTo(1));
            Assert.Equal(decrease ? 89 : 90, state.Officers[20].LoyaltyTo(2));
            Assert.Equal(expected.State, state.Simulation.RandomStreams.Single(s => s.Name == "personnel-recruit-feedback").State);
            Assert.Equal(2, state.Officers[20].OwnerId);
            Assert.Equal(2, state.Officers[20].SettlementId);
        }
        Assert.Equal(4, combinations.Count);
    }

    [Theory]
    [InlineData(OfficerStatus.Free, 5, 0)]
    [InlineData(OfficerStatus.Captive, 5, 0)]
    [InlineData(OfficerStatus.Serving, 15, 0)]
    [InlineData(OfficerStatus.Serving, 5, 50)]
    public void ExcludedDecreaseGatesConsumeOnlyOneFeedbackRoll(OfficerStatus status, int righteousness, int affinity)
    {
        var state = World().BeginMutation();
        state.Replace(state.Officers[20] with { Status = status, Righteousness = righteousness, Affinity = affinity });
        var random = new StableRandom(StableRandom.DeriveSeed(0, "personnel-recruit-feedback"));
        random.NextUInt64();
        RecruitmentRules.Failure(state, 1, 20, 100, []);
        Assert.Equal(90, state.Officers[20].LoyaltyTo(2));
        Assert.Equal(random.State, state.Simulation.RandomStreams.Single(s => s.Name == "personnel-recruit-feedback").State);
        if (status == OfficerStatus.Captive) Assert.DoesNotContain(state.Simulation.RandomStreams, s => s.Name == "personnel-recruit-relations");
    }
}
