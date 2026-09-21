using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class DefectionPressure
{
    internal static bool OrdinaryForce(WorldMutation world, int id) => world.Forces.TryGetValue(id, out var force) &&
        !force.IsEliminated && !force.IsDormant && !force.IsBarbarian && force.LeaderId is not null;

    internal static IEnumerable<UnitState> FieldUnits(WorldMutation world) => world.Units.Values.Where(u => !u.IsEmbedded && !u.IsPersonnel);

    internal static int? BesiegedRecipient(WorldMutation world, BattleSource defender, OfficerState deciding,
        bool rulerPresent, SimulationRules rules, string stream)
    {
        if (rules.Military is not { } military) return null;
        if (defender.IsSettlement && !military.TryPlace(world.Settlements[defender.Id], out _)) return null;
        var area = defender.Area(world, military);
        int owner = defender.Owner(world);
        bool Targets(MilitaryOrder order) => defender.IsSettlement
            ? order.Kind == MilitaryOrderKind.AttackSettlement && order.SettlementId == defender.Id
            : order.Kind == MilitaryOrderKind.AttackUnit && order.UnitId == defender.Id;
        var attackers = FieldUnits(world).Where(u => u.Soldiers > 0 &&
            u.Condition is UnitCondition.Normal or UnitCondition.HighSpirits or UnitCondition.NoFormation &&
            u.EffectiveOrder is { } order && Targets(order) &&
            u.FormationId is int formation && military.Formations.ContainsKey(formation) &&
            MilitaryDiplomacy.AutomaticEnemy(world, owner, u.OwnerId) &&
            CellDistance(u.Position, area.Position) <= 1 &&
            MilitaryCombat.UnitRange(world, u, order, military) is > 0 and var range &&
            MilitaryCombat.Area(u, military).IntersectsExpanded(area, range)).ToArray();
        int? recipient = attackers.Select(u => u.OwnerId).Distinct().OrderByDescending(id => world.Forces[id].Trust)
            .ThenBy(id => id).Select(id => (int?)id).FirstOrDefault();
        if (attackers.Length < 2 || recipient is not int target || !OrdinaryForce(world, target)) return null;
        // The besieged army is not its own reinforcement.
        if (FieldUnits(world).Any(u => u.OwnerId == owner && (defender.IsSettlement || u.Id != defender.Id) &&
            CellDistance(u.Position, area.Position) <= 10)) return null;
        long strength = attackers.Where(u => u.OwnerId == target).Sum(u => (long)u.Soldiers);
        return Roll(world, deciding, rulerPresent, owner, target, defender.Soldiers(world), strength, stream) ? target : null;
    }

    private static long CellDistance(WorldPosition first, WorldPosition second)
    {
        var a = MapCoordinates.Cell(first);
        var b = MapCoordinates.Cell(second);
        return MapCoordinates.Distance(new(a.X, a.Y), new(b.X, b.Y));
    }

    internal static bool Roll(WorldMutation world, OfficerState deciding, bool rulerPresent, int owner, int target,
        long ownStrength, long targetStrength, string stream)
    {
        int loyalty = rulerPresent ? 255 : deciding.LoyaltyTo(owner);
        int willingness = rulerPresent ? 0 : deciding.LoyaltyTo(target);
        long chance = targetStrength / Math.Max(1000, ownStrength) + (willingness - loyalty) / 2 +
            (19 - deciding.Ambition) / 5 * (world.Forces[target].Trust - world.Forces[owner].Trust) / 50;
        return (long)(world.NextRandom(stream) % 100) < chance;
    }
}
