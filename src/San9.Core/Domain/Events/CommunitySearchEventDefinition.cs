using System.Collections.Immutable;
using System.Text.Json.Serialization;
using San9.Core.Simulation;

namespace San9.Core.Domain;

public enum CommunityAbility { Leadership, War, Intelligence, Politics }
public enum CivicSearchKind { DisasterRelief, Volunteers, Bribe, TravelPermit, Festival }

public abstract record CommunitySearchEventDefinition : SearchEventDefinition
{
    [JsonRequired] public CommunityAbility ChanceAbility { get; init; }
    [JsonRequired] public int? ChanceDivisor { get; init; }
    [JsonRequired] public int MinimumTrustExclusive { get; init; } = -1;
    [JsonRequired] public int MaximumTrustExclusive { get; init; } = 1001;
    [JsonRequired] public uint? MinimumMoneyExclusive { get; init; }
    [JsonRequired] public ImmutableArray<int> Months { get; init; } = [];

    public void Validate(IReadOnlySet<int> places)
    {
        if (!Enum.IsDefined(ChanceAbility) || ChanceDivisor is <= 0 || MinimumTrustExclusive is < -1 or > 999 ||
            MaximumTrustExclusive is < 1 or > 1001 || MinimumTrustExclusive >= MaximumTrustExclusive ||
            Months.IsDefault || Months.Any(m => m is < 1 or > 12) || Months.Distinct().Count() != Months.Length)
            throw new ArgumentException("Invalid community search gates.");
        bool valid = this switch
        {
            CivicSearchEventDefinition d => Consumption == EventConsumption.None && Enum.IsDefined(d.Activity) &&
                d.CostAbilityCeiling >= 0 && d.SoldierMultiplier >= 0 && d.MoneyAbilityMultiplier >= 0 &&
                d.MaximumForceSoldiersExclusive > 0 && d.TrustReward >= 0 && d.TrustPenalty >= 0 && d.ExecutionTrustPenalty >= 0,
            CommemorationSearchEventDefinition d => Consumption == EventConsumption.OnSuccess && !d.Sites.IsDefaultOrEmpty &&
                d.TrustReward >= 0 && d.AbilityReward >= 0 && d.Sites.All(s => s is not null && places.Contains(s.PlaceId) &&
                    s.RequiredTreasureId is not < 0 && Enum.IsDefined(s.RewardAbility) && !string.IsNullOrWhiteSpace(s.ConsumptionKey.Value) &&
                    !s.RewardTacticIds.IsDefault && s.RewardTacticIds.All(t => t >= 0) && s.RewardTacticIds.Distinct().Count() == s.RewardTacticIds.Length) &&
                d.Sites.Select(s => s.PlaceId).Distinct().Count() == d.Sites.Length && d.Sites.Select(s => s.ConsumptionKey).Distinct().Count() == d.Sites.Length,
            TrainingSiteSearchEventDefinition d => Consumption == EventConsumption.None && !d.Sites.IsDefaultOrEmpty &&
                d.MinimumForceSoldiersExclusive >= 0 && d.MaximumProficiencyExclusive is > 0 and <= 1000 && d.LeadershipRewardDivisor > 0 &&
                d.Sites.All(s => s is not null && places.Contains(s.PlaceId) && s.Category is >= 0 and <= 8) &&
                d.Sites.Select(s => s.PlaceId).Distinct().Count() == d.Sites.Length,
            _ => false
        };
        if (!valid) throw new ArgumentException("Invalid community search parameters.");
    }
}

public sealed record CivicSearchEventDefinition([property: JsonRequired] CivicSearchKind Activity) : CommunitySearchEventDefinition
{
    [JsonRequired] public uint MinimumFoodExclusive { get; init; }
    [JsonRequired] public int CostAbilityCeiling { get; init; }
    [JsonRequired] public uint MoneyCostMultiplier { get; init; }
    [JsonRequired] public uint FoodCostMultiplier { get; init; }
    [JsonRequired] public int MaximumForceSoldiersExclusive { get; init; } = int.MaxValue;
    [JsonRequired] public int SoldierMultiplier { get; init; }
    [JsonRequired] public uint BaseMoneyReward { get; init; }
    [JsonRequired] public int MoneyAbilityMultiplier { get; init; }
    [JsonRequired] public int TrustReward { get; init; }
    [JsonRequired] public int TrustPenalty { get; init; }
    [JsonRequired] public int ExecutionTrustPenalty { get; init; }
}

public sealed record CommemorationSite([property: JsonRequired] int PlaceId,
    [property: JsonRequired] int? RequiredTreasureId, [property: JsonRequired] CommunityAbility RewardAbility,
    [property: JsonRequired] ImmutableArray<int> RewardTacticIds, [property: JsonRequired] PersistentEventKey ConsumptionKey);
public sealed record CommemorationSearchEventDefinition([property: JsonRequired] ImmutableArray<CommemorationSite> Sites,
    [property: JsonRequired] uint Cost, [property: JsonRequired] int TrustReward,
    [property: JsonRequired] int AbilityReward) : CommunitySearchEventDefinition;
public sealed record TrainingSearchSite([property: JsonRequired] int PlaceId, [property: JsonRequired] int Category);
public sealed record TrainingSiteSearchEventDefinition([property: JsonRequired] ImmutableArray<TrainingSearchSite> Sites,
    [property: JsonRequired] uint Cost, [property: JsonRequired] int MinimumForceSoldiersExclusive,
    [property: JsonRequired] int MaximumProficiencyExclusive,
    [property: JsonRequired] int LeadershipRewardDivisor) : CommunitySearchEventDefinition;
