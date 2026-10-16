using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Content.Scenarios;

public enum OriginalAiMode { General, Domestic, TargetForce, TargetSettlement, NoFrontier, MilitaryPreparation }
public enum OriginalAiWork
{
    Patrol, Commerce, Cultivation, Repair, Recruit, Train, Attack, CreateCorps, Adviser, Offices,
    Reward, GrantTreasure, ConfiscateTreasure, MoveOfficers, Search, RecruitLocal, RecruitEnemy,
    Burn, Confuse, Trap, Encourage, FalseReport, Alienate, Seize, Gossip,
    Gift, RequestAttack, ReturnPrisoner, Surrender, Transport, DisposePrisoner, Retreat, Deploy,
    FulfillRequest, Trade, Residual,
}

public sealed record OriginalAiWorkRule(int OpeningWaitDays, int PeriodDays, int MaximumAttempts,
    bool AllowsBarbarians, bool AllowsChildCorps);

/// <summary>Authored policy, independent of executable offsets and source object slots.</summary>
public sealed record OriginalAiDefinition
{
    [JsonRequired] public bool Advanced { get; init; }
    [JsonRequired] public bool RecruitmentEnabled { get; init; } = true;
    [JsonRequired] public ImmutableDictionary<int, int> FamilyRankBonuses { get; init; } = ImmutableDictionary<int, int>.Empty;
    [JsonRequired] public ImmutableArray<int> PreferredStrategyRulerIds { get; init; } = [];
    [JsonRequired] public ImmutableDictionary<OriginalAiWork, OriginalAiWorkRule> WorkRules { get; init; } = DefaultRules;
    [JsonRequired] public ImmutableDictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>> SecondaryBatches { get; init; } = DefaultBatches;


    public void Validate(IReadOnlySet<int> people)
    {
        if (FamilyRankBonuses is null || FamilyRankBonuses.Any(p => p.Key < 0 || p.Value < 0) ||
            PreferredStrategyRulerIds.IsDefault || PreferredStrategyRulerIds.Distinct().Count() != PreferredStrategyRulerIds.Length ||
            PreferredStrategyRulerIds.Any(id => !people.Contains(id)) || WorkRules is null || SecondaryBatches is null ||
            DefaultRules.Keys.Any(w => !WorkRules.ContainsKey(w)) || WorkRules.Any(p => !Enum.IsDefined(p.Key) || p.Value is null ||
                p.Value.OpeningWaitDays < 0 || p.Value.PeriodDays < 0 || p.Value.MaximumAttempts < 1) ||
            Enum.GetValues<OriginalAiMode>().Any(m => !SecondaryBatches.ContainsKey(m)) ||
            SecondaryBatches.Any(p => !Enum.IsDefined(p.Key) || p.Value.IsDefault || p.Value.Any(w => !Enum.IsDefined(w))))
            throw new InvalidDataException("Invalid original AI policy.");
    }

    private static ImmutableDictionary<OriginalAiWork, OriginalAiWorkRule> DefaultRules { get; } = new Dictionary<OriginalAiWork, OriginalAiWorkRule>
    {
        [OriginalAiWork.Patrol] = new(0, 0, 255, false, true),
        [OriginalAiWork.Commerce] = new(0, 0, 255, false, true),
        [OriginalAiWork.Cultivation] = new(0, 0, 255, false, true),
        [OriginalAiWork.Repair] = new(0, 0, 255, true, true),
        [OriginalAiWork.Recruit] = new(60, 0, 255, true, true),
        [OriginalAiWork.Train] = new(0, 0, 255, true, true),
        [OriginalAiWork.Trade] = new(60, 0, 1, false, true),
        [OriginalAiWork.Residual] = new(0, 0, 255, true, true),
    }.ToImmutableDictionary();

    private static ImmutableDictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>> DefaultBatches { get; } =
        new Dictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>>
        {
            [OriginalAiMode.General] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Search, OriginalAiWork.FalseReport, OriginalAiWork.Alienate, OriginalAiWork.Seize, OriginalAiWork.Gossip],
            [OriginalAiMode.Domestic] = [OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Train, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Seize, OriginalAiWork.Search],
            [OriginalAiMode.TargetForce] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Alienate, OriginalAiWork.Seize, OriginalAiWork.FalseReport, OriginalAiWork.Gossip, OriginalAiWork.Search],
            [OriginalAiMode.TargetSettlement] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Alienate, OriginalAiWork.Seize, OriginalAiWork.FalseReport, OriginalAiWork.Gossip, OriginalAiWork.Search],
            [OriginalAiMode.NoFrontier] = [OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Search, OriginalAiWork.Seize],
            [OriginalAiMode.MilitaryPreparation] = [OriginalAiWork.Confuse, OriginalAiWork.Burn, OriginalAiWork.Encourage, OriginalAiWork.Trap, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.Repair, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Search, OriginalAiWork.Seize],
        }.ToImmutableDictionary();

    public static OriginalAiDefinition Default { get; } = new();
}
