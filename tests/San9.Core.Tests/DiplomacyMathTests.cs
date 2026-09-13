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
        var world = WorldState.Create(new(190, 1, 1), 1, [new(3, 0, 0, 0), new(900, 0, 0, 0)], [], [],
            relations: [new(3, 900, 99)]);
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
