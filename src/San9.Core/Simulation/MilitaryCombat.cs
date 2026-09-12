using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BattleDamageEvent(GameClock Clock, int AttackerId, int? TargetUnitId, int? SettlementId,
    uint SoldierLoss, uint AddedWounded, uint Dead, int DurabilityLoss, int MoraleLoss, bool AttackerIsSettlement = false) : DomainEvent;

internal static class MilitaryCombat
{
    internal static bool Hostile(WorldMutation world, int attacker, int? defender) =>
        world.Forces.TryGetValue(attacker, out var force) && !force.IsEliminated && attacker != defender &&
        (defender is null || world.Forces.TryGetValue(defender.Value, out var other) && !other.IsEliminated);

    internal static bool ValidTarget(WorldMutation world, int owner, MilitaryOrder order, FormationRule formation, MilitaryRulebook rules)
    {
        if (formation.Combat is null || formation.Attack is null) return false;
        if (order.Kind == MilitaryOrderKind.AttackUnit)
            return order.UnitId is int id && world.Units.TryGetValue(id, out var unit) && Hostile(world, owner, unit.OwnerId) &&
                unit.FormationId is int f && rules.Formations[f].Combat is not null;
        return order.Kind == MilitaryOrderKind.AttackSettlement && order.SettlementId is int place &&
            world.Settlements.TryGetValue(place, out var settlement) && !settlement.IsDestroyed &&
            Hostile(world, owner, settlement.OwnerId) && rules.Places.TryGetValue(place, out var definition) && definition.Combat is not null;
    }

    internal static MapOccupancy TargetArea(WorldMutation world, MilitaryOrder order, MilitaryRulebook rules) =>
        order.UnitId is int id ? Area(world.Units[id], rules) :
        new(rules.Places[order.SettlementId!.Value].Position, rules.Places[order.SettlementId.Value].Footprint);

