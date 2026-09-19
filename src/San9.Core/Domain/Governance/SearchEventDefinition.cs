using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

[JsonPolymorphic(TypeDiscriminatorPropertyName = "kind")]
[JsonDerivedType(typeof(HealingSearchEventDefinition), "healing")]
[JsonDerivedType(typeof(BeastSearchEventDefinition), "beast")]
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
