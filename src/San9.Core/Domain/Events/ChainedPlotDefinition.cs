using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record ChainedPlotDefinition(
    [property: JsonRequired] int RulerId,
    [property: JsonRequired] int SuccessorId,
    [property: JsonRequired] int InstigatorId,
    [property: JsonRequired] GameClock EarliestDate,
    [property: JsonRequired] int LoyaltyThreshold,
    [property: JsonRequired] int TrustThreshold,
    [property: JsonRequired] ImmutableArray<int> SupporterIds,
    [property: JsonRequired] ImmutableArray<int> OpponentIds,
    [property: JsonRequired] int SupporterMinimumLoyalty,
    [property: JsonRequired] int OpponentMaximumLoyalty,
    [property: JsonRequired] int AffinityDivisor,
    [property: JsonRequired] PersistentEventKey StartedKey,
    [property: JsonRequired] PersistentEventKey CompletedKey)
{
    public void Validate(IReadOnlySet<int> officers)
    {
        _ = new GameClock(EarliestDate.Year, EarliestDate.Month, EarliestDate.Day);
        if (SupporterIds.IsDefault || OpponentIds.IsDefault ||
            new[] { RulerId, SuccessorId, InstigatorId }.Distinct().Count() != 3 ||
            new[] { RulerId, SuccessorId, InstigatorId }.Concat(SupporterIds).Concat(OpponentIds).Any(id => !officers.Contains(id)) ||
            SupporterIds.Distinct().Count() != SupporterIds.Length || OpponentIds.Distinct().Count() != OpponentIds.Length ||
            LoyaltyThreshold is < 0 or > 255 || TrustThreshold is < 0 or > 1000 ||
            SupporterMinimumLoyalty is < 0 or > 255 || OpponentMaximumLoyalty is < 0 or > 255 || AffinityDivisor <= 0 ||
            string.IsNullOrWhiteSpace(StartedKey.Value) || string.IsNullOrWhiteSpace(CompletedKey.Value) || StartedKey == CompletedKey)
            throw new ArgumentException("Invalid chained plot definition.");
    }
}
