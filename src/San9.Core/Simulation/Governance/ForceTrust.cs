using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Total trust is derived from current ownership and assets, never from its previous value.</summary>
internal static class ForceTrust
{
    internal static int Calculate(WorldMutation world, EconomyRulebook rules, ForceState force)
    {
        long contribution = 0;
        foreach (var city in world.Settlements.Values)
            if (city.Kind == SettlementKind.City && city.OwnerId == force.Id)
                contribution += ((long)city.Population + city.DraftPopulation) * Math.Max(0, city.PublicOrder - 300) / 1000 / 5000;
        bool seal = SealOwner(world, rules) == force.Id;
        return (int)Math.Clamp(force.BaseTrust + contribution + (seal ? 100 : 0) +
            (EmperorOwner(world, rules) == force.Id ? 100 : 0), 0, 1000);
    }

    internal static int? EmperorOwner(WorldMutation world, EconomyRulebook rules) =>
        ImperialCourt.City(world, rules) is int city ? world.Settlements[city].OwnerId : null;

    internal static int? SealOwner(WorldMutation world, EconomyRulebook rules)
    {
        if (rules.ImperialSealId is not int item || !world.Treasures.TryGetValue(item, out var treasure) ||
            !treasure.Discovered || treasure.HolderId is not int holder) return null;
        if (world.EquipmentInitialized && !world.AllEquipment.Values.Any(equipment =>
                equipment.LegacyTreasureId == item && equipment.OfficerId == holder && equipment.Equipped)) return null;
        foreach (var force in world.Forces.Values)
            if (!force.IsEliminated && force.LeaderId == holder) return force.Id;
        return null;
    }
}
