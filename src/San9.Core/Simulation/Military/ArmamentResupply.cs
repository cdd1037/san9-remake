using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record ResupplyUnitArmamentsCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId)
    : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ConfigureUnitArmamentCommand(Guid CommandId, int IssuerId, long Sequence, int UnitId,
    int OfficerId, ArmamentSlot Slot, string? DefinitionId) : GameCommand(CommandId, IssuerId, Sequence);
public sealed record ArmamentResupplyLine(int OfficerId, ArmamentSlot Slot, string DefinitionId, uint Sets);
public sealed record UnitArmamentsResuppliedEvent(GameClock Clock, int ForceId, int UnitId,
    ImmutableArray<ArmamentResupplyLine> Lines) : DomainEvent;
public sealed record UnitArmamentConfiguredEvent(GameClock Clock, int ForceId, int UnitId, int OfficerId,
    ArmamentSlot Slot, string? PreviousDefinitionId, uint ReturnedSets, string? DefinitionId, uint IssuedSets) : DomainEvent;

internal static class ArmamentResupply
{
    internal static CommandRejection Execute(WorldMutation world, ResupplyUnitArmamentsCommand command,
        MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (!CanManage(world, command.IssuerId, command.UnitId, rules, out var force, out var unit))
            return CommandRejection.InvalidTask;

        var stock = force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
        var reserve = force.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty;
        (stock, reserve) = ArmamentInventory.ReleaseReserve(stock, reserve);
        var detachments = unit.Detachments;
        var lines = ImmutableArray.CreateBuilder<ArmamentResupplyLine>();
        foreach (var (officerId, original) in unit.Detachments.OrderBy(pair => pair.Key))
        {
            var current = original;
            foreach (var slot in Enum.GetValues<ArmamentSlot>())
            {
                var issue = slot == ArmamentSlot.Weapon ? current.Weapon : current.Armor;
                if (issue is null || current.Soldiers == 0) continue;
                if (!issue.IsValid || !rules!.Armaments.TryGetValue(issue.DefinitionId, out var definition) ||
                    definition.Slot != slot || !definition.Supports(current.TroopTypeId) || issue.RemainingSets > current.Soldiers)
                    return CommandRejection.InvalidTask;
                if (definition.ResearchProjectId is string project && !force.CompletedResearch.Contains(project)) continue;
                uint count = Math.Min(current.Soldiers - issue.RemainingSets, stock.GetValueOrDefault(issue.DefinitionId));
                if (count == 0) continue;
                uint left = stock[issue.DefinitionId] - count;
                stock = left == 0 ? stock.Remove(issue.DefinitionId) : stock.SetItem(issue.DefinitionId, left);
                (stock, reserve) = ArmamentInventory.ReleaseReserve(stock, reserve);
                var updated = new ArmamentAllocation(issue.DefinitionId, checked(issue.RemainingSets + count), current.Soldiers, 0);
                current = slot == ArmamentSlot.Weapon ? current with { Weapon = updated } : current with { Armor = updated };
                lines.Add(new(officerId, slot, issue.DefinitionId, count));
            }
            detachments = detachments.SetItem(officerId, current);
        }
        if (lines.Count == 0) return CommandRejection.InvalidTask;
        world.Replace(force with { ArmamentInventory = stock.IsEmpty ? null : stock, ArmamentReserve = reserve.IsEmpty ? null : reserve });
        world.Replace(unit with { Detachments = detachments });
        events.Add(new UnitArmamentsResuppliedEvent(world.Clock, force.Id, unit.Id, lines.ToImmutable()));
        return CommandRejection.None;
    }

    internal static CommandRejection Configure(WorldMutation world, ConfigureUnitArmamentCommand command,
        MilitaryRulebook? rules, List<DomainEvent> events)
    {
        if (!CanManage(world, command.IssuerId, command.UnitId, rules, out var force, out var unit) ||
            !Enum.IsDefined(command.Slot) || !unit.Detachments.TryGetValue(command.OfficerId, out var detachment) ||
            detachment.Soldiers == 0 || !world.Officers.TryGetValue(command.OfficerId, out var officer) ||
            !officer.IsServing || officer.UnitId != unit.Id)
            return CommandRejection.InvalidTask;
        var previous = command.Slot == ArmamentSlot.Weapon ? detachment.Weapon : detachment.Armor;
        if (previous is not null && (!previous.IsValid || previous.RemainingSets > detachment.Soldiers))
            return CommandRejection.InvalidTask;
        var inventory = force.ArmamentInventory ?? ImmutableDictionary<string, uint>.Empty;
        var reserve = force.ArmamentReserve ?? ImmutableDictionary<string, ulong>.Empty;
        if (previous is not null)
        {
            try { (inventory, reserve) = ArmamentInventory.ReturnWithReserve(inventory, reserve, previous); }
            catch (OverflowException) { return CommandRejection.ResourceOverflow; }
        }
        (inventory, reserve) = ArmamentInventory.ReleaseReserve(inventory, reserve);
        ArmamentAllocation? replacement = null;
        if (command.DefinitionId is string id)
        {
            if (string.IsNullOrWhiteSpace(id) || !rules!.Armaments.TryGetValue(id, out var definition) ||
                definition.Slot != command.Slot || !definition.Supports(detachment.TroopTypeId) ||
                definition.ResearchProjectId is string project && !force.CompletedResearch.Contains(project) ||
                inventory.GetValueOrDefault(id) == 0)
                return CommandRejection.InvalidTask;
            (inventory, replacement) = ArmamentInventory.Issue(inventory, definition, detachment.TroopTypeId, detachment.Soldiers);
        }
        if (previous?.DefinitionId == replacement?.DefinitionId && previous?.RemainingSets == replacement?.RemainingSets)
            return CommandRejection.InvalidTask;
        (inventory, reserve) = ArmamentInventory.ReleaseReserve(inventory, reserve);
        var changed = command.Slot == ArmamentSlot.Weapon ? detachment with { Weapon = replacement } : detachment with { Armor = replacement };
        world.Replace(force with { ArmamentInventory = inventory.IsEmpty ? null : inventory, ArmamentReserve = reserve.IsEmpty ? null : reserve });
        world.Replace(unit with { Detachments = unit.Detachments.SetItem(command.OfficerId, changed) });
        events.Add(new UnitArmamentConfiguredEvent(world.Clock, force.Id, unit.Id, command.OfficerId, command.Slot,
            previous?.DefinitionId, previous?.RemainingSets ?? 0, replacement?.DefinitionId, replacement?.RemainingSets ?? 0));
        return CommandRejection.None;
    }

    private static bool CanManage(WorldMutation world, int forceId, int unitId, MilitaryRulebook? rules,
        out ForceState force, out UnitState unit)
    {
        force = null!; unit = null!;
        if (!world.Simulation.NewGame.MixedTroopsEnabled || rules is null ||
            !world.Forces.TryGetValue(forceId, out var owner) || owner.IsDormant || owner.IsEliminated ||
            !world.Units.TryGetValue(unitId, out var target) || target.OwnerId != owner.Id || target.IsPersonnel ||
            target.IsEmbedded || target.Soldiers == 0 || target.Detachments.IsEmpty || target.ActiveEngagement || target.PassiveEngagement ||
            target.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) || rules.Map.IsWater(target.Position) ||
            rules.RegionAt(target.Position) is not int region || !world.Settlements.TryGetValue(region, out var home) ||
            home.OwnerId != owner.Id || home.IsDormant || home.IsDestroyed || !home.CanHostPersons) return false;
        force = owner; unit = target; return true;
    }
}
