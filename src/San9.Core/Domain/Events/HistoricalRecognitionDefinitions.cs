using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record HanzhongKingDefinition(
    [property: JsonRequired] int RulerId,
    [property: JsonRequired] int CityId,
    [property: JsonRequired] int RankId,
    [property: JsonRequired] int MinimumElapsedDays,
    [property: JsonRequired] int RequiredTrust,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public EventCandidate Candidate() => new("hanzhong-king", EventTrigger.Quarter, 0, EventConsumption.OnSuccess, ConsumptionKey);

    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> cities, IReadOnlySet<int> ranks)
    {
        _ = Candidate();
        if (!officers.Contains(RulerId) || !cities.Contains(CityId) || !ranks.Contains(RankId) ||
            MinimumElapsedDays < 0 || RequiredTrust is < 0 or > 1000 || BaseTrustBonus is < 0 or > 1000)
            throw new ArgumentException("Invalid Hanzhong king definition.");
    }
}

public sealed record FiveTigersDefinition(
    [property: JsonRequired] ImmutableArray<int> OfficerIds,
    [property: JsonRequired] int MinimumElapsedDays,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int LoyaltyBonus,
    [property: JsonRequired] ImmutableArray<int> GrantedTacticIds,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public EventCandidate Candidate() => new("five-tigers", EventTrigger.Quarter, 0, EventConsumption.OnSuccess, ConsumptionKey);

    public void Validate(IReadOnlySet<int> officers, IReadOnlySet<int> tactics)
    {
        _ = Candidate();
        if (OfficerIds.IsDefaultOrEmpty || OfficerIds.Distinct().Count() != OfficerIds.Length || OfficerIds.Any(id => !officers.Contains(id)) ||
            MinimumElapsedDays < 0 || BaseTrustBonus is < 0 or > 1000 || LoyaltyBonus is < 0 or > 255 ||
            GrantedTacticIds.IsDefault || GrantedTacticIds.Distinct().Count() != GrantedTacticIds.Length || GrantedTacticIds.Any(id => !tactics.Contains(id)))
            throw new ArgumentException("Invalid five tigers definition.");
    }
}

public sealed record ExpeditionMemorialDefinition(
    [property: JsonRequired] int OfficerId,
    [property: JsonRequired] int CountryTitleId,
    [property: JsonRequired] int ProvinceId,
    [property: JsonRequired] int OpposingCityId,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int LoyaltyBonus,
    [property: JsonRequired] ImmutableArray<int> GrantedTacticIds,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public EventCandidate Candidate() => new("expedition-memorial", EventTrigger.Quarter, 0, EventConsumption.OnSuccess, ConsumptionKey);

    public void Validate(IReadOnlySet<int> officers, IReadOnlyDictionary<int, int> cityProvinces,
        IReadOnlySet<int> cities, IReadOnlySet<int> titles, IReadOnlySet<int> tactics)
    {
        _ = Candidate();
        if (!officers.Contains(OfficerId) || !titles.Contains(CountryTitleId) || !cityProvinces.Values.Contains(ProvinceId) ||
            !cities.Contains(OpposingCityId) || BaseTrustBonus is < 0 or > 1000 || LoyaltyBonus is < 0 or > 255 ||
            GrantedTacticIds.IsDefault || GrantedTacticIds.Distinct().Count() != GrantedTacticIds.Length || GrantedTacticIds.Any(id => !tactics.Contains(id)))
            throw new ArgumentException("Invalid expedition memorial definition.");
    }
}
