using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Directed engagements derived from current orders, ownership and attack reach.</summary>
internal static class MilitaryEngagements
{
    internal static BattleSource? Target(WorldMutation world, UnitState unit, MilitaryRulebook military)
    {
        if (unit.IsEmbedded || unit.IsPersonnel || unit.Soldiers == 0 || unit.Morale == 0 ||
            unit.Condition is UnitCondition.Confused or UnitCondition.Routed ||
            unit.FormationId is not int formation || unit.EffectiveOrder is not { IsAttack: true } order ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, order, military.Formations[formation], military) ||
            !MilitaryCombat.Area(unit, military).IntersectsExpanded(MilitaryCombat.TargetArea(world, order, military),
                MilitaryCombat.UnitRange(world, unit, order, military))) return null;
        return new(order.UnitId ?? order.SettlementId!.Value, order.UnitId is null);
    }

    internal static UnitState? FacilityTarget(WorldMutation world, SettlementState settlement, MilitaryRulebook military)
    {
        if (!FacilityCombat.Active(settlement) || !military.TryPlace(settlement, out _) ||
            military.FacilityAttack(settlement).Attack is null) return null;
        var area = new BattleSource(settlement.Id, true).Area(world, military);
        return world.Units.Values.Where(u => !u.IsPersonnel && !u.IsEmbedded &&
            (MilitaryDiplomacy.Work(world, settlement.Id, military) || MilitaryDiplomacy.AutomaticEnemy(world, settlement.OwnerId!.Value, u.OwnerId)) && u.OwnerId != settlement.OwnerId &&
            u.FormationId is int f && military.Formations[f].Combat is not null &&
            MilitaryCombat.Area(u, military).IntersectsExpanded(area, MilitaryCombat.FacilityRange(world, settlement, u, military)))
            .OrderBy(u => MilitaryCombat.Area(u, military).DistanceTo(area)).ThenBy(u => u.Id).FirstOrDefault();
    }

    internal static HashSet<BattleSource> Incoming(WorldMutation world, BattleSource subject, MilitaryRulebook military)
    {
        HashSet<BattleSource> result = [];
        foreach (var unit in world.Units.Values)
            if (Target(world, unit, military) == subject) result.Add(new(unit.Id, false));
        if (!subject.IsSettlement)
            foreach (var settlement in world.Settlements.Values)
                if (FacilityTarget(world, settlement, military)?.Id == subject.Id) result.Add(new(settlement.Id, true));
        return result;
    }

    internal static int Intensity(WorldMutation world, BattleSource subject, BattleSource opponent, MilitaryRulebook military)
    {
        var attackers = Incoming(world, subject, military);
        attackers.Add(opponent);
        return attackers.Count;
    }

    internal static bool Engaged(WorldMutation world, UnitState unit, MilitaryRulebook military) =>
        Target(world, unit, military) is not null || Incoming(world, new(unit.Id, false), military).Count > 0;
}
