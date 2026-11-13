using System.Collections.Immutable;

namespace San9.Core.Domain;

[Flags]
public enum TacticSource { None = 0, Learned = 1, Force = 2, Treasure = 4 }

/// <summary>Queries current tactic sources without changing permanent learning or caching world state.
/// Source IDs are semantic references, independent of selectable tactics and UI page coverage.</summary>
public static class TacticAvailability
{
    public static ImmutableDictionary<int, TacticSource> ForOfficer(OfficerState officer, ForceState? force,
        IEnumerable<TreasureState> holdings, TreasureRulebook treasures, IEnumerable<EquipmentInstance>? equipment = null)
    {
        var result = ImmutableDictionary.CreateBuilder<int, TacticSource>();
        foreach (var (id, source) in Contributions([officer], force?.Id == officer.OwnerId ? force : null, holdings, treasures, equipment))
            result[id] = result.GetValueOrDefault(id) | source;
        return result.ToImmutable();
    }

    public static bool HasForMembers(int tacticId, IEnumerable<OfficerState> members, ForceState force,
        IEnumerable<TreasureState> holdings, TreasureRulebook treasures, IEnumerable<EquipmentInstance>? equipment = null) =>
        Contributions(members, force, holdings, treasures, equipment).Any(entry => entry.Id == tacticId);

    private static IEnumerable<(int Id, TacticSource Source)> Contributions(IEnumerable<OfficerState> members,
        ForceState? force, IEnumerable<TreasureState> holdings, TreasureRulebook treasures, IEnumerable<EquipmentInstance>? equipment)
    {
        if (force is not null)
            foreach (int id in force.ProvidedTactics) yield return (id, TacticSource.Force);
        var memberIds = new HashSet<int>();
        foreach (var person in members)
        {
            memberIds.Add(person.Id);
            foreach (int id in person.LearnedTactics) yield return (id, TacticSource.Learned);
        }
        if (equipment is not null)
        {
            foreach (var item in equipment)
                if (item.Equipped && item.OfficerId is int holder && memberIds.Contains(holder) &&
                    treasures.Equipment.Items.TryGetValue(item.DefinitionId, out var definition) && definition.TacticId is int id)
                    yield return (id, TacticSource.Treasure);
            yield break;
        }
        foreach (var item in holdings)
            if (item.Discovered && item.HolderId is int holder && memberIds.Contains(holder) &&
                treasures.Items.TryGetValue(item.Id, out var definition) && definition.TacticId is int id)
                yield return (id, TacticSource.Treasure);
    }
}
