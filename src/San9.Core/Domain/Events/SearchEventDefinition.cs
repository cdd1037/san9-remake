using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HealingSearchEventDefinition), "healing")]
[JsonDerivedType(typeof(BeastSearchEventDefinition), "beast")]
[JsonDerivedType(typeof(ScholarReturnSearchEventDefinition), "scholar-return")]
[JsonDerivedType(typeof(ImperialCeremonySearchEventDefinition), "imperial-ceremony")]
[JsonDerivedType(typeof(TreasureMonumentSearchEventDefinition), "treasure-monument")]
[JsonDerivedType(typeof(ForceActivationSearchEventDefinition), "force-activation")]
[JsonDerivedType(typeof(WomenVisitSearchEventDefinition), "women-visit")]
[JsonDerivedType(typeof(SageVisitSearchEventDefinition), "sage-visit")]
[JsonDerivedType(typeof(PhysicianVisitSearchEventDefinition), "physician-visit")]
[JsonDerivedType(typeof(DivinerVisitSearchEventDefinition), "diviner-visit")]
[JsonDerivedType(typeof(LongevitySearchEventDefinition), "longevity")]
[JsonDerivedType(typeof(ScholarInsultSearchEventDefinition), "scholar-insult")]
public abstract record SearchEventDefinition
{
    [JsonRequired] public string Id { get; init; } = "";
    [JsonRequired] public int Priority { get; init; }
    [JsonRequired] public EventConsumption Consumption { get; init; }
    public EventCandidate Candidate(PersistentEventKey? key) => new(Id, EventTrigger.Search, Priority, Consumption, key);
}

public sealed record HealingSearchEventDefinition(
    [property: JsonRequired] int HealerOfficerId,
    [property: JsonRequired] ImmutableArray<int> ExcludedSearchingOfficerIds,
    [property: JsonRequired] int WarChanceDivisor) : SearchEventDefinition
{
    [JsonRequired] public PersistentEventKey? ConsumptionKey { get; init; }
}

public sealed record BeastSearchSite(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] PersistentEventKey? ConsumptionKey,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds);

public sealed record BeastSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<BeastSearchSite> Sites,
    [property: JsonRequired] ImmutableArray<int> BattleTacticIds,
    [property: JsonRequired] int WarChanceDivisor,
    [property: JsonRequired] int WarReward,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] int DefeatInjury,
    [property: JsonRequired] bool ReduceLifespanOnDefeat) : SearchEventDefinition;

public sealed record ScholarReturnSearchEventDefinition(
    [property: JsonRequired] int ScholarOfficerId,
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] GameClock EarliestDate,
    [property: JsonRequired] GameClock LatestDate,
    [property: JsonRequired] int MaximumBaseAbilityExclusive,
    [property: JsonRequired] int PoliticsChanceDivisor,
    [property: JsonRequired] int MinimumTrustExclusive,
    [property: JsonRequired] int MaximumBaseTrustExclusive,
    [property: JsonRequired] uint MinimumMoneyExclusive,
    [property: JsonRequired] int IntelligenceReward,
    [property: JsonRequired] int FallbackPoliticsReward,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds,
    [property: JsonRequired] PersistentEventKey ConsumptionKey,
    [property: JsonRequired] PersistentEventKey SuccessHistoryKey) : SearchEventDefinition;

public sealed record ImperialCeremonySearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int RequiredRankId,
    [property: JsonRequired] int AbilityReward,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] int LoyaltyReward,
    [property: JsonRequired] PersistentEventKey ConsumptionKey) : SearchEventDefinition;

public sealed record TreasureMonumentSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int TreasureId,
    [property: JsonRequired] int MinimumRankId,
    [property: JsonRequired] uint MinimumMoneyExclusive,
    [property: JsonRequired] uint Cost,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] PersistentEventKey ConsumptionKey) : SearchEventDefinition;

public sealed record ForceActivationSearchEventDefinition(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int ForceId,
    [property: JsonRequired] int CityId,
    [property: JsonRequired] ImmutableArray<int> AttachedSettlementIds,
    [property: JsonRequired] ImmutableArray<int> OfficerIds,
    [property: JsonRequired] int MinimumTrust,
    [property: JsonRequired] int MinimumProvinces,
    [property: JsonRequired] uint CitySoldiers,
    [property: JsonRequired] int BaseTrustReward,
    [property: JsonRequired] PersistentEventKey ConsumptionKey) : SearchEventDefinition
{
    public void ValidateReferences(IReadOnlySet<int> forces, IReadOnlySet<int> cities, IReadOnlySet<int> settlements,
        IReadOnlySet<int> officers, bool hasRegions)
    {
        if (!forces.Contains(ForceId) || !cities.Contains(CityId) || AttachedSettlementIds.Any(id => !settlements.Contains(id)) ||
            OfficerIds.Any(id => !officers.Contains(id)) || !hasRegions)
            throw new ArgumentException("Unknown force activation reference or missing geographic regions.");
    }
}
