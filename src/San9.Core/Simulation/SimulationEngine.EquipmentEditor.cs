using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed partial class SimulationEngine
{
    /// <summary>Applies full-width equipment instance edits as one validated world transaction.</summary>
    public IReadOnlyList<DomainEvent> EditEquipment(ImmutableArray<EquipmentInstanceEdit> edits)
    {
        if (_world.IsExecuting || _world.Simulation.Boundary != SimulationBoundary.Stable)
            throw new InvalidOperationException("Editing requires a stable strategy boundary.");
        if (edits.IsDefaultOrEmpty || edits.Any(e => e is null) || edits.Select(e => e.Id).Distinct().Count() != edits.Length)
            throw new ArgumentException("Invalid equipment edit targets.");
        _world.EnterExecution();
        try
        {
            var world = BeginMutation();
            if (!world.EquipmentInitialized) throw new ArgumentException("Equipment is not initialized.");
            var targets = new HashSet<(int Officer, EquipmentSlot Slot)>();
            foreach (var edit in edits.OrderBy(e => e.Id))
            {
                if (!world.AllEquipment.TryGetValue(edit.Id, out var original) || original.PendingArmamentCredit is not null || !_rules.Equipment.Items.TryGetValue(original.DefinitionId, out var definition) ||
                    edit.OfficerId is not null && edit.InventoryForceId is not null ||
                    edit.OfficerId is int personId && (!world.Officers.TryGetValue(personId, out var person) || !person.IsVisible) ||
                    edit.InventoryForceId is int forceId && (!world.Forces.TryGetValue(forceId, out var force) || force.IsEliminated) ||
                    edit.Equipped && (edit.OfficerId is not int wearer || !world.Officers[wearer].IsServing ||
                        !EquipmentRules.MeetsRequirements(definition, world.Officers[wearer].BaseAbilities) || !targets.Add((wearer, definition.Slot))))
                    throw new ArgumentException("Invalid equipment ownership or requirements.");
                bool moved = original.OfficerId != edit.OfficerId || original.InventoryForceId != edit.InventoryForceId;
                if (moved && edit.OfficerId is null && edit.InventoryForceId is null)
                    throw new ArgumentException("A moved item needs a holder or inventory.");
                if (moved && original.LegacyTreasureId is int treasureId)
                {
                    var treasure = world.AllTreasures[treasureId];
                    world.Replace(treasure with { Discovery = TreasureDiscovery.Discovered, HolderId = edit.OfficerId });
                }
                var current = world.AllEquipment[edit.Id];
                var next = current with { OfficerId = edit.OfficerId, InventoryForceId = edit.InventoryForceId,
                    Equipped = false, IsLost = moved ? false : current.IsLost, SearchPlaceId = moved ? null : current.SearchPlaceId,
                    EventGiftKey = moved ? null : current.EventGiftKey,
                    EquipOnService = moved ? null : current.EquipOnService,
                    Level = edit.Level, Experience = edit.Experience };
                world.SetEquipment(next);
                if (!EquipmentRules.ValidInventory(world.AllEquipment.Values, _rules.Equipment.Items))
                    throw new ArgumentException("Invalid equipment level or inventory.");
                if (edit.Equipped)
                {
                    var equipped = EquipmentRules.Equip(world.AllEquipment.Values, _rules.Equipment.Items, edit.OfficerId!.Value, edit.Id)
                        ?? throw new ArgumentException("Incompatible equipment combination.");
                    foreach (var item in equipped)
                        if (world.AllEquipment[item.Id] != item) world.SetEquipment(item);
                }
            }
            List<DomainEvent> events = [];
            world.MaintainDerivedState(events);
            WorldInvariants.Validate(world);
            _world.Commit(world);
            return events;
        }
        finally { _world.ExitExecution(); }
    }
}
