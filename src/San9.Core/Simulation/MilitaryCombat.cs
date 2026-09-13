using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record BattleDamageEvent(GameClock Clock, int AttackerId, int? TargetUnitId, int? SettlementId,
    uint SoldierLoss, uint AddedWounded, uint Dead, int DurabilityLoss, int MoraleLoss, bool AttackerIsSettlement = false) : DomainEvent;

internal static class MilitaryCombat
{
    internal static uint NormalDamage(WorldMutation world, BattleSource source, BattleSource target, MilitaryRulebook military)
    {
        CombatStrength StrengthOf(BattleSource actor, BattleSource opponent) =>
            (actor.IsSettlement ? GarrisonStrength(world, world.Settlements[actor.Id]) : Strength(world, world.Units[actor.Id]))
            with { Intensity = MilitaryEngagements.Intensity(world, actor, opponent, military) };
        CombatProfile Profile(BattleSource actor) => actor.IsSettlement ? military.Place(world.Settlements[actor.Id]).Combat! :
            military.CombatFormation(world.Units[actor.Id])!;
        return CombatMath.TroopDamage(StrengthOf(source, target), StrengthOf(target, source), Profile(source), Profile(target),
            target.IsSettlement, target.IsSettlement ? world.Settlements[target.Id].Durability : 0,
            source.IsSettlement && military.FacilityAttack(world.Settlements[source.Id]).UsesRockfallPower);
    }

    internal static bool ValidTarget(WorldMutation world, int owner, MilitaryOrder order, FormationRule formation, MilitaryRulebook rules)
    {
        if (formation.Combat is null || formation.Attack is null) return false;
        if (order.Kind == MilitaryOrderKind.AttackUnit)
            return order.UnitId is int id && world.Units.TryGetValue(id, out var unit) && MilitaryDiplomacy.CanFight(world, owner, unit.OwnerId) &&
                unit.FormationId is int f && rules.Formations[f].Combat is not null && rules.CanAttackTarget(formation.Attack, unit.Position, false, false);
        return order.Kind == MilitaryOrderKind.AttackSettlement && order.SettlementId is int place &&
            world.Settlements.TryGetValue(place, out var settlement) && !settlement.IsDestroyed && settlement.Facility?.BuilderUnitId is null &&
            MilitaryDiplomacy.CanFight(world, owner, settlement.OwnerId) && rules.TryPlace(settlement, out var definition) && definition.Combat is not null &&
            rules.CanAttackTarget(formation.Attack, definition.Position, true, rules.IsHarbor(settlement));
    }

    internal static int UnitRange(WorldMutation world, UnitState source, MilitaryOrder target, MilitaryRulebook rules, WorldPosition? position = null)
    {
        var targetPosition = target.UnitId is int id ? world.Units[id].Position : rules.Place(world.Settlements[target.SettlementId!.Value]).Position;
        return rules.AttackRange(rules.Formations[source.FormationId!.Value].Attack, position ?? source.Position, targetPosition,
            target.UnitId is null, target.SettlementId is int place && rules.IsHarbor(world.Settlements[place]));
    }

    internal static int FacilityRange(WorldMutation world, SettlementState source, UnitState target, MilitaryRulebook rules) =>
        rules.FacilityAttackRange(source, target.Position, world.Settlements.Values);

    internal static MapOccupancy TargetArea(WorldMutation world, MilitaryOrder order, MilitaryRulebook rules) =>
        order.UnitId is int id ? Area(world.Units[id], rules) :
        new(rules.Place(world.Settlements[order.SettlementId!.Value]).Position, rules.Place(world.Settlements[order.SettlementId.Value]).Footprint);

    internal static MapOccupancy Area(UnitState unit, MilitaryRulebook rules) => new(unit.Position, rules.Formations[unit.FormationId!.Value].Footprint);

