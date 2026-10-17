using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Combat identities and response inputs at activation, before damage, growth or removal.</summary>
public sealed record TacticOccurrence(BattleObjectContext Source, BattleObjectContext Target,
    int? SourceFormationId, int? TargetFormationId, UnitCondition TargetCondition,
    int? ResistanceOfficerId, int? IntelligenceOfficerId, int? CounterOfficerId, bool DefensiveFormation)
{
    [System.Text.Json.Serialization.JsonRequired]
    public ImmutableArray<int> SourceMemberIds { get; init; } = [];

    internal static TacticOccurrence Capture(WorldMutation world, BattleSource source, MilitaryOrder target,
        TacticRule tactic, MilitaryRulebook military, TreasureRulebook? treasures = null)
    {
        var defenders = IntelligenceTactics.Defenders(world, target);
        var targetSource = new BattleSource(target.UnitId ?? target.SettlementId!.Value, target.UnitId is null);
        var sourceUnit = source.IsSettlement ? null : world.Units[source.Id];
        var targetUnit = target.UnitId is int id ? world.Units[id] : null;
        var formation = targetUnit is null ? null : military.Formation(targetUnit);
        var condition = targetUnit?.Condition ?? (world.Settlements[target.SettlementId!.Value].ConfusionDaysRemaining > 0
            ? UnitCondition.Confused : UnitCondition.Normal);
        TacticEffect? counter = tactic.Effect switch
        {
            TacticEffect.Trap => TacticEffect.CounterTrap,
            TacticEffect.Confusion => TacticEffect.CounterConfusion,
            _ => null
        };
        int? owner = targetUnit?.OwnerId ?? world.Settlements.GetValueOrDefault(target.SettlementId ?? -1)?.OwnerId;
        var sourceContext = BattleObjectContext.Capture(world, source);
        return new(sourceContext, BattleObjectContext.Capture(world, targetSource),
            sourceUnit is null ? null : military.Formation(sourceUnit).Id, formation?.Id, condition,
            defenders.OrderByDescending(p => tactic.Category is int category ? p.TacticProficiencies[category] : 0)
                .ThenBy(p => p.Id).FirstOrDefault()?.Id,
            defenders.OrderByDescending(p => world.Abilities(p).Intelligence).ThenBy(p => p.Id).FirstOrDefault()?.Id,
            defenders.Where(p => counter is not null && formation?.BypassesPassiveCounters != true &&
                owner is int force && p.EquippedTacticId is int equipped && military.Tactics[equipped].Effect == counter &&
                MilitaryTactics.HasTactic(world, force, [p.Id], equipped, treasures ?? TreasureRulebook.Empty))
                .OrderBy(p => p.Id).FirstOrDefault()?.Id,
            formation?.ConfusionResistanceChance > 0)
        {
            SourceMemberIds = source.IsSettlement ? sourceContext.CommanderId is int commander ? [commander] : [] :
                world.ServingUnitMembers(source.Id).OrderBy(p => p.Id).Select(p => p.Id).ToImmutableArray()
        };
    }
}
