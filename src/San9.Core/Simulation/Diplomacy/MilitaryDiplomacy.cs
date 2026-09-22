using San9.Core.Domain;

namespace San9.Core.Simulation;

[Flags]
public enum AttackWarning { None = 0, FriendlyRelations = 1, AllianceBetrayal = 2, AttackRequestBreach = 4 }
public sealed record WarDeclaredEvent(GameClock Clock, int AttackerForceId, int DefenderForceId, bool BetrayedAlliance) : DomainEvent;

internal static class MilitaryDiplomacy
{
    internal static int Relation(WorldMutation world, int first, int second) =>
        world.Relations.TryGetValue((Math.Min(first, second), Math.Max(first, second)), out var relation) ? relation.Value : 0;

    internal static bool CanFight(WorldMutation world, int attacker, int? defender) =>
        world.Forces.TryGetValue(attacker, out var force) && !force.IsEliminated && attacker != defender &&
        (defender is null || world.Forces.TryGetValue(defender.Value, out var other) && !other.IsEliminated);

    internal static bool IsHostileRelation(int value) => value < 40;

    internal static bool CanUseHostileStrategy(WorldMutation world, int source, int target) =>
        CanFight(world, source, target) && world.Simulation.Alliance?.Protects(source, target) != true &&
        !AttackRequests.Protects(world, source, target);

    internal static bool AutomaticEnemy(WorldMutation world, int attacker, int? defender) =>
        CanFight(world, attacker, defender) && (defender is null ||
            world.Simulation.Alliance?.Protects(attacker, defender.Value) != true && !AttackRequests.Protects(world, attacker, defender.Value) &&
            IsHostileRelation(Relation(world, attacker, defender.Value)));

    internal static bool Work(WorldMutation world, int settlementId, MilitaryRulebook rules) =>
        rules.Place(world.Settlements[settlementId]).FacilityTypeId is int type && rules.Facilities[type].IsWork;

    internal static int? TargetOwner(WorldMutation world, MilitaryOrder? order) => order?.Kind switch
    {
        MilitaryOrderKind.AttackUnit when order.UnitId is int id && world.Units.TryGetValue(id, out var unit) => unit.OwnerId,
        MilitaryOrderKind.AttackSettlement when order.SettlementId is int id && world.Settlements.TryGetValue(id, out var place) => place.OwnerId,
        _ => null
    };

    internal static AttackWarning Warning(WorldMutation world, int force, MilitaryOrder? order, MilitaryRulebook? rules)
    {
        if (rules is null || order?.IsAttack != true || TargetOwner(world, order) is not int other || !CanFight(world, force, other) ||
            order.SettlementId is int place && Work(world, place, rules)) return AttackWarning.None;
        return (Relation(world, force, other) >= 60 ? AttackWarning.FriendlyRelations : AttackWarning.None) |
            (world.Simulation.Alliance?.Protects(force, other) == true ? AttackWarning.AllianceBetrayal : AttackWarning.None) |
            (AttackRequests.Protects(world, force, other) ? AttackWarning.AttackRequestBreach : AttackWarning.None);
    }

    internal static void BeginAdvance(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        foreach (var unit in world.Units.Values.OrderBy(u => u.Id).ToArray())
        {
            if (unit.IsEmbedded || unit.IsPersonnel || unit.Order is not { Kind: MilitaryOrderKind.AttackSettlement, SettlementId: int id } order ||
                unit.FormationId is not int formation || !MilitaryCombat.ValidTarget(world, unit.OwnerId, order, military.Formations[formation], military) ||
                Work(world, id, military) || world.Settlements[id].OwnerId is not int other) continue;
            int value = Relation(world, unit.OwnerId, other);
            bool betrayal = world.Simulation.Alliance?.Protects(unit.OwnerId, other) == true;
            if (value >= 40) ForceRelations.Set(world, unit.OwnerId, other, 35, events);
            if (value >= 60) Penalize(world, unit.OwnerId, value >= 80 ? 20 : 10, value >= 80 ? 1 : 2, false, events);
            if (betrayal)
            {
                var alliance = world.Simulation.Alliance!;
                Penalize(world, unit.OwnerId, 50, 1, true, events);
                foreach (int member in alliance.MemberForceIds.Where(f => f != unit.OwnerId))
                {
                    int relation = Relation(world, unit.OwnerId, member);
                    if (relation > 0) ForceRelations.Set(world, unit.OwnerId, member, Math.Min(35, Math.Max(0, relation - 30)), events);
                }
                AllianceDiplomacy.Betray(world, unit.OwnerId, events);
            }
            bool brokenRequest = AttackRequests.BreakPair(world, unit.OwnerId, other, events);
            if (value >= 40 || betrayal || brokenRequest) events.Add(new WarDeclaredEvent(world.Clock, unit.OwnerId, other, betrayal));
        }
    }

    private static void Penalize(WorldMutation world, int forceId, int trustLoss, int divisor, bool excludeRuler, List<DomainEvent> events)
    {
        var force = world.Forces[forceId];
        world.Replace(force with { BaseTrust = Math.Max(0, force.BaseTrust - trustLoss) });
        foreach (var person in world.Officers.Values.Where(p => p.OwnerId == forceId && p.IsServing &&
            (!excludeRuler || p.Status != OfficerStatus.Ruler)).OrderBy(p => p.Id).ToArray())
            RecruitmentRules.SetLoyalty(world, person.Id, forceId, Math.Max(0, person.Loyalty - person.Righteousness / divisor), "forced-attack", events);
    }

    internal static void CombatOpened(WorldMutation world, int attacker, int? defender, bool work, List<DomainEvent> events)
    {
        if (work || defender is not int other || !CanFight(world, attacker, other)) return;
        int value = Relation(world, attacker, other);
        if (value >= 20) ForceRelations.Set(world, attacker, other, value >= 40 ? 39 : value - 1, events);
    }
}