    internal static int WallDamage(WorldMutation world, UnitState unit, SettlementState target, MilitaryRulebook rules)
    {
        var attack = rules.Formations[unit.FormationId!.Value].Attack;
        if (attack?.DamagesWalls != true || unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits)) return 0;
        bool close = Area(unit, rules).IntersectsExpanded(new BattleSource(target.Id, true).Area(world, rules), MapCoordinates.UnitsPerCell);
        if (!close && !attack.RangedSiege) return 0;
        int damage = CombatMath.DurabilityDamage(unit.Soldiers, Strength(world, unit).Leadership,
            rules.CombatFormation(unit)!.AgainstWalls, target.Durability);
        return close ? damage : Math.Min(damage, Math.Max(0, target.Durability - 1));
    }

    internal static int ApproachRange(WorldMutation world, UnitState source, MilitaryOrder target, MilitaryRulebook rules, WorldPosition position)
    {
        var formation = rules.Formations[source.FormationId!.Value];
        int reach = UnitRange(world, source, target, rules, position);
        if (formation.Attack?.RangedSiege != true) return reach;
        var area = new MapOccupancy(position, formation.Footprint);
        bool targetCanReply;
        if (target.SettlementId is int id)
        {
            var place = world.Settlements[id];
            if (place.Soldiers < 1000 || place.Durability < 20) return MapCoordinates.UnitsPerCell;
            targetCanReply = new BattleSource(id, true).Area(world, rules).IntersectsExpanded(area,
                FacilityRange(world, place, source with { Position = position }, rules));
        }
        else
        {
            var unit = world.Units[target.UnitId!.Value];
            int range = rules.AttackRange(rules.Formations[unit.FormationId!.Value].Attack, unit.Position, position, false, false);
            targetCanReply = Area(unit, rules).IntersectsExpanded(area, range);
        }
        return targetCanReply ? MapCoordinates.UnitsPerCell : reach;
    }

    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, List<DomainEvent> events)
    {
        if (rules.Military is not { } military) return;
        var used = new HashSet<int>();
        var buildingsUsed = new HashSet<int>();
        foreach (int id in world.Units.Keys.Order().ToArray())
        {
            if (world.Simulation.TerminalReason is not null) break;
            if (!world.Units.TryGetValue(id, out var attacker) || attacker.EffectiveOrder is not { IsAttack: true } order || attacker.Condition is UnitCondition.Confused or UnitCondition.Routed) continue;
            var formation = military.Formations[attacker.FormationId!.Value];
            if (!ValidTarget(world, attacker.OwnerId, order, formation, military))
            {
                world.Replace(attacker with { Order = attacker.EngagementTargetId is null ? null : attacker.Order,
                    AwaitingPostAction = attacker.EngagementTargetId is null, EngagementTargetId = null });
                if (attacker.EngagementTargetId is not null)
                    events.Add(new UnitEngagementChangedEvent(world.Clock, id, null, "attack-target-invalid"));
                else events.Add(new UnitOrderChangedEvent(world.Clock, id, null, "attack-target-invalid"));
                continue;
            }
            var targetArea = TargetArea(world, order, military);
            if (used.Contains(id) || !Area(attacker, military).IntersectsExpanded(targetArea, UnitRange(world, attacker, order, military))) continue;
            used.Add(id);
            MilitaryDiplomacy.CombatOpened(world, attacker.OwnerId, MilitaryDiplomacy.TargetOwner(world, order),
                order.SettlementId is int attackedPlace && MilitaryDiplomacy.Work(world, attackedPlace, military), events);
            var affectedForces = new HashSet<int> { attacker.OwnerId };
            if (order.UnitId is int targetId)
            {
                var defender = world.Units[targetId];
                affectedForces.Add(defender.OwnerId);
                var defense = military.Formations[defender.FormationId!.Value];
                uint outgoing = NormalDamage(world, new(id, false), new(targetId, false), military);
                uint incoming = 0;
                if (!used.Contains(targetId) && defender.Condition is not (UnitCondition.Confused or UnitCondition.Routed) && defense.Attack is not null &&
                    targetArea.IntersectsExpanded(Area(attacker, military), UnitRange(world, defender, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id }, military)))
                {
                    used.Add(targetId);
                    incoming = NormalDamage(world, new(targetId, false), new(id, false), military);
                }
                DamageUnit(world, defender, outgoing, military.CombatFormation(defender)!, id, events);
                DamageUnit(world, attacker, incoming, military.CombatFormation(attacker)!, targetId, events);
                bool attackerLost = Defeated(world.Units[id]), defenderLost = Defeated(world.Units[targetId]);
                if (defenderLost) BattleAftermath.TerminateUnit(world, targetId, attackerLost ? null : new BattleSource(id, false), rules, events);
                if (attackerLost) BattleAftermath.TerminateUnit(world, id, defenderLost ? null : new BattleSource(targetId, false), rules, events);
                if (!attackerLost && !defenderLost)
                {
                    TacticCombat.TryUnit(world, id, order, rules, events);
                    TacticCombat.TryUnit(world, targetId, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id }, rules, events, counter: true);
                }
            }
            else
            {
                var settlement = world.Settlements[order.SettlementId!.Value];
                if (settlement.OwnerId is int defenderOwner) affectedForces.Add(defenderOwner);
                var definition = military.Place(settlement);
                var capability = military.FacilityAttack(settlement);
                var defense = GarrisonStrength(world, settlement);
                uint outgoing = NormalDamage(world, new(id, false), new(settlement.Id, true), military);
                int walls = WallDamage(world, attacker, settlement, military);
                uint incoming = 0;
                bool retaliated = false;
                if (settlement.OwnerId is not null && settlement.Morale > 0 && settlement.ConfusionDaysRemaining == 0 && capability.Attack is { } retaliation &&
                    !buildingsUsed.Contains(settlement.Id) && targetArea.IntersectsExpanded(Area(attacker, military), FacilityRange(world, settlement, attacker, military)))
                {
                    buildingsUsed.Add(settlement.Id);
                    retaliated = true;
                    incoming = NormalDamage(world, new(settlement.Id, true), new(id, false), military);
                }
                DamageSettlement(world, settlement, outgoing, definition.Combat!, id, events, walls);
                DamageUnit(world, attacker, incoming, military.CombatFormation(attacker)!, settlement.Id, events, attackerIsSettlement: true);
                if (walls > 0 && world.Settlements[settlement.Id].Durability > 0) BattleLearning.Siege(world, id, events);
                if (walls > 0 && !Defeated(world.Units[id]) && !Defeated(world.Settlements[settlement.Id]) &&
                    definition.FacilityTypeId is int type && military.Facilities[type].Reprisal is { } reprisal)
                    FacilityCombat.ApplyReprisal(world, world.Settlements[settlement.Id], world.Units[id], reprisal, military.CombatFormation(attacker)!, events);
                if (Defeated(world.Units[id])) BattleAftermath.TerminateUnit(world, id,
                    Defeated(world.Settlements[settlement.Id]) || !settlement.CanHostPersons ? null : new BattleSource(settlement.Id, true), rules, events);
                else if (Defeated(world.Settlements[settlement.Id]))
                    BattleAftermath.ResolveSettlementDefeat(world, settlement.Id, new(id, false), rules, events);
                TacticCombat.TryUnit(world, id, order, rules, events);
                if (retaliated) FacilityCombat.TryTactic(world, settlement.Id,
                    new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = id }, rules, events);
                CityBattleEffects.CompleteAttack(world, id, settlement.Id, settlement.OwnerId, events);
            }
            BattleAftermath.Reconcile(world, affectedForces, rules, events);
            ResolutionPipeline.CheckConquest(world, events);
        }
        FacilityCombat.AdvanceDay(world, rules, used, buildingsUsed, events);
    }

    private static bool Defeated(UnitState unit) => unit.Soldiers == 0 || unit.Morale == 0;
    internal static bool Defeated(SettlementState settlement) => !settlement.IsDestroyed && settlement.Facility?.BuilderUnitId is null &&
        (settlement.Durability == 0 || settlement.CanHostPersons && (settlement.Soldiers == 0 || settlement.Morale == 0));
    internal static void DamageSettlement(WorldMutation world, SettlementState settlement, uint damage, CombatProfile profile,
        int attacker, List<DomainEvent> events, int walls = 0, bool attackerIsSettlement = false)
    {
        damage = FinalDamage(world, new(attacker, attackerIsSettlement), settlement.OwnerId, damage, settlement.Soldiers);
        var loss = CombatMath.Loss(settlement.Soldiers, damage, profile.WoundedPercent, settlement.Wounded, MilitaryRulebook.WoundedCapacity);
        int morale = 0;
        world.Replace(settlement with { Soldiers = loss.RemainingSoldiers, Wounded = settlement.Wounded + loss.AddedWounded,
            Morale = settlement.Morale - morale, Durability = settlement.Durability - walls });
        events.Add(new BattleDamageEvent(world.Clock, attacker, null, settlement.Id, settlement.Soldiers - loss.RemainingSoldiers,
            loss.AddedWounded, loss.Dead, walls, morale, attackerIsSettlement));
    }
    internal static void DamageUnit(WorldMutation world, UnitState unit, uint damage, CombatProfile profile, int attacker, List<DomainEvent> events,
        bool attackerIsSettlement = false)
    {
        damage = FinalDamage(world, new(attacker, attackerIsSettlement), unit.OwnerId, damage, unit.Soldiers);
        int morale = 0;
        ApplyUnitLoss(world, unit, damage, morale, profile, new(attacker, attackerIsSettlement), events);
    }
    internal static void ApplyUnitLoss(WorldMutation world, UnitState unit, uint damage, int morale, CombatProfile profile,
        BattleSource source, List<DomainEvent> events)
    {
        damage = Math.Min(damage, unit.Soldiers);
        var loss = CombatMath.Loss(unit.Soldiers, damage, profile.WoundedPercent, unit.Wounded, MilitaryRulebook.WoundedCapacity);
        world.Replace(unit with { Soldiers = loss.RemainingSoldiers, Wounded = unit.Wounded + loss.AddedWounded, Morale = unit.Morale - morale });
        events.Add(new BattleDamageEvent(world.Clock, source.Id, unit.Id, null, damage, loss.AddedWounded, loss.Dead, 0, morale, source.IsSettlement));
    }
    private static uint FinalDamage(WorldMutation world, BattleSource source, int? targetOwner, uint damage, uint soldiers)
    {
        int? sourceOwner = source.IsSettlement ? world.Settlements[source.Id].OwnerId : world.Units[source.Id].OwnerId;
        bool Player(int? owner) => owner is int id && world.Simulation.Controls.Any(c => c.ForceId == id && c.Kind == ControllerKind.Player);
        int bonus = !Player(sourceOwner) && Player(targetOwner) ? world.Simulation.ComputerDamageBonusPercent : 0;
        return CombatMath.AdjustComputerDamage(damage, soldiers, bonus);
    }
    internal static CombatStrength Strength(WorldMutation world, UnitState unit) => new(unit.Soldiers, unit.Morale,
        Leadership(world, p => p.UnitId == unit.Id), 1, unit.Condition switch { UnitCondition.Confused => 8, UnitCondition.Routed => 6, _ => 10 });
    internal static CombatStrength GarrisonStrength(WorldMutation world, SettlementState settlement) => new(settlement.Soldiers,
        settlement.Morale, Leadership(world, p => p.SettlementId == settlement.Id), 1, settlement.ConfusionDaysRemaining > 0 ? 8 : 10);
    private static int Leadership(WorldMutation world, Func<OfficerState, bool> location) => world.Officers.Values
        .Where(p => p.IsServing && location(p) && !world.Simulation.Tasks.Any(t => t.OfficerId == p.Id && t.Journey is not null))
        .Select(p => world.Abilities(p).Leadership).DefaultIfEmpty(0).Max();
}
