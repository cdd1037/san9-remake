using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public sealed record QiaoMarriageDefinition(
    [property: JsonRequired] int RulerId,
    [property: JsonRequired] int CompanionId,
    [property: JsonRequired] ImmutableArray<int> RequiredLivingOfficerIds,
    [property: JsonRequired] int RivalRulerId,
    [property: JsonRequired] int ProvinceId,
    [property: JsonRequired] ImmutableArray<int> ExcludedCityIds,
    [property: JsonRequired] GameClock LatestScenarioStart,
    [property: JsonRequired] GameClock LatestDate,
    [property: JsonRequired] int LifespanBonus,
    [property: JsonRequired] int LifespanCap,
    [property: JsonRequired] int BaseTrustBonus,
    [property: JsonRequired] int RelationPenalty,
    [property: JsonRequired] PersistentEventKey ConsumptionKey)
{
    public EventCandidate Candidate() => new("qiao-marriage", EventTrigger.Quarter, 0, EventConsumption.OnSuccess, ConsumptionKey);

    public void Validate(IReadOnlySet<int> officers, IReadOnlyDictionary<int, int> cityProvinces)
    {
        _ = Candidate();
        _ = new GameClock(LatestScenarioStart.Year, LatestScenarioStart.Month, LatestScenarioStart.Day);
        _ = new GameClock(LatestDate.Year, LatestDate.Month, LatestDate.Day);
        if (!officers.Contains(RulerId) || !officers.Contains(CompanionId) || RulerId == CompanionId ||
            !officers.Contains(RivalRulerId) || RequiredLivingOfficerIds.IsDefaultOrEmpty ||
            RequiredLivingOfficerIds.Any(id => !officers.Contains(id)) ||
            RequiredLivingOfficerIds.Distinct().Count() != RequiredLivingOfficerIds.Length ||
            ExcludedCityIds.IsDefault || ExcludedCityIds.Any(id => !cityProvinces.ContainsKey(id)) ||
            ExcludedCityIds.Distinct().Count() != ExcludedCityIds.Length ||
            !cityProvinces.Any(p => p.Value == ProvinceId && !ExcludedCityIds.Contains(p.Key)) ||
            LifespanBonus < 0 || LifespanCap < 0 || BaseTrustBonus < 0 || RelationPenalty is < 0 or > 100)
            throw new ArgumentException("Invalid Qiao marriage event definition.");
    }
}