    internal static MapOccupancy Area(UnitState unit, MilitaryRulebook rules) => new(unit.Position, rules.Formations[unit.FormationId!.Value].Footprint);

    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        var used = new HashSet<int>();
        var buildingsUsed = new HashSet<int>();
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            if (world.Simulation.TerminalReason is not null) break;
            if (!world.Units.TryGetValue(id, out var attacker) || attacker.Order is not { IsAttack: true } order) continue;
            var formation = military.Formations[attacker.FormationId!.Value];
            if (!ValidTarget(world, attacker.OwnerId, order, formation, military))
            {
                world.Replace(attacker with { Order = null });
                events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "attack-target-invalid"));
                continue;
            }
            var targetArea = TargetArea(world, order, military);
            if (used.Contains(id) || Area(attacker, military).DistanceTo(targetArea) > formation.Attack!.Range) continue;
            used.Add(id);
            var affectedForces = new HashSet<int> { attacker.OwnerId };
            if (order.UnitId is int targetId)
            {
                var defender = world.Units[targetId];
                affectedForces.Add(defender.OwnerId);
                var defense = military.Formations[defender.FormationId!.Value];
                uint outgoing = CombatMath.TroopDamage(Strength(world, attacker), Strength(world, defender), formation.Combat!, defense.Combat!, false, 0);
                uint incoming = 0;
                if (!used.Contains(targetId) && defense.Attack is { } retaliation && targetArea.DistanceTo(Area(attacker, military)) <= retaliation.Range)
                {
                    used.Add(targetId);
                    incoming = CombatMath.TroopDamage(Strength(world, defender), Strength(world, attacker), defense.Combat!, formation.Combat!, false, 0);
                }
                DamageUnit(world, defender, outgoing, defense.Combat!, id, events);
                DamageUnit(world, attacker, incoming, formation.Combat!, targetId, events);
                bool attackerLost = Defeated(world.Units[id]), defenderLost = Defeated(world.Units[targetId]);
                if (defenderLost) BattleAftermath.TerminateUnit(world, targetId, attackerLost ? null : id, rules, events);
                if (attackerLost) BattleAftermath.TerminateUnit(world, id, defenderLost ? null : targetId, rules, events);
            }
            else
            {
                var settlement = world.Settlements[order.SettlementId!.Value];
                if (settlement.OwnerId is int defenderOwner) affectedForces.Add(defenderOwner);
                var definition = military.Places[settlement.Id];
                var defense = GarrisonStrength(world, settlement);
                uint outgoing = CombatMath.TroopDamage(Strength(world, attacker), defense, formation.Combat!, definition.Combat!, true, settlement.Durability);
                int walls = formation.Attack.DamagesWalls ? CombatMath.DurabilityDamage(attacker.Soldiers,
                    Strength(world, attacker).Leadership, formation.Combat!.AgainstWalls, settlement.Durability) : 0;
                uint incoming = 0;
                if (settlement.OwnerId is not null && settlement.Morale > 0 && definition.Attack is { } retaliation &&
                    !buildingsUsed.Contains(settlement.Id) && targetArea.DistanceTo(Area(attacker, military)) <= retaliation.Range)
                {
                    buildingsUsed.Add(settlement.Id);
                    incoming = CombatMath.TroopDamage(defense, Strength(world, attacker), definition.Combat!, formation.Combat!, false, 0);
                }
                var loss = CombatMath.Loss(settlement.Soldiers, outgoing, definition.Combat!.WoundedPercent, settlement.Wounded, MilitaryRulebook.WoundedCapacity);
                int morale = MoraleLoss(settlement.Soldiers, outgoing, settlement.Morale);
                world.Replace(settlement with { Soldiers = loss.RemainingSoldiers, Wounded = settlement.Wounded + loss.AddedWounded,
                    Morale = settlement.Morale - morale, Durability = settlement.Durability - walls });
                events.Add(new BattleDamageEvent(world.Clock, id, null, settlement.Id, outgoing, loss.AddedWounded, loss.Dead, walls, morale));
                DamageUnit(world, attacker, incoming, formation.Combat!, settlement.Id, events, attackerIsSettlement: true);
                if (Defeated(world.Units[id])) BattleAftermath.TerminateUnit(world, id, null, rules, events);
                else if (loss.RemainingSoldiers == 0 || settlement.Morale == morale || settlement.Durability == walls)
                    BattleAftermath.CaptureSettlement(world, settlement.Id, id, rules, events);
            }
            BattleAftermath.Reconcile(world, affectedForces, rules, events);
            ResolutionPipeline.CheckConquest(world, events);
        }
    }

    private static bool Defeated(UnitState unit) => unit.Soldiers == 0 || unit.Morale == 0;
    private static int MoraleLoss(uint before, uint loss, int morale) => loss == 0 ? 0 :
        Math.Min(morale, (int)(((ulong)loss * 100 + before - 1) / before));
    private static void DamageUnit(WorldMutation world, UnitState unit, uint damage, CombatProfile profile, int attacker, List<DomainEvent> events,
        bool attackerIsSettlement = false)
    {
        var loss = CombatMath.Loss(unit.Soldiers, damage, profile.WoundedPercent, unit.Wounded, MilitaryRulebook.WoundedCapacity);
        int morale = MoraleLoss(unit.Soldiers, damage, unit.Morale);
        world.Replace(unit with { Soldiers = loss.RemainingSoldiers, Wounded = unit.Wounded + loss.AddedWounded, Morale = unit.Morale - morale });
        events.Add(new BattleDamageEvent(world.Clock, attacker, unit.Id, null, damage, loss.AddedWounded, loss.Dead, 0, morale, attackerIsSettlement));
    }
    private static CombatStrength Strength(WorldMutation world, UnitState unit) => new(unit.Soldiers, unit.Morale,
        Leadership(world, p => p.UnitId == unit.Id), 1, unit.Condition is UnitCondition.Confused or UnitCondition.Routed ? 5 : 10);
    private static CombatStrength GarrisonStrength(WorldMutation world, SettlementState settlement) => new(settlement.Soldiers,
        settlement.Morale, Leadership(world, p => p.SettlementId == settlement.Id), 1, 10);
    private static int Leadership(WorldMutation world, Func<OfficerState, bool> location) => world.Officers.Values
        .Where(p => p.IsServing && location(p) && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null))
        .Select(p => world.Abilities(p).Leadership).DefaultIfEmpty(0).Max();
}
