using System.Collections.Immutable;
using System.Text.Json.Serialization;

namespace San9.Content.Scenarios;

public enum OriginalAiMode { General, Domestic, TargetForce, TargetSettlement, NoFrontier, MilitaryPreparation }
public enum OriginalAiWork
{
    Patrol, Commerce, Cultivation, Repair, Recruit, Train, Attack, CreateCorps, Adviser, Offices,
    Reward, GrantTreasure, ConfiscateTreasure, MoveOfficers, Search, RecruitLocal, RecruitEnemy,
    FalseReport, Confuse, Encourage, Reinforce, Alienate, Burn, Seize, Gossip,
    Gift, RequestAttack, ReturnPrisoner, Surrender, Transport, DisposePrisoner, Retreat, Deploy,
    FulfillRequest, Trade, Residual,
}

public sealed record OriginalAiWorkRule(int OpeningWaitDays, int PeriodDays, int MaximumAttempts,
    bool AllowsBarbarians, bool AllowsChildCorps, int MaximumGeometricDays = 255);

/// <summary>Authored policy, independent of executable offsets and source object slots.</summary>
public sealed record OriginalAiDefinition
{
    [JsonRequired] public OriginalAiMilitaryDefinition Military { get; init; } = new();
    [JsonRequired] public ImmutableDictionary<int, int> DiplomacyFacilityPriorities { get; init; } = ImmutableDictionary<int, int>.Empty;
    [JsonRequired] public bool Advanced { get; init; } = true;
    [JsonRequired] public bool RecruitmentEnabled { get; init; } = true;
    [JsonRequired] public ImmutableDictionary<int, int> FamilyRankBonuses { get; init; } = ImmutableDictionary<int, int>.Empty;
    [JsonRequired] public ImmutableArray<int> PreferredStrategyRulerIds { get; init; } = [];
    [JsonRequired] public ImmutableDictionary<OriginalAiWork, OriginalAiWorkRule> WorkRules { get; init; } = DefaultRules;
    [JsonRequired] public ImmutableDictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>> SecondaryBatches { get; init; } = DefaultBatches;


