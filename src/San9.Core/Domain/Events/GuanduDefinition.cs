using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record GuanduDefinition(
    [property: JsonRequired] int PatronId,
    [property: JsonRequired] int OpponentId,
    [property: JsonRequired] int RecruitId,
    [property: JsonRequired] int RequiredPlaceId,
    [property: JsonRequired] ImmutableArray<int> PatronRegionIds,
    [property: JsonRequired] ImmutableArray<int> EnemyRegionIds,
    [property: JsonRequired] int LoyaltyExclusive,
    [property: JsonRequired] int RelationExclusive,
    [property: JsonRequired] long MinimumEnemySoldiers,
    [property: JsonRequired] int SoldierLossPercent,
    [property: JsonRequired] int FoodLossPercent,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int EnemyBaseTrustPenalty,
    [property: JsonRequired] int LoyaltyBonus,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> places, bool hasRegionMap)
    {
        int[] participants = [PatronId, OpponentId, RecruitId];
        if (participants.Distinct().Count() != 3 || participants.Any(id => !officers.Contains(id)) ||
            !places.Contains(RequiredPlaceId) || !ValidRegions(PatronRegionIds) || !ValidRegions(EnemyRegionIds) ||
            !hasRegionMap || LoyaltyExclusive is < 0 or > 256 || RelationExclusive is < 0 or > 101 ||
            MinimumEnemySoldiers < 0 || SoldierLossPercent is < 0 or > 100 || FoodLossPercent is < 0 or > 100 ||
            BaseTrustBonus < 0 || EnemyBaseTrustPenalty < 0 || LoyaltyBonus < 0 || string.IsNullOrWhiteSpace(ConsumptionKey.Value))
            throw new ArgumentException("Invalid Guandu definition.");
        bool ValidRegions(ImmutableArray<int> ids) => !ids.IsDefaultOrEmpty &&
            ids.Distinct().Count() == ids.Length && ids.All(places.Contains);
    }
}
