using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class FacilityCombat
{
    internal static void ApplyReprisal(WorldMutation world, SettlementState source, UnitState target,
        FacilityReprisal reprisal, CombatProfile defense, List<DomainEvent> events)
    {
        var members = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == target.OwnerId && p.UnitId == target.Id).ToArray();
        int proficiency = members.Select(p => p.TacticProficiencies[reprisal.ProficiencyCategory]).DefaultIfEmpty(0).Max();
        MilitaryCombat.ApplyUnitLoss(world, target, reprisal.Damage(proficiency), 0, defense, new(source.Id, true), events);
        if (world.Units[target.Id].Soldiers == 0) return;
        int intelligence = members.Select(p => world.Abilities(p).Intelligence).DefaultIfEmpty(0).Max();
        if (TacticCombat.Roll(world, "military-facility-reprisal", reprisal.Chance(intelligence)))
            MilitaryConditions.Confuse(world, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id }, reprisal.ConfusionDays, events);
    }

    internal static bool Active(SettlementState settlement) => !settlement.IsDestroyed && settlement.CanHostPersons &&
        settlement.Facility?.BuilderUnitId is null && settlement.OwnerId is not null && settlement.Soldiers > 0 &&
        settlement.Durability > 0 && settlement.Morale > 0 && settlement.ConfusionDaysRemaining == 0;

    internal static void AdvanceDay(WorldMutation world, SimulationRules rules, HashSet<int> unitsUsed,
        HashSet<int> settlementsUsed, List<DomainEvent> events)
    {
        var military = rules.Military!;
        foreach (int id in world.Settlements.Keys.Order().ToArray())
        {
            if (world.Simulation.TerminalReason is not null) return;
            var settlement = world.Settlements[id];
            if (settlementsUsed.Contains(id) || !Active(settlement) || !military.TryPlace(settlement, out var place)) continue;
            var source = new BattleSource(id, true);
            var area = source.Area(world, military);
            var capability = military.FacilityAttack(settlement);
            var target = capability.Attack is { } attack ? world.Units.Values.Where(u => MilitaryCombat.Hostile(world, settlement.OwnerId!.Value, u.OwnerId) &&
                u.FormationId is int f && military.Formations[f].Combat is not null &&
                MilitaryCombat.Area(u, military).DistanceTo(area) <= MilitaryCombat.FacilityRange(world, settlement, u, military))
                .OrderBy(u => MilitaryCombat.Area(u, military).DistanceTo(area)).ThenBy(u => u.Id).FirstOrDefault() : null;
            settlementsUsed.Add(id);
            if (target is null) { TryTactic(world, id, null, rules, events); continue; }
            var formation = military.Formations[target.FormationId!.Value];
            uint outgoing = CombatMath.TroopDamage(MilitaryCombat.GarrisonStrength(world, settlement), MilitaryCombat.Strength(world, target),
                place.Combat!, formation.Combat!, false, 0, capability.UsesRockfallPower);
            uint incoming = 0;
            int walls = 0;
            if (!unitsUsed.Contains(target.Id) && target.Condition != UnitCondition.Confused && formation.Attack is { } retaliation &&
                MilitaryCombat.Area(target, military).DistanceTo(area) <= MilitaryCombat.UnitRange(world, target, new(MilitaryOrderKind.AttackSettlement, null, id), military))
            {
                unitsUsed.Add(target.Id);
                incoming = CombatMath.TroopDamage(MilitaryCombat.Strength(world, target), MilitaryCombat.GarrisonStrength(world, settlement),
                    formation.Combat!, place.Combat!, true, settlement.Durability);
                if (retaliation.DamagesWalls) walls = CombatMath.DurabilityDamage(target.Soldiers,
                    MilitaryCombat.Strength(world, target).Leadership, formation.Combat!.AgainstWalls, settlement.Durability);
            }
            MilitaryCombat.DamageUnit(world, target, outgoing, formation.Combat!, id, events, true);
            MilitaryCombat.DamageSettlement(world, settlement, incoming, place.Combat!, target.Id, events, walls);
            bool unitLost = world.Units[target.Id].Soldiers == 0 || world.Units[target.Id].Morale == 0;
            bool settlementLost = MilitaryCombat.Defeated(world.Settlements[id]);
            if (unitLost) BattleAftermath.TerminateUnit(world, target.Id, settlementLost ? null : source, rules, events);
            if (!unitLost && settlementLost) BattleAftermath.ResolveSettlementDefeat(world, id, new(target.Id, false), rules, events);
            if (!unitLost && !settlementLost)
            {
                TryTactic(world, id, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id }, rules, events);
                TacticCombat.TryUnit(world, target.Id, new(MilitaryOrderKind.AttackSettlement, null, id), rules, events, true);
            }
            BattleAftermath.Reconcile(world, new HashSet<int> { settlement.OwnerId!.Value, target.OwnerId }, rules, events);
            ResolutionPipeline.CheckConquest(world, events);
        }
    }

    internal static void TryTactic(WorldMutation world, int settlementId, MilitaryOrder? target, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        var settlement = world.Settlements[settlementId];
        if (!Active(settlement) || !military.TryPlace(settlement, out var place) || place.FacilityTypeId is not int typeId ||
            military.Facilities[typeId].Tactics is not { } profile) return;
        var actor = GovernanceMaintenance.Commander(world, settlementId, rules.Governance);
        if (actor is null || actor.Injury > 0 || actor.TacticCooldown > 0) return;
        var selected = military.FacilityTactic(settlement);
        if (selected is null) return;
        var source = new BattleSource(settlementId, true);
        var members = IntelligenceTactics.Defenders(world, new(MilitaryOrderKind.AttackSettlement, null, settlementId));
        if (selected.Effect is TacticEffect.Heal or TacticEffect.Inspire)
        {
            var targets = TacticSupport.Targets(world, source, selected, military);
            if (targets.Length > 0 && TacticCombat.Roll(world, "military-facility-tactic-opportunity", profile.OpportunityChance))
                TacticSupport.Resolve(world, source, actor, selected, targets, members, military, events);
            return;
        }
        if (target?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var unit) ||
            !MilitaryCombat.Hostile(world, settlement.OwnerId!.Value, unit.OwnerId) || unit.FormationId is not int f ||
            military.Formations[f].Combat is null || place.Attack is null ||
            source.Area(world, military).DistanceTo(MilitaryCombat.Area(unit, military)) > MilitaryCombat.FacilityRange(world, settlement, unit, military)) return;
        var tactic = selected.Effect == TacticEffect.TrapSelection ? military.TrapAt(unit.Position) : selected;
        if (tactic is null || !TacticCombat.Roll(world, "military-facility-tactic-opportunity", profile.OpportunityChance)) return;
        int proficiency = actor.TacticProficiencies[tactic.Category!.Value];
        int effect;
        bool doubled = false;
        if (tactic.Effect == TacticEffect.Martial)
        {
            effect = TacticCombat.MartialEffect(world, actor, tactic, target, military);
            doubled = TacticCombat.Roll(world, "military-tactic-double", 1 + 2 * (proficiency / 100));
        }
        else
        {
            effect = IntelligenceTactics.Effect(world, actor, tactic, target, rules);
            if (effect == 0) return;
        }
        world.Replace(world.Officers[actor.Id] with { TacticCooldown = tactic.Effect == TacticEffect.Martial ? profile.MartialCooldown : tactic.MinimumInterval });
        events.Add(new TacticActivatedEvent(world.Clock, settlementId, true, targetId, false, tactic.Id, [actor.Id], doubled, effect));
        if (tactic.Effect == TacticEffect.Martial)
            TacticCombat.ResolveMartial(world, source, target, [new(tactic.Power, proficiency, world.Abilities(actor).War, false)], doubled, effect, rules, events);
        else IntelligenceTactics.Resolve(world, source, actor, tactic, target, effect, rules, events);
        TacticCombat.Grow(world, members, [actor.Id], selected, events);
    }
}
