using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class PrisonerCompensationTests
{
    private static TreasureRulebook Treasures() => new([
        new(9, default, 20), new(10, default, 5),
        new(11, new(0, 100, 0, 0), 1) { RetentionGroup = TreasureRetentionGroup.Intelligence }]);

    private static WorldState World(uint money = 10000, int relation = 0, bool exchange = false) => WorldState.Create(
        new(190, 1, 1), 42,
        [new ForceState(1, money, 0, 100) { LeaderId = 1 }, new ForceState(2, 1000, 0, 100) { LeaderId = 2 }],
        [new(1, 1, 0, 100, 100, 0, 0, 0, 0, 0, 0), new(2, 2, 0, 100, 100, 0, 0, 0, 0, 0, 0)],
        [new OfficerState(1, 1, 1, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new OfficerState(2, 2, 2, new(50, 50, 50, 50), false) { Status = OfficerStatus.Ruler },
         new(3, 1, 1, new(50, 50, 50, 50), false),
         new OfficerState(4, 1, 2, new(100, 100, 100, 100), false) { Status = OfficerStatus.Captive },
         new OfficerState(5, 2, exchange ? 1 : 2, new(100, 100, 1, 1), false) { Status = exchange ? OfficerStatus.Captive : OfficerStatus.Serving },
         new OfficerState(6, 2, exchange ? 1 : 2, new(60, 60, 60, 60), false) { Status = exchange ? OfficerStatus.Captive : OfficerStatus.Serving }],
        treasures: [new(9, 1, TreasureDiscovery.Discovered, null), new(10, 1, TreasureDiscovery.Discovered, null),
            new(11, 5, TreasureDiscovery.Discovered, null)], relations: [new(1, 2, relation)]);

    private static WorldMutation Mutation(WorldState world) => world.BeginMutation(new(Treasures(), new([])));
    private static CompensationOffer Quote(WorldMutation world) => PrisonerCompensation.Quote(world, 1, 3, 2, 4, Treasures());

    [Fact]
    public void ExchangePrecedesFreeAndUsesCurrentAbilityTotalIncludingTreasureAndInjury()
    {
        var state = Mutation(World(relation: 100, exchange: true));
        Assert.Equal(new(CompensationKind.Prisoner, 0, 5, null), Quote(state));
        state.Replace(state.Officers[5] with { Injury = 5 });
        Assert.Equal(new(CompensationKind.Prisoner, 0, 6, null), Quote(state));
        state.Replace(state.Officers[6] with { BaseAbilities = new(50, 50, 50, 51) });
        Assert.Equal(new(CompensationKind.Prisoner, 0, 5, null), Quote(state));
    }

    [Theory]
    [InlineData(79, CompensationKind.Money)]
    [InlineData(80, CompensationKind.Free)]
    [InlineData(100, CompensationKind.Free)]
    public void FreeReturnRequiresTopRelationBandAndNoExchange(int relation, CompensationKind expected) =>
        Assert.Equal(expected, Quote(Mutation(World(relation: relation))).Kind);

    [Fact]
    public void MoneyThenTreasureThenRefusalUseCurrentBalancesWithoutMutatingWorldOrRandom()
    {
        var state = Mutation(World());
        var simulation = state.Simulation;
        var forces = state.Forces.Values.ToArray();
        Assert.Equal(new(CompensationKind.Money, 2100, null, null), Quote(state));
        Assert.Equal(simulation, state.Simulation);
        Assert.Equal(forces, state.Forces.Values);
        state.Replace(state.Forces[1] with { Money = 2100 });
        Assert.Equal(new(CompensationKind.Treasure, 0, null, 10), Quote(state));
        state.Replace(state.Treasures[10] with { HolderId = 2 });
        Assert.Equal(CompensationKind.Refuse, Quote(state).Kind);
        state.Replace(state.Forces[1] with { Money = 2101 });
        Assert.Equal(CompensationKind.Money, Quote(state).Kind);
    }

    [Fact]
    public void ExchangeIncludesFieldPrisonersAndIgnoresFreedOrWrongAllegiancePeople()
    {
        var state = Mutation(World(exchange: true));
        state.AddUnit(new(20, 1, new(5, 5), 100));
        state.Replace(state.Officers[5] with { SettlementId = null, UnitId = 20 });
        Assert.Equal(1, PrisonerCompensation.Captor(state, state.Officers[5]));
        Assert.Equal(5, PrisonerCompensation.ExchangeCandidate(state, 1, 2));
        state.Replace(state.Officers[5] with { Status = OfficerStatus.Serving, OwnerId = 1 });
        Assert.Equal(6, PrisonerCompensation.ExchangeCandidate(state, 1, 2));
        state.Replace(state.Officers[6] with { OwnerId = 1 });
        Assert.Null(PrisonerCompensation.ExchangeCandidate(state, 1, 2));
    }

    [Fact]
    public void InvalidRequestOrLostTargetProducesNoUsableQuote()
    {
        var state = Mutation(World());
        Assert.Equal(CompensationKind.Refuse, PrisonerCompensation.Quote(state, 1, 2, 2, 4, Treasures()).Kind);
        Assert.Equal(CompensationKind.Refuse, PrisonerCompensation.Quote(state, 1, 3, 2, 999, Treasures()).Kind);
        state.Replace(state.Officers[4] with { Status = OfficerStatus.Serving, SettlementId = 1 });
        Assert.Equal(CompensationKind.Refuse, Quote(state).Kind);
    }

    [Fact]
    public void PaymentRevalidationIsSeparateFromAutomaticWillingnessAndOfferSelection()
    {
        var state = Mutation(World(money: 500, exchange: true));
        Assert.True(PrisonerCompensation.Payable(state, 1, 2, new(CompensationKind.Money, 500, null, null), Treasures()));
        Assert.False(DiplomacyMath.AcceptPrisonerRansom(500, 500, 500, 0));
        Assert.False(PrisonerCompensation.Payable(state, 1, 2, new(CompensationKind.Money, 501, null, null), Treasures()));
        var prisoner = new CompensationOffer(CompensationKind.Prisoner, 0, 5, null);
        Assert.True(PrisonerCompensation.Payable(state, 1, 2, prisoner, Treasures()));
        state.Replace(state.Officers[5] with { Status = OfficerStatus.Serving, SettlementId = 2 });
        Assert.False(PrisonerCompensation.Payable(state, 1, 2, prisoner, Treasures()));
        Assert.True(PrisonerCompensation.Payable(state, 1, 2, new(CompensationKind.Treasure, 0, null, 9), Treasures()));
        state.Replace(state.Treasures[9] with { HolderId = 3 });
        Assert.False(PrisonerCompensation.Payable(state, 1, 2, new(CompensationKind.Treasure, 0, null, 9), Treasures()));
        Assert.False(PrisonerCompensation.Payable(state, 1, 2, new(CompensationKind.Refuse, 0, null, null), Treasures()));
    }

    [Fact]
    public void OffersRejectMixedPayloadsAndUnknownKinds()
    {
        CompensationOffer[] invalid = [new(CompensationKind.Free, 1, null, null), new(CompensationKind.Prisoner, 1, 5, null),
            new(CompensationKind.Prisoner, 0, null, null), new(CompensationKind.Money, 0, null, null),
            new(CompensationKind.Money, 1000001, null, null), new(CompensationKind.Treasure, 0, 5, 9),
            new(CompensationKind.Treasure, 0, null, -1), new((CompensationKind)99, 0, null, null)];
        Assert.All(invalid, offer => Assert.False(offer.IsValid));
    }
}
