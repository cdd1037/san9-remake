using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public enum ConflictChanceAbility { Leadership, War, Intelligence, Politics }

public abstract record ConflictSearchEventDefinition : SearchEventDefinition
{
    [JsonRequired] public ConflictChanceAbility ChanceAbility { get; init; }
    [JsonRequired] public int ChanceDivisor { get; init; }
    [JsonRequired] public bool RequiresOwnedPlace { get; init; }
    [JsonRequired] public int MinimumBaseTrustExclusive { get; init; } = -1;
    [JsonRequired] public int MaximumBaseTrustExclusive { get; init; } = 1001;
    [JsonRequired] public int MaximumInjuryExclusive { get; init; } = 15;
    [JsonRequired] public ImmutableArray<int> Months { get; init; } = [];
    [JsonRequired] public uint? MinimumMoneyExclusive { get; init; }
    [JsonRequired] public bool RequiresMoneyCapacity { get; init; }
    [JsonRequired] public int TrustReward { get; init; } = 2;
    [JsonRequired] public int TrustPenalty { get; init; } = 2;
    [JsonRequired] public int DefeatInjury { get; init; } = 7;

    public void Validate(IReadOnlySet<int> places)
    {
        if (Consumption != EventConsumption.None || !Enum.IsDefined(ChanceAbility) || ChanceDivisor <= 0 ||
            MinimumBaseTrustExclusive is < -1 or > 999 || MaximumBaseTrustExclusive is < 1 or > 1001 ||
            MinimumBaseTrustExclusive >= MaximumBaseTrustExclusive || MaximumInjuryExclusive is < 1 or > 15 ||
            Months.IsDefault || Months.Any(m => m is < 1 or > 12) || Months.Distinct().Count() != Months.Length ||
            TrustReward < 0 || TrustPenalty < 0 || DefeatInjury is < 0 or > 14)
            throw new ArgumentException("Invalid search conflict gates.");
        bool valid = this switch
        {
            MoneyDemandSearchEventDefinition d => ValidIds(d.DemandingOfficerIds) && !d.DemandingOfficerIds.IsEmpty && d.PoliticsCostDivisor > 0 && d.CostSteps >= 0,
            ScholarPunishmentSearchEventDefinition d => !d.Sites.IsDefaultOrEmpty && d.PunishmentLifespanCeiling >= 0 &&
                d.Sites.All(s => s is not null && places.Contains(s.PlaceId) && s.ScholarOfficerId >= 0 && ValidIds(s.RewardTacticIds) && !string.IsNullOrWhiteSpace(s.HistoryKey.Value)) &&
                d.Sites.Select(s => s.PlaceId).Distinct().Count() == d.Sites.Length && d.Sites.Select(s => s.HistoryKey).Distinct().Count() == d.Sites.Length,
            EvildoerSearchEventDefinition d => d.MinimumSuccessChance is >= 0 and <= 100,
            LandSpiritSearchEventDefinition d => d.MinimumSuccessChance is >= 0 and <= 100,
            BanditSuppressionSearchEventDefinition d => d.MinimumSuccessChance is >= 0 and <= 100 && d.AppeasementPoliticsCeiling >= 0 && d.AppeasementTrustReward >= 0,
            TravelerRescueSearchEventDefinition => true,
            BanditMediationSearchEventDefinition d => !d.Sites.IsDefaultOrEmpty &&
                d.Sites.All(s => s is not null && places.Contains(s.PlaceId) && s.FavoredLeaderId >= 0) &&
                d.Sites.Select(s => s.PlaceId).Distinct().Count() == d.Sites.Length && ValidIds(d.GuaranteedOfficerIds) && d.MinimumSuccessChance is >= 0 and <= 100,
            _ => false
        };
        if (!valid) throw new ArgumentException("Invalid search conflict parameters.");
    }
    public void ValidateReferences(IReadOnlySet<int> officers, IReadOnlySet<int> tactics)
    {
        bool valid = this switch
        {
            MoneyDemandSearchEventDefinition d => d.DemandingOfficerIds.All(officers.Contains),
            ScholarPunishmentSearchEventDefinition d => d.Sites.All(s => officers.Contains(s.ScholarOfficerId) && s.RewardTacticIds.All(tactics.Contains)),
            BanditMediationSearchEventDefinition d => d.Sites.All(s => officers.Contains(s.FavoredLeaderId)) && d.GuaranteedOfficerIds.All(officers.Contains),
            _ => true
        };
        if (!valid) throw new ArgumentException("Unknown search conflict reference.");
    }
    private static bool ValidIds(ImmutableArray<int> ids) => !ids.IsDefault && ids.All(id => id >= 0) && ids.Distinct().Count() == ids.Length;

}

public sealed record MoneyDemandSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<int> DemandingOfficerIds,
    [property: JsonRequired] uint CostUnit,
    [property: JsonRequired] int CostSteps,
    [property: JsonRequired] int PoliticsCostDivisor) : ConflictSearchEventDefinition;
public sealed record ScholarPunishmentSite(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int ScholarOfficerId,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds,
    [property: JsonRequired] PersistentEventKey HistoryKey)
{
    [JsonIgnore] public PersistentEventKey WithdrawalKey => new($"scholar-punishment.withdrawn:{ScholarOfficerId}");
}
public sealed record ScholarPunishmentSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<ScholarPunishmentSite> Sites,
    [property: JsonRequired] int PunishmentLifespanCeiling) : ConflictSearchEventDefinition;
public sealed record EvildoerSearchEventDefinition(
    [property: JsonRequired] int MinimumSuccessChance) : ConflictSearchEventDefinition;
public sealed record LandSpiritSearchEventDefinition(
    [property: JsonRequired] int MinimumSuccessChance,
    [property: JsonRequired] uint MoneyReward) : ConflictSearchEventDefinition;
public sealed record BanditSuppressionSearchEventDefinition(
    [property: JsonRequired] int MinimumSuccessChance,
    [property: JsonRequired] uint AppeasementCostMultiplier,
    [property: JsonRequired] int AppeasementPoliticsCeiling,
    [property: JsonRequired] int AppeasementTrustReward) : ConflictSearchEventDefinition;
public sealed record TravelerRescueSearchEventDefinition(
    [property: JsonRequired] uint IntelligenceRewardMultiplier,
    [property: JsonRequired] uint BaseMoneyReward) : ConflictSearchEventDefinition;
public sealed record BanditMediationSite(
    [property: JsonRequired] int PlaceId,
    [property: JsonRequired] int FavoredLeaderId);
public sealed record BanditMediationSearchEventDefinition(
    [property: JsonRequired] ImmutableArray<BanditMediationSite> Sites,
    [property: JsonRequired] ImmutableArray<int> GuaranteedOfficerIds,
    [property: JsonRequired] int MinimumSuccessChance,
    [property: JsonRequired] uint IneligibleCorpsSoldierTotal,
    [property: JsonRequired] uint SoldierMultiplier,
    [property: JsonRequired] uint FavoredSoldierMultiplier,
    [property: JsonRequired] uint FavoredMoneyReward) : ConflictSearchEventDefinition;
