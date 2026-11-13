using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Content.Scenarios;

/// <summary>Creates deterministic equipment metadata and owned instances from legacy treasure data.</summary>
public static class LegacyEquipmentMigration
{
    public sealed record Result(ImmutableArray<EquipmentDefinition> Definitions,
        ImmutableArray<EquipmentInstance> Instances, ImmutableArray<int> UnclassifiedTreasureIds);

    public static Result Convert(IEnumerable<TreasureDefinition> definitions, IEnumerable<TreasureState> states,
        IEnumerable<EquipmentDefinition>? authored = null)
    {
        var source = definitions.ToArray();
        var holdings = states.ToArray();
        if (source.Any(d => d.Id < 0 || d.Category < 0 || !d.AbilityBonuses.IsNonnegative || d.TacticId is < 0) ||
            source.Select(d => d.Id).Distinct().Count() != source.Length ||
            holdings.Any(s => !s.IsValid) || holdings.Select(s => s.Id).Distinct().Count() != holdings.Length)
            throw new ArgumentException("Invalid legacy treasure data.");
        var byId = source.ToDictionary(d => d.Id);
        if (holdings.Any(s => s.Discovery != TreasureDiscovery.Disabled && !byId.ContainsKey(s.Id)))
            throw new ArgumentException("Unknown legacy treasure definition.");
        var explicitItems = (authored ?? []).Select(item => item.LegacyTreasureId is int id &&
            item.Id == $"legacy-treasure:{id}" && item.Slot == EquipmentSlot.Treasure &&
            byId.TryGetValue(id, out var treasure) && treasure.Category is >= 2 and <= 9
                ? item with { Slot = EquipmentSlot.Book } : item).ToArray();
        var replacedLegacyIds = explicitItems.Where(item => item.LegacyTreasureId is not null).Select(item => item.LegacyTreasureId!.Value).ToHashSet();
        if (replacedLegacyIds.Any(id => !byId.ContainsKey(id))) throw new ArgumentException("Unknown authored treasure reference.");
        var mapped = source.Where(d => !replacedLegacyIds.Contains(d.Id)).OrderBy(d => d.Id).Select(d => new EquipmentDefinition($"legacy-treasure:{d.Id}",
            d.EscapeMount || d.Category == 0 ? EquipmentSlot.Mount : d.Category == 1 ? EquipmentSlot.MainHand :
                d.Category is >= 2 and <= 9 ? EquipmentSlot.Book : EquipmentSlot.Treasure,
            d.TacticId is not null || d.EscapeMount ? EquipmentRarity.Rare : EquipmentRarity.Precious,
            d.AbilityBonuses, default)
        { LegacyTreasureId = d.Id, TacticId = d.TacticId, EscapeMount = d.EscapeMount }).Concat(explicitItems).ToImmutableArray();
        var rulebook = new EquipmentRulebook(mapped);
        var byLegacyId = mapped.Where(d => d.LegacyTreasureId is not null).ToDictionary(d => d.LegacyTreasureId!.Value);
        var eligible = holdings.Where(s => s.Discovered && s.HolderId is not null).OrderBy(s => s.Id).ToArray();
        // Preserve every holding; choose the strongest original effect for each slot, with ID as a stable tie-breaker.
        var selected = eligible.GroupBy(s => (s.HolderId, byLegacyId[s.Id].Slot))
            .Select(group => group.OrderByDescending(s => Value(byId[s.Id])).ThenBy(s => s.Id).First().Id).ToHashSet();
        var instances = holdings.Where(s => s.Discovery != TreasureDiscovery.Disabled && byLegacyId.ContainsKey(s.Id)).OrderBy(s => s.Id)
            .Select(s => new EquipmentInstance(s.Id, byLegacyId[s.Id].Id, s.Discovered ? s.HolderId : null, null, selected.Contains(s.Id))
            { LegacyTreasureId = s.Id }).ToImmutableArray();
        if (!EquipmentRules.ValidInventory(instances, rulebook.Items))
            throw new ArgumentException("Legacy equipment migration produced an incompatible or conflicting loadout.");
        return new(mapped, instances, source.Where(d => d.Category > 12).Select(d => d.Id).Order().ToImmutableArray());
    }

    private static long Value(TreasureDefinition d) => (long)d.AbilityBonuses.War + d.AbilityBonuses.Intelligence +
        d.AbilityBonuses.Politics + d.AbilityBonuses.Leadership + d.LoyaltyBonus +
        (d.TacticId is null ? 0 : 1000) + (d.EscapeMount ? 1000 : 0);
}
