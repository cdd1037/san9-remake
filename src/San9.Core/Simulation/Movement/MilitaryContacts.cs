using San9.Core.Domain;

namespace San9.Core.Simulation;

/// <summary>Command contact is separate from player intent and from combat's latched engagement state.</summary>
internal static class MilitaryContacts
{
    internal static MilitaryOrder Attack(MapObjectReference target) => target.IsSettlement
        ? new(MilitaryOrderKind.AttackSettlement, null, target.Id)
        : new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id };

    internal static bool Valid(IWorldQuery world, UnitState unit, MapObjectReference target, MilitaryRulebook rules) =>
        MilitaryCombat.ValidTarget(world, unit.OwnerId, Attack(target), rules.Formation(unit), rules);

    internal static UnitState AfterMovement(IWorldQuery world, UnitState unit, MapOccupancy? collision, MilitaryRulebook rules)
    {
        if (rules.Purpose(unit.FormationId!.Value) != UnitPurpose.Combat || unit.Condition == UnitCondition.Disguised) return unit;
        bool InRange(MilitaryOrder order) => MilitaryCombat.ValidTarget(world, unit.OwnerId, order, rules.Formation(unit), rules) &&
            MilitaryCombat.Area(unit, rules).IntersectsExpanded(MilitaryCombat.TargetArea(world, order, rules),
                MilitaryCombat.UnitRange(world, unit, order, rules));
        MapObjectReference? target = unit.ContactTarget is { } existing && InRange(Attack(existing)) ? existing : null;
        if (unit.CommandOrder is { IsAttack: true } intended && InRange(intended))
            target = new(intended.UnitId ?? intended.SettlementId!.Value, intended.UnitId is null);
        else if (target is null && collision?.Object is { IsSettlement: true } hit &&
            world.Settlements.TryGetValue(hit.Id, out var place) && !place.IsDestroyed && place.Facility?.BuilderUnitId is null &&
            (MilitaryDiplomacy.Work(world, place.Id, rules) || place.OwnerId is null ||
                MilitaryDiplomacy.AutomaticEnemy(world, unit.OwnerId, place.OwnerId)) && Valid(world, unit, hit, rules)) target = hit;
        return target != unit.ContactTarget ? unit with { ContactTarget = target, Lookahead = null } : unit;
    }
}
