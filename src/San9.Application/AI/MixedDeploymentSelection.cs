using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Application.AI;

internal static class MixedDeploymentSelection
{
    internal static ImmutableDictionary<int, DetachmentState> EquipArmaments(
        ImmutableDictionary<int, DetachmentState> detachments, MilitaryRulebook rules, ForceState force)
    {
        var stock = force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
        var result = detachments.ToBuilder();
        foreach (int officer in detachments.Keys.Order())
        {
            var detachment = result[officer];
            foreach (var slot in Enum.GetValues<ArmamentSlot>())
            {
                var choice = rules.Armaments.Values.Where(item => item.Slot == slot && item.Supports(detachment.TroopTypeId) &&
                        stock.GetValueOrDefault(item.Id) > 0 &&
                        (item.ResearchProjectId is null || force.CompletedResearch.Contains(item.ResearchProjectId)))
                    .OrderByDescending(item => slot == ArmamentSlot.Weapon ? item.AttackBonus : item.DefenseBonus)
                    .ThenBy(item => item.Id, StringComparer.Ordinal).FirstOrDefault();
                if (choice is null) continue;
                var issued = ArmamentInventory.Issue(stock, choice, detachment.TroopTypeId, detachment.Soldiers);
                stock = issued.Inventory;
                detachment = slot == ArmamentSlot.Weapon ? detachment with { Weapon = issued.Allocation } : detachment with { Armor = issued.Allocation };
            }
            result[officer] = detachment;
        }
        return result.ToImmutable();
    }
    internal static (ImmutableDictionary<int, FormationPosition> Positions, ImmutableDictionary<int, DetachmentState> Detachments)
        EnsureFrontLine(FormationRule formation, ImmutableDictionary<int, FormationPosition> positions,
            ImmutableDictionary<int, DetachmentState> detachments, MilitaryRulebook rules, ForceState force)
    {
        if (detachments.IsEmpty || positions.Count == 0) return (positions, detachments);
        var frontSlots = formation.Positions.Where(position => position.Rank == BattleRank.Front)
            .OrderBy(position => position.Column).ToArray();
        if (frontSlots.Length == 0) return (positions, detachments);
        bool IsMelee(DetachmentState detachment) => !rules.TroopTypes[detachment.TroopTypeId].IsRangedAttack;
        if (positions.Any(pair => pair.Value.Rank == BattleRank.Front &&
            detachments.TryGetValue(pair.Key, out var existing) && IsMelee(existing))) return (positions, detachments);

        int? melee = positions.Keys.Where(id => detachments.TryGetValue(id, out var detachment) && IsMelee(detachment))
            .Order().Select(id => (int?)id).FirstOrDefault();
        int officer = melee ?? positions.OrderBy(pair => pair.Value.Rank).ThenBy(pair => pair.Key).First().Key;
        var oldPosition = positions[officer];
        var newPosition = frontSlots[0];
        int? occupant = positions.Where(pair => pair.Value == newPosition && pair.Key != officer)
            .Select(pair => (int?)pair.Key).FirstOrDefault();
        positions = positions.SetItem(officer, newPosition);
        if (occupant is int other) positions = positions.SetItem(other, oldPosition);
        if (melee is null)
        {
            var replacement = rules.TroopTypes.Values.Where(type => !type.IsRangedAttack &&
                type.LevelResearchProjects.All(project => project.Key > 1 || force.CompletedResearch.Contains(project.Value)))
                .OrderBy(type => type.EquipmentCostPerThousand.GetValueOrDefault(1)).ThenBy(type => type.Id).FirstOrDefault();
            if (replacement is not null)
                detachments = detachments.SetItem(officer, detachments[officer] with { TroopTypeId = replacement.Id, Level = 1 });
        }
        return (positions, detachments);
    }

    internal static int? SiegeEquipment(MilitaryRulebook rules, ForceState force, OfficerState[] people,
        IEnumerable<TreasureState> treasures, TreasureRulebook treasureRules, long budget, IEnumerable<EquipmentInstance>? equipment = null)
    {
        return rules.SiegeEquipment.Values.Where(item => item.DeploymentCost <= budget &&
                force.CompletedResearch.Contains(item.ResearchProjectId) &&
                (rules.Formations[item.FormationId].RequiredTacticId is not int required ||
                    TacticAvailability.HasForMembers(required, people, force, treasures, treasureRules, equipment)))
            .OrderByDescending(item => rules.Formations[item.FormationId].Combat!.AgainstWalls)
            .ThenByDescending(item => rules.Formations[item.FormationId].Attack!.Range)
            .ThenBy(item => item.DeploymentCost).ThenBy(item => item.Id).Select(item => (int?)item.Id).FirstOrDefault();
    }
}
