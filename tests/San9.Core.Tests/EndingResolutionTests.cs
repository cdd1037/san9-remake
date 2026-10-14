using System.Collections.Immutable;
using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class EndingResolutionTests
{
    private static readonly EndingOfficeholders Court = new(1, 2, 3, 4);
    private static EndingSnapshot Victory() => new(0,
        Enumerable.Range(0, 5).Select(id => new EndingOfficer(id, new(90, 80, 80, 80), 0, id == 0 ? 7 : null, 400, 200, 20)).ToImmutableArray(),
        300000, 300000, 750000, 950, false, 2000, false, 2500, 1500, false, 4000, 15);

    [Theory]
    [InlineData(EndingPolicy.ContinentalExpansion, EndingOutcome.GreatEmpire)]
    [InlineData(EndingPolicy.DomesticProsperity, EndingOutcome.CulturalPower)]
    public void SuccessfulPoliciesDoNotMutateVictory(EndingPolicy policy, EndingOutcome outcome)
    {
        var snapshot = Victory();
        string before = CanonicalJson.Text(snapshot);
        Assert.Equal(new(outcome, 4, null), EndingResolution.Resolve(snapshot, Court, policy));
        Assert.Equal(before, CanonicalJson.Text(snapshot));
    }

    public static TheoryData<EndingPolicy, string, int, EndingOutcome> Gates => new()
    {
        { EndingPolicy.ContinentalExpansion, "soldiers", 1, EndingOutcome.SouthernDynasty },
        { EndingPolicy.ContinentalExpansion, "military-leadership", 1, EndingOutcome.SouthernDynasty },
        { EndingPolicy.ContinentalExpansion, "expansion-order", 2, EndingOutcome.OrdinaryState },
        { EndingPolicy.ContinentalExpansion, "protector-war", 2, EndingOutcome.OrdinaryState },
        { EndingPolicy.ContinentalExpansion, "commerce", 3, EndingOutcome.CommercialPower },
        { EndingPolicy.ContinentalExpansion, "naval", 3, EndingOutcome.CommercialPower },
        { EndingPolicy.ContinentalExpansion, "learned", 4, EndingOutcome.CommercialPower },
        { EndingPolicy.DomesticProsperity, "durability", 1, EndingOutcome.OrdinaryState },
        { EndingPolicy.DomesticProsperity, "knowledge", 1, EndingOutcome.OrdinaryState },
        { EndingPolicy.DomesticProsperity, "trust", 2, EndingOutcome.OrdinaryState },
        { EndingPolicy.DomesticProsperity, "ruler-affinity", 2, EndingOutcome.OrdinaryState },
        { EndingPolicy.DomesticProsperity, "money", 3, EndingOutcome.SouthernDynasty },
        { EndingPolicy.DomesticProsperity, "food", 3, EndingOutcome.SouthernDynasty },
        { EndingPolicy.DomesticProsperity, "domestic-leadership", 3, EndingOutcome.SouthernDynasty },
        { EndingPolicy.DomesticProsperity, "domestic-order", 4, EndingOutcome.CityState },
        { EndingPolicy.DomesticProsperity, "treasures", 4, EndingOutcome.CityState },
    };

    [Theory, MemberData(nameof(Gates))]
    public void EveryNumericGateCoversBelowEqualAbove(EndingPolicy policy, string gate, int stage, EndingOutcome failure)
    {
        for (int delta = -1; delta <= 1; delta++)
        {
            var snapshot = AtGate(Victory(), gate, delta);
            var result = EndingResolution.Resolve(snapshot, Court, policy);
            Assert.Equal(delta < 0 ? failure : policy == EndingPolicy.ContinentalExpansion ? EndingOutcome.GreatEmpire : EndingOutcome.CulturalPower, result.Outcome);
            Assert.Equal(delta < 0 ? stage : 4, result.DecisiveStage);
        }
    }

    private static EndingSnapshot Person(EndingSnapshot snapshot, int id, Func<EndingOfficer, EndingOfficer> change) =>
        snapshot with { Officers = snapshot.Officers.Select(o => o.Id == id ? change(o) : o).ToImmutableArray() };

    private static EndingSnapshot AtGate(EndingSnapshot s, string gate, int delta) => gate switch
    {
        "soldiers" => s with { SoldiersAndWounded = 300000 + delta },
        "military-leadership" => Person(s, 2, o => o with { Abilities = o.Abilities with { Leadership = 70 + delta } }),
        "expansion-order" => s with { ExpansionPublicOrder = 2000 + delta },
        "protector-war" => Person(s, 3, o => o with { Abilities = o.Abilities with { War = 90 + delta } }),
        "commerce" => s with { ExpansionCommerce = 2500 + delta },
        "naval" => Person(s, 1, o => o with { NavalProficiency = 400 + delta }),
        "learned" => Person(s, 1, o => o with { LearnedTacticCount = 20 + delta }),
        "durability" => s with { DomesticDurability = 1500 + delta },
        "knowledge" => Person(s, 1, o => o with { KnowledgeProficiency = 200 + delta }),
        "trust" => s with { Trust = 950 + delta },
        "ruler-affinity" => Person(s, 0, o => o with { Affinity = 15 - delta }),
        "money" => s with { Money = 300000 + delta },
        "food" => s with { Food = 750000 + delta },
        "domestic-leadership" => Person(s, 1, o => o with { Abilities = o.Abilities with { Leadership = 80 + delta } }),
        "domestic-order" => s with { DomesticPublicOrder = 4000 + delta },
        "treasures" => s with { CulturalTreasureCount = 15 + delta },
        _ => throw new ArgumentException(gate),
    };

    [Theory]
    [InlineData(EndingPolicy.ContinentalExpansion)]
    [InlineData(EndingPolicy.DomesticProsperity)]
    public void GatewaysReplaceOnlyThirdStageEconomicGate(EndingPolicy policy)
    {
        var snapshot = Victory() with { Money = 0, Food = 0, ExpansionCommerce = 0,
            OwnsExpansionGateways = true, OwnsDomesticGateways = true };
        Assert.Equal(4, EndingResolution.Resolve(snapshot, Court, policy).DecisiveStage);
        snapshot = snapshot with { SoldiersAndWounded = 0, DomesticDurability = 0 };
        Assert.Equal(1, EndingResolution.Resolve(snapshot, Court, policy).DecisiveStage);
    }

    [Theory]
    [InlineData(79, 71, 15, false, false)]
    [InlineData(80, 70, 15, false, false)]
    [InlineData(80, 71, 15, false, true)]
    [InlineData(81, 72, 14, false, true)]
    [InlineData(80, 71, 16, false, false)]
    [InlineData(80, 0, 5, true, true)]
    [InlineData(80, 0, 6, true, false)]
    [InlineData(80, 71, 5, true, true)]
    [InlineData(80, 0, 4, true, true)]
    public void SuccessionBranchesAreIndependent(int politics, int successorPolitics, int distance, bool relative, bool stable)
    {
        var s = Victory() with { ExpansionPublicOrder = 0 };
        s = Person(s, 1, o => o with { Abilities = o.Abilities with { Politics = politics } });
        s = Person(s, 4, o => o with { Abilities = o.Abilities with { Politics = successorPolitics },
            Affinity = 150 - distance, BloodlineId = relative ? 7 : null });
        Assert.Equal(new(stable ? EndingOutcome.OrdinaryState : EndingOutcome.DividedState, 2, stable),
            EndingResolution.Resolve(s, Court, EndingPolicy.ContinentalExpansion));
    }

    [Fact]
    public void FailureShortCircuitsAndFourthStageDoesNotCheckSuccession()
    {
        var s = Person(Victory(), 1, o => o with { Abilities = o.Abilities with { Politics = 0 } });
        Assert.Equal(new(EndingOutcome.FallenState, 1, false), EndingResolution.Resolve(s with { SoldiersAndWounded = 0, ExpansionPublicOrder = 0 }, Court, EndingPolicy.ContinentalExpansion));
        Assert.Equal(new(EndingOutcome.DividedState, 3, false), EndingResolution.Resolve(s with { ExpansionCommerce = 0 }, Court, EndingPolicy.ContinentalExpansion));
        Assert.Equal(new(EndingOutcome.FallenState, 3, false), EndingResolution.Resolve(s with { Money = 0 }, Court, EndingPolicy.DomesticProsperity));
        Assert.Equal(new(EndingOutcome.CommercialPower, 4, null), EndingResolution.Resolve(s with { HasLivingBarbarianForce = true }, Court, EndingPolicy.ContinentalExpansion));
        Assert.Equal(new(EndingOutcome.CulturalPower, 4, null), EndingResolution.Resolve(s, Court, EndingPolicy.DomesticProsperity));
    }

    [Fact]
    public void InvalidCourtAndSnapshotAreRejectedAndSmallCourtDoesNotInventOfficers()
    {
        var s = Victory();
        foreach (var court in new[] { new EndingOfficeholders(1, 1, 3, 4), new(0, 2, 3, 4), new(1, 2, 3, 99) })
            Assert.Throws<ArgumentException>(() => EndingResolution.Resolve(s, court, EndingPolicy.ContinentalExpansion));
        Assert.Throws<ArgumentException>(() => EndingResolution.Resolve(s with { Officers = s.Officers.Add(s.Officers[0]) }, Court, EndingPolicy.ContinentalExpansion));
        Assert.Throws<ArgumentException>(() => EndingResolution.Resolve(s with { Money = -1 }, Court, EndingPolicy.ContinentalExpansion));
        Assert.Throws<ArgumentOutOfRangeException>(() => EndingResolution.Resolve(s, Court, (EndingPolicy)99));
        s = s with { Officers = s.Officers.Take(4).ToImmutableArray() };
        foreach (var policy in Enum.GetValues<EndingPolicy>())
            Assert.Equal(new(EndingOutcome.FallenState, 1, false), EndingResolution.Resolve(s, null, policy));
    }
}
