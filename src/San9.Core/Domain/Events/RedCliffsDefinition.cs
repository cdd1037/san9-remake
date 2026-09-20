using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record RedCliffsDefinition(
    [property: JsonRequired] int OfficerId,
    [property: JsonRequired] int OpposingCityId,
    [property: JsonRequired] ImmutableArray<int> ExcludedRulerIds,
    [property: JsonRequired] int OfficerRegionId,
    [property: JsonRequired] ImmutableArray<int> RegionIds,
    [property: JsonRequired] int RequiredRelationExclusive,
    [property: JsonRequired] int MinimumFriendlyWaterUnits,
    [property: JsonRequired] long MinimumEnemySoldiers,
    [property: JsonRequired] int SoldierLossPercent,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int EnemyBaseTrustPenalty,
    [property: JsonRequired] int LoyaltyBonus,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> cities, IReadOnlySet<int> regions,
        bool hasRegionMap)
    {
        if (!officers.Contains(OfficerId) || !cities.Contains(OpposingCityId) || !regions.Contains(OfficerRegionId) ||
            ExcludedRulerIds.IsDefault || ExcludedRulerIds.Any(id => !officers.Contains(id)) ||
            ExcludedRulerIds.Distinct().Count() != ExcludedRulerIds.Length || RegionIds.IsDefaultOrEmpty ||
            RegionIds.Any(id => !regions.Contains(id)) || RegionIds.Distinct().Count() != RegionIds.Length || !hasRegionMap ||
            RequiredRelationExclusive is < 0 or > 101 || MinimumFriendlyWaterUnits < 1 || MinimumEnemySoldiers < 0 ||
            SoldierLossPercent is < 0 or > 100 || BaseTrustBonus < 0 || EnemyBaseTrustPenalty < 0 || LoyaltyBonus < 0 ||
            string.IsNullOrWhiteSpace(ConsumptionKey.Value)) throw new ArgumentException("Invalid Red Cliffs definition.");
    }
}
