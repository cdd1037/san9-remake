using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

internal static class FacilityCombat
{
    internal static void ApplyReprisal(WorldMutation world, SettlementState source, UnitState target,
        FacilityReprisal reprisal, CombatProfile defense, List<DomainEvent> events)
    {
        var members = world.ServingUnitMembers(target.Id).Where(p => p.OwnerId == target.OwnerId).ToArray();
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
        HashSet<int> settlementsUsed, List<DomainEvent> events, GameClock? xunStarted = null,
        ImmutableArray<int>? remainingBuildings = null)
    {
        var military = rules.Military!;
        var buildings = remainingBuildings ?? world.Settlements.Keys.Order().ToImmutableArray();
        for (int index = 0; index < buildings.Length; index++)
        {
            int id = buildings[index];
            world.MaintainLeadership(events);
            if (world.Simulation.TerminalReason is not null) return;
            var settlement = world.Settlements[id];
            if (settlementsUsed.Contains(id) || !Active(settlement) || !military.TryPlace(settlement, out _)) continue;
            var target = MilitaryEngagements.FacilityTarget(world, settlement, military);
            settlementsUsed.Add(id);
            if (target is null) { TryTactic(world, id, null, rules, events); continue; }
            var source = new BattleSource(id, true);
            MilitaryEngagements.Open(world, source, new(target.Id, false), military);
            MilitaryDiplomacy.CombatOpened(world, settlement.OwnerId!.Value, target.OwnerId, false, events);
            CombatDayContinuation CaptureDay() => new([], unitsUsed.Order().ToImmutableArray(), settlementsUsed.Order().ToImmutableArray())
                { RemainingBuildings = buildings.Skip(index + 1).ToImmutableArray() };
            if (!MilitaryCombat.BeginExchange(world, source, new(target.Id, false), rules, events, xunStarted ?? world.Clock, CaptureDay)) return;
        }
    }

    internal static bool TryTactic(WorldMutation world, int settlementId, MilitaryOrder? target, SimulationRules rules,
        List<DomainEvent> events, bool counter = false, CombatExchangeDamage? damage = null)
    {
        var military = rules.Military!;
        var settlement = world.Settlements[settlementId];
        if (!Active(settlement) || !military.TryPlace(settlement, out var place) || place.FacilityTypeId is not int typeId ||
            military.Facilities[typeId].Tactics is not { } profile) return false;
        var selected = military.FacilityTactic(settlement);
        if (selected is null) return false;
        var source = new BattleSource(settlementId, true);
        if (selected.Effect is TacticEffect.Heal or TacticEffect.Inspire)
        {
            var targets = TacticSupport.Targets(world, source, selected, military);
            if (targets.Length == 0) return false;
            var supportActor = TacticActor(world, settlementId);
            if (supportActor is null || !PassesOpportunity(world, profile, military, counter)) return false;
            TacticSupport.Resolve(world, source, supportActor, selected, targets,
                IntelligenceTactics.Defenders(world, MilitaryCombat.TargetOrder(source)), military, events,
                target?.UnitId is int opponent ? new BattleSource(opponent, false) : null, damage);
            if (target?.UnitId is int victim)
                MilitaryCombat.CompleteLosses(world, source, new(victim, false), rules, events);
            return true;
        }
        if (target?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var unit) ||
            !MilitaryDiplomacy.CanFight(world, settlement.OwnerId!.Value, unit.OwnerId) || unit.FormationId is null ||
            military.Formation(unit).Combat is null || place.Attack is null ||
            !source.Area(world, military).IntersectsExpanded(MilitaryCombat.Area(unit, military), MilitaryCombat.FacilityRange(world, settlement, unit, military))) return false;
        var tactic = selected.Effect == TacticEffect.TrapSelection ? military.TrapAt(unit.Position) : selected;
        if (tactic is null) return false;
        var actor = TacticActor(world, settlementId);
        if (actor is null || !PassesOpportunity(world, profile, military, counter)) return false;
        if (tactic.Effect != TacticEffect.Martial)
        {
            int intelligenceEffect = IntelligenceTactics.Effect(world, actor, tactic, target, rules);
            TacticCombat.ActivateIntelligence(world, source, actor, tactic, target, intelligenceEffect, rules, events, damage);
            return true;
        }
        int proficiency = actor.TacticProficiencies[tactic.Category!.Value];
        int effect = TacticCombat.MartialEffect(world, actor, tactic, target, military);
        bool doubled = TacticCombat.Roll(world, "military-tactic-double", 1 + 2 * (proficiency / 100));
        var members = IntelligenceTactics.Defenders(world, MilitaryCombat.TargetOrder(source));
        world.Replace(world.Officers[actor.Id] with { TacticCooldown = profile.MartialCooldown });
        events.Add(new TacticActivatedEvent(world.Clock, settlementId, true, targetId, false, tactic.Id, [new(actor.Id, tactic.Id)], doubled, effect)
        { Occurrence = TacticOccurrence.Capture(world, source, target, tactic, military, rules.Treasures) });
        var sniping = TacticSniping.Select(world, source, tactic, target, [actor.Id], effect, military);
        MilitaryCombat.CommitOrdinary(world, source, new(targetId, false), damage, military, events);
        TacticCombat.ResolveMartial(world, source, actor.Id, tactic, target,
            [new(tactic.Power, proficiency, world.Abilities(actor).War, false)], doubled, effect, rules, events);
        if (!TacticSniping.Apply(world, sniping, !world.Units.ContainsKey(targetId), rules, events))
        {
            SnipingSuccession.CaptureTactic(world, new(source, target, selected.Id, selected.Id,
                members.ToImmutableArray(), [actor.Id]));
            return true;
        }
        if (world.Units.ContainsKey(targetId)) BattleLearning.Learn(world, IntelligenceTactics.Defenders(world, target), selected, events);
        TacticCombat.Grow(world, members, [actor.Id], selected, events);
        return true;
    }

    // Garrison defensive opportunities use the global entrance before their authored base chance.
    private static bool PassesOpportunity(WorldMutation world, FacilityTactics profile, MilitaryRulebook military, bool counter) =>
        (!counter || TacticCombat.Roll(world, "military-tactic-counter", military.DefensiveTacticChance)) &&
        TacticCombat.Roll(world, "military-facility-tactic-opportunity", profile.OpportunityChance);

    private static OfficerState? TacticActor(WorldMutation world, int settlementId)
    {
        var actor = LeadershipMaintenance.Commander(world, settlementId);
        return actor is { Injury: 0, TacticCooldown: 0 } ? actor : null;
    }
}
