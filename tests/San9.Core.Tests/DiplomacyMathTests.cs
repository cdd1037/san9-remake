using San9.Core.Domain;
using San9.Core.Simulation;

namespace San9.Core.Tests;

public sealed class DiplomacyMathTests
{
    [Theory]
    [InlineData(0, ForceRelationBand.Hostile)]
    [InlineData(19, ForceRelationBand.Hostile)]
    [InlineData(20, ForceRelationBand.Unfriendly)]
    [InlineData(39, ForceRelationBand.Unfriendly)]
    [InlineData(40, ForceRelationBand.Neutral)]
    [InlineData(59, ForceRelationBand.Neutral)]
    [InlineData(60, ForceRelationBand.Friendly)]
    [InlineData(79, ForceRelationBand.Friendly)]
    [InlineData(80, ForceRelationBand.Intimate)]
    [InlineData(100, ForceRelationBand.Intimate)]
    public void AllFiveBandsHaveClosedBoundaries(int value, ForceRelationBand expected) =>
        Assert.Equal(expected, DiplomacyMath.RelationBand(value));

    [Fact]
    public void GiftUsesExecutedPoliticsAndPayloadSpecificValues()
    {
        Assert.Equal(7, DiplomacyMath.GiftMoneyThreshold(80, 59, 399));
        Assert.Equal(8, DiplomacyMath.GiftFoodThreshold(80, 61, 3999));
        Assert.Equal(24, DiplomacyMath.GiftTreasureThreshold(100, 60, 7));
        Assert.Equal(65, DiplomacyMath.FreeAttackRequestThreshold(85, 60));
    }

    [Fact]
    public void PrisonerCompensationUsesBestTwoAbilitiesAndIntegerAverage()
    {
        Assert.Equal(906u, DiplomacyMath.PrisonerCompensation(new(99, 100, 10, 20), 99, 94));
        Assert.Equal(500u, DiplomacyMath.PrisonerCompensation(new(80, 90, 10, 20), 100, 100));
        Assert.Equal(2500u, DiplomacyMath.PrisonerCompensation(new(100, 100, 10, 20), 0, 0));
    }

    [Theory]
    [InlineData(499u, 500u, false)]
    [InlineData(500u, 500u, false)]
    [InlineData(501u, 500u, true)]
    public void RansomQuotationRequiresMoreThanTheCompensation(uint money, uint compensation, bool expected) =>
        Assert.Equal(expected, DiplomacyMath.CanQuotePrisonerRansom(money, compensation));

    [Theory]
    [InlineData(499u, 500u, 1000u, 0L, true)]
    [InlineData(500u, 500u, 1000u, 0L, false)]
    [InlineData(500u, 500u, 1001u, 0L, false)]
    [InlineData(500u, 500u, 1002u, 0L, true)]
    [InlineData(501u, 500u, 10000u, 0L, false)]
    [InlineData(500u, 500u, 10000u, 9499L, true)]
    [InlineData(500u, 500u, 10000u, 9500L, false)]
    [InlineData(500u, 500u, 10000u, 10001L, false)]
    [InlineData(1u, 500u, 10000u, long.MaxValue, false)]
    [InlineData(0u, 500u, 10000u, 0L, false)]
    public void RansomAcceptanceSeparatesValuationHalfTreasuryAndReserveLimits(uint quote, uint compensation,
        uint money, long reserve, bool expected) =>
        Assert.Equal(expected, DiplomacyMath.AcceptPrisonerRansom(quote, compensation, money, reserve));

    [Theory]
    [InlineData(4, 500u, true)]
    [InlineData(5, 500u, false)]
    [InlineData(5, 599u, false)]
    [InlineData(5, 600u, true)]
    [InlineData(28, 10000u, true)]
    [InlineData(29, 10000u, false)]
    public void TreasureAcceptanceUsesStrictLoyaltyLimitAndIntegerCompensation(int bonus, uint compensation, bool expected) =>
        Assert.Equal(expected, DiplomacyMath.AcceptPrisonerTreasure(bonus, compensation));

    [Theory]
    [InlineData(1, 1, 9)]
    [InlineData(1, 9, 9)]
    [InlineData(1, 10, 8)]
    [InlineData(1, 11, 8)]
    [InlineData(2, 20, 4)]
    [InlineData(3, 21, 1)]
    [InlineData(3, 30, 0)]
    [InlineData(4, 1, 9)]
    [InlineData(12, 30, 0)]
    public void SalaryHorizonUsesCalendarTenDayBlocks(int month, int day, int expected) =>
        Assert.Equal(expected, DiplomacyMath.PrisonerSalaryReserveXuns(month, day));

    [Fact]
    public void InvalidNegotiationArithmeticInputsAreRejected()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.AcceptPrisonerRansom(1, 500, 1000, -1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.AcceptPrisonerTreasure(-1, 500));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.PrisonerSalaryReserveXuns(0, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.PrisonerSalaryReserveXuns(13, 1));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.PrisonerSalaryReserveXuns(1, 0));
        Assert.Throws<ArgumentOutOfRangeException>(() => DiplomacyMath.PrisonerSalaryReserveXuns(1, 31));
    }

    [Fact]
    public void AcceptedAndRejectedRelationsRespectTheirDifferentFloors()
    {
        Assert.Equal(60, DiplomacyMath.GiftAcceptedRelation(0));
        Assert.Equal(79, DiplomacyMath.GiftAcceptedRelation(59));
        Assert.Equal(100, DiplomacyMath.GiftAcceptedRelation(99));
        Assert.Equal(40, DiplomacyMath.AttackRequestAcceptedRelation(0));
        Assert.Equal(49, DiplomacyMath.AttackRequestAcceptedRelation(39));
        Assert.Equal(29, DiplomacyMath.SurrenderRejectedRelation(39));
        Assert.Equal(39, DiplomacyMath.SurrenderRejectedRelation(100));
        Assert.Equal(0, DiplomacyMath.SurrenderRejectedRelation(5));
        Assert.Equal(60, DiplomacyMath.AllianceMemberRelation(0));
        Assert.Equal(100, DiplomacyMath.AllianceMemberRelation(90));
        Assert.Equal(0, DiplomacyMath.AllianceTargetRelation(10));
        Assert.Equal(35, DiplomacyMath.AllianceTargetRelation(100));
    }

    [Fact]
    public void OnePairOwnsBothDirectionsAndEmitsOnlyActualChanges()
    {
        var world = WorldState.Create(new(190, 1, 1), 1, [new(3, 0), new(900, 0)], [], [],
            relations: [new(3, 900, 99)], corps: [new San9.Core.Domain.CorpsState(3, 3, null, new(0, 0)) { IsRoot = true }, new San9.Core.Domain.CorpsState(900, 900, null, new(0, 0)) { IsRoot = true }]);
        var mutation = world.BeginMutation();
        List<DomainEvent> events = [];
        ForceRelations.Change(mutation, 900, 3, 15, events);
        Assert.Equal(100, ForceRelations.Get(mutation, 3, 900).Value);
        ForceRelations.Change(mutation, 3, 900, 1, events);
        Assert.Single(events);
        ForceRelations.Change(mutation, 900, 3, int.MinValue, events);
        Assert.Equal(0, ForceRelations.Get(mutation, 900, 3).Value);
        Assert.Single(mutation.Relations);
        Assert.Throws<ArgumentOutOfRangeException>(() => ForceRelations.Set(mutation, 3, 900, 101, events));
        Assert.Throws<ArgumentException>(() => ForceRelations.Get(mutation, 3, 3));
    }
}