    public void Validate(IReadOnlySet<int> people, San9.Core.Domain.MilitaryDefinition? military)
    {
        if (DiplomacyFacilityPriorities is null || DiplomacyFacilityPriorities.Any(p => p.Key < 0 || p.Value < 0) || FamilyRankBonuses is null || FamilyRankBonuses.Any(p => p.Key < 0 || p.Value < 0) ||
            PreferredStrategyRulerIds.IsDefault || PreferredStrategyRulerIds.Distinct().Count() != PreferredStrategyRulerIds.Length ||
            PreferredStrategyRulerIds.Any(id => !people.Contains(id)) || WorkRules is null || SecondaryBatches is null ||
            DefaultRules.Keys.Any(w => !WorkRules.ContainsKey(w)) || WorkRules.Any(p => !Enum.IsDefined(p.Key) || p.Value is null ||
                p.Value.OpeningWaitDays < 0 || p.Value.PeriodDays < 0 || p.Value.MaximumAttempts < 1 || p.Value.MaximumGeometricDays < 0) ||
            Enum.GetValues<OriginalAiMode>().Any(m => !SecondaryBatches.ContainsKey(m)) ||
            SecondaryBatches.Any(p => !Enum.IsDefined(p.Key) || p.Value.IsDefault || p.Value.Any(w => !Enum.IsDefined(w))))
            throw new InvalidDataException("Invalid original AI policy.");
        var facilities = military?.Facilities.Select(f => f.Id).ToHashSet() ?? [];
        if (DiplomacyFacilityPriorities.Keys.Any(id => !facilities.Contains(id)))
            throw new InvalidDataException("Unknown AI diplomacy facility reference.");
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
        [OriginalAiWork.CreateCorps] = new(120, 90, 1, false, false),
        [OriginalAiWork.Adviser] = new(40, 30, 1, false, false),
        [OriginalAiWork.Offices] = new(120, 30, 1, false, false),
        [OriginalAiWork.Reward] = new(90, 30, 1, false, false),
        [OriginalAiWork.GrantTreasure] = new(120, 30, 1, false, false),
        [OriginalAiWork.ConfiscateTreasure] = new(120, 30, 1, false, false),
        [OriginalAiWork.MoveOfficers] = new(60, 0, 255, false, true),
        [OriginalAiWork.Search] = new(40, 0, 3, false, true),
        [OriginalAiWork.RecruitLocal] = new(90, 20, 3, false, true),
        [OriginalAiWork.RecruitEnemy] = new(90, 20, 3, false, true),
        [OriginalAiWork.DisposePrisoner] = new(150, 45, 2, false, false),
        [OriginalAiWork.Residual] = new(0, 0, 255, true, true),
        [OriginalAiWork.Attack] = new(120, 0, 255, true, true),
        [OriginalAiWork.Retreat] = new(0, 0, 5, true, true),
        [OriginalAiWork.Deploy] = new(0, 0, 255, true, true),
        [OriginalAiWork.Transport] = new(90, 20, 3, false, true),
        [OriginalAiWork.FalseReport] = new(0, 0, 3, false, true, 20),
        [OriginalAiWork.Confuse] = new(0, 0, 3, false, true, 30),
        [OriginalAiWork.Encourage] = new(100, 0, 3, false, true, 20),
        [OriginalAiWork.Reinforce] = new(100, 0, 3, false, true, 20),
        [OriginalAiWork.Alienate] = new(90, 20, 3, false, true, 255),
        [OriginalAiWork.Burn] = new(90, 20, 3, false, true, 30),
        [OriginalAiWork.Seize] = new(90, 20, 3, false, true, 30),
        [OriginalAiWork.Gossip] = new(120, 20, 3, false, true, 30),
        [OriginalAiWork.Gift] = new(180, 30, 1, true, false, 255),
        [OriginalAiWork.RequestAttack] = new(120, 30, 1, false, false, 30),
        [OriginalAiWork.ReturnPrisoner] = new(150, 30, 1, false, false, 255),
        [OriginalAiWork.Surrender] = new(180, 30, 1, false, false, 255),
        [OriginalAiWork.FulfillRequest] = new(10, 0, 255, false, true),
    }.ToImmutableDictionary();

    private static ImmutableDictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>> DefaultBatches { get; } =
        new Dictionary<OriginalAiMode, ImmutableArray<OriginalAiWork>>
        {
            [OriginalAiMode.General] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Search, OriginalAiWork.Alienate, OriginalAiWork.Burn, OriginalAiWork.Seize, OriginalAiWork.Gossip],
            [OriginalAiMode.Domestic] = [OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Train, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Seize, OriginalAiWork.Search],
            [OriginalAiMode.TargetForce] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Burn, OriginalAiWork.Seize, OriginalAiWork.Alienate, OriginalAiWork.Gossip, OriginalAiWork.Search],
            [OriginalAiMode.TargetSettlement] = [OriginalAiWork.Attack, OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Burn, OriginalAiWork.Seize, OriginalAiWork.Alienate, OriginalAiWork.Gossip, OriginalAiWork.Search],
            [OriginalAiMode.NoFrontier] = [OriginalAiWork.FulfillRequest, OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.RecruitEnemy, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Repair, OriginalAiWork.Search, OriginalAiWork.Seize],
            [OriginalAiMode.MilitaryPreparation] = [OriginalAiWork.Confuse, OriginalAiWork.FalseReport, OriginalAiWork.Reinforce, OriginalAiWork.Encourage, OriginalAiWork.Transport, OriginalAiWork.Recruit, OriginalAiWork.Train, OriginalAiWork.Repair, OriginalAiWork.Commerce, OriginalAiWork.Cultivation, OriginalAiWork.Patrol, OriginalAiWork.Search, OriginalAiWork.Seize],
        }.ToImmutableDictionary();

    public static OriginalAiDefinition Default { get; } = new();
}
