using San9.Content.Scenarios;
using San9.Core.Domain;
using System.Collections.Immutable;
using System.Globalization;

namespace San9.Application;

internal static class EquipmentMigration
{
    internal static SessionSnapshot Initialize(SessionSnapshot snapshot, ScenarioInitialState content, EquipmentRulebook rules)
    {
        if (!snapshot.World.EquipmentInitialized)
        {
            if (snapshot.World.Equipment.Count != 0)
                throw new InvalidDataException("Uninitialized equipment state contains existing instances.");
            var migrated = LegacyEquipmentMigration.Convert(content.Information.Treasures, snapshot.World.Treasures,
                content.Information.Equipment);
            snapshot = snapshot with { World = snapshot.World with
                { Equipment = UnequipNonServing(migrated.Instances.Concat(content.InitialEquipment ?? []), snapshot.World.Officers), EquipmentInitialized = true } };
        }
        var previousKeys = snapshot.InitializedSearchEquipment ?? [];
        var eventRewards = content.SearchEvents.OfType<EquipmentGiftSearchEventDefinition>().ToDictionary(gift => gift.EquipmentInstanceId);
        foreach (var gift in eventRewards.Values)
            if (snapshot.World.Equipment.FirstOrDefault(item => item.Id == gift.EquipmentInstanceId) is { } occupied &&
                occupied.DefinitionId != gift.EquipmentDefinitionId)
                throw new InvalidDataException($"Equipment gift instance ID conflicts with saved equipment: {gift.EquipmentInstanceId}.");
        var pending = (content.InitialEquipment ?? []).Where(item => (item.SearchPlaceId is not null || eventRewards.ContainsKey(item.Id)) &&
            !previousKeys.Contains(SearchKey(item))).OrderBy(item => item.Id).ToArray();
        if (pending.Length > 0)
        {
            var equipment = snapshot.World.Equipment.ToList();
            long next = equipment.Select(item => item.Id).Concat(pending.Select(item => item.Id)).DefaultIfEmpty(-1).Max();
            foreach (var item in pending)
            {
                var existing = equipment.FirstOrDefault(other => other.Id == item.Id);
                if (existing?.DefinitionId == item.DefinitionId) continue;
                equipment.Add(existing is null ? item : item with { Id = checked(++next) });
            }
            snapshot = snapshot with { World = snapshot.World with { Equipment = equipment.ToArray() } };
        }
        snapshot = snapshot with { InitializedSearchEquipment = previousKeys.Concat(SearchKeys(content)).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal).ToImmutableArray() };
        if (snapshot.ArmamentMigrationInitialized == true) return snapshot;
        var forces = snapshot.World.Forces.ToDictionary(force => force.Id);
        var officers = snapshot.World.Officers.ToDictionary(officer => officer.Id);
        foreach (var force in forces.Values.ToArray())
        {
            if (force.EquipmentProduction is not { } progress) continue;
            if (!rules.Items.TryGetValue(progress.DefinitionId, out var recipe) || recipe.ResearchProjectId is null)
                throw new InvalidDataException($"Unknown equipment production: {progress.DefinitionId}.");
            forces[force.Id] = force with { EquipmentProduction = null,
                ArmamentMigrationCredit = checked((force.ArmamentMigrationCredit ?? 0) + recipe.ProductionCost) };
        }
        var kept = new List<EquipmentInstance>();
        foreach (var item in snapshot.World.Equipment.OrderBy(item => item.Id))
        {
            if (!rules.Items.TryGetValue(item.DefinitionId, out var definition))
                throw new InvalidDataException($"Unknown equipment definition: {item.DefinitionId}.");
            if (!definition.IsOrdinaryArmament) { kept.Add(item); continue; }
            int value = item.PendingArmamentCredit ?? definition.PurchaseCost ?? definition.ProductionCost;
            int? owner = item.InventoryForceId;
            if (item.OfficerId is int personId)
            {
                if (!officers.TryGetValue(personId, out var person)) throw new InvalidDataException($"Unknown equipment holder: {personId}.");
                owner = person.IsServing ? person.OwnerId : null;
            }
            if (owner is not int forceId)
            {
                kept.Add(item with { Equipped = false, EquipOnService = null, PendingArmamentCredit = value });
                continue;
            }
            if (!forces.TryGetValue(forceId, out var force) || force.IsEliminated)
                throw new InvalidDataException($"Invalid equipment owner: {forceId}.");
            forces[forceId] = force with { ArmamentMigrationCredit = checked((force.ArmamentMigrationCredit ?? 0) + value) };
        }
        return snapshot with { InitialEquipmentIssued = true, ArmamentMigrationInitialized = true, World = snapshot.World with
        {
            Equipment = kept.ToArray(),
            Forces = snapshot.World.Forces.Select(force => forces[force.Id]).ToArray()
        } };
    }

    internal static EquipmentInstance[] UnequipNonServing(IEnumerable<EquipmentInstance> items, IEnumerable<OfficerState> officers)
    {
        var byId = officers.ToDictionary(officer => officer.Id);
        return items.Select(item => item.Equipped && item.OfficerId is int holder &&
            byId.TryGetValue(holder, out var officer) && !officer.IsServing ? item with { Equipped = false, EquipOnService = true } : item).ToArray();
    }

    private static string SearchKey(EquipmentInstance item) => $"{item.Id.ToString(CultureInfo.InvariantCulture)}:{item.DefinitionId}";
    internal static ImmutableArray<string> SearchKeys(ScenarioInitialState content)
    {
        var eventIds = content.SearchEvents.OfType<EquipmentGiftSearchEventDefinition>().Select(gift => gift.EquipmentInstanceId).ToHashSet();
        return (content.InitialEquipment ?? []).Where(item => item.SearchPlaceId is not null || eventIds.Contains(item.Id))
            .Select(SearchKey).Order(StringComparer.Ordinal).ToImmutableArray();
    }
}
