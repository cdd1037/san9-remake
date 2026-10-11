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
            if (settlementsUsed.Contains(id) || !Active(settlement) || !military.TryPlace(settlement, out var place)) continue;
            var target = MilitaryEngagements.FacilityTarget(world, settlement, military);
            settlementsUsed.Add(id);
            if (target is null) { TryTactic(world, id, null, rules, events); continue; }
            var source = new BattleSource(id, true);
            var area = source.Area(world, military);
            var formation = military.Formation(target);
            MilitaryDiplomacy.CombatOpened(world, settlement.OwnerId!.Value, target.OwnerId, false, events);
            uint outgoing = MilitaryCombat.NormalDamage(world, source, new(target.Id, false), military);
            uint incoming = 0;
            int walls = 0;
            if (!unitsUsed.Contains(target.Id) && target.Condition is not (UnitCondition.Confused or UnitCondition.Routed) && formation.Attack is { } retaliation &&
                MilitaryCombat.Area(target, military).IntersectsExpanded(area, MilitaryCombat.UnitRange(world, target, new(MilitaryOrderKind.AttackSettlement, null, id), military)))
            {
                unitsUsed.Add(target.Id);
                incoming = MilitaryCombat.NormalDamage(world, new(target.Id, false), source, military);
                walls = MilitaryCombat.WallDamage(world, target, settlement, military);
            }
            MilitaryCombat.DamageUnit(world, target, outgoing, military.CombatFormation(target)!, id, events, true);
            MilitaryCombat.DamageSettlement(world, settlement, incoming, place.Combat!, target.Id, events, walls);
            if (walls > 0 && world.Settlements[id].Durability > 0) BattleLearning.Siege(world, target.Id, events);
            bool unitLost = world.Units[target.Id].Soldiers == 0 || world.Units[target.Id].Morale == 0;
            bool settlementLost = MilitaryCombat.Defeated(world.Settlements[id]);
            if (unitLost) BattleAftermath.TerminateUnit(world, target.Id, settlementLost ? null : source, rules, events);
            if (!unitLost && settlementLost) BattleAftermath.ResolveSettlementDefeat(world, id, new(target.Id, false), rules, events);
            if (!unitLost && !settlementLost)
            {
                CombatExchangeContinuation CaptureExchange(bool counterPending) => new(xunStarted ?? world.Clock,
                    new([], unitsUsed.Order().ToImmutableArray(), settlementsUsed.Order().ToImmutableArray())
                        { RemainingBuildings = buildings.Skip(index + 1).ToImmutableArray() },
                    source, new(target.Id, false), new[] { settlement.OwnerId!.Value, target.OwnerId }.Distinct().Order().ToImmutableArray(), counterPending);
                TryTactic(world, id, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = target.Id }, rules, events);
                if (world.Simulation.PendingSniping is not null && SnipingSuccession.CaptureExchange(world, CaptureExchange(true))) return;
                TacticCombat.TryUnit(world, target.Id, new(MilitaryOrderKind.AttackSettlement, null, id), rules, events, true);
                if (world.Simulation.PendingSniping is not null && SnipingSuccession.CaptureExchange(world, CaptureExchange(false))) return;
            }
            BattleAftermath.Reconcile(world, new HashSet<int> { settlement.OwnerId!.Value, target.OwnerId }, rules, events, world.CurrentDateCommandsConsumed);
            ResolutionPipeline.CheckConquest(world, events);
        }
    }

    internal static void TryTactic(WorldMutation world, int settlementId, MilitaryOrder? target, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        var settlement = world.Settlements[settlementId];
        if (!Active(settlement) || !military.TryPlace(settlement, out var place) || place.FacilityTypeId is not int typeId ||
            military.Facilities[typeId].Tactics is not { } profile) return;
        var selected = military.FacilityTactic(settlement);
        if (selected is null) return;
        var source = new BattleSource(settlementId, true);
        if (selected.Effect is TacticEffect.Heal or TacticEffect.Inspire)
        {
            var targets = TacticSupport.Targets(world, source, selected, military);
            if (targets.Length == 0) return;
            var supportActor = TacticActor(world, settlementId);
            if (supportActor is null || !TacticCombat.Roll(world, "military-facility-tactic-opportunity", profile.OpportunityChance)) return;
            TacticSupport.Resolve(world, source, supportActor, selected, targets,
                IntelligenceTactics.Defenders(world, new(MilitaryOrderKind.AttackSettlement, null, settlementId)), military, events);
            return;
        }
        if (target?.UnitId is not int targetId || !world.Units.TryGetValue(targetId, out var unit) ||
            !MilitaryDiplomacy.CanFight(world, settlement.OwnerId!.Value, unit.OwnerId) || unit.FormationId is not int f ||
            military.Formation(unit).Combat is null || place.Attack is null ||
            !source.Area(world, military).IntersectsExpanded(MilitaryCombat.Area(unit, military), MilitaryCombat.FacilityRange(world, settlement, unit, military))) return;
        var tactic = selected.Effect == TacticEffect.TrapSelection ? military.TrapAt(unit.Position) : selected;
        if (tactic is null) return;
        var actor = TacticActor(world, settlementId);
        if (actor is null || !TacticCombat.Roll(world, "military-facility-tactic-opportunity", profile.OpportunityChance)) return;
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
            if (effect == 0)
            {
                events.Add(new IntelligenceEffectResolvedEvent(world.Clock, source, targetId, false, tactic.Id, 0)
                { ActorId = actor.Id, IsAttempt = true, Occurrence = TacticOccurrence.Capture(world, source, target, tactic, military, rules.Treasures) });
                return;
            }
        }
        var members = tactic.Effect == TacticEffect.Martial
            ? IntelligenceTactics.Defenders(world, new(MilitaryOrderKind.AttackSettlement, null, settlementId)) : [];
        world.Replace(world.Officers[actor.Id] with { TacticCooldown = tactic.Effect == TacticEffect.Martial ? profile.MartialCooldown : tactic.MinimumInterval });
        events.Add(new TacticActivatedEvent(world.Clock, settlementId, true, targetId, false, tactic.Id, [actor.Id], doubled, effect)
        { Occurrence = TacticOccurrence.Capture(world, source, target, tactic, military, rules.Treasures) });
        int? captured = null;
        var sniping = tactic.Effect == TacticEffect.Martial
            ? TacticSniping.Select(world, source, tactic, target, [actor.Id], effect, military) : null;
        if (tactic.Effect == TacticEffect.Martial)
            captured = TacticCombat.ResolveMartial(world, source, actor.Id, tactic, target, [new(tactic.Power, proficiency, world.Abilities(actor).War, false)], doubled, effect, rules, events);
        else IntelligenceTactics.Resolve(world, source, actor, tactic, target, effect, rules, events);
        if (!TacticSniping.Apply(world, sniping, !world.Units.ContainsKey(targetId), rules, events))
        {
            SnipingSuccession.CaptureTactic(world, new(source, target, selected.Id, selected.Id,
                members.ToImmutableArray(), [actor.Id]));
            return;
        }
        if (tactic.Effect == TacticEffect.Martial)
        {
            if (world.Units.ContainsKey(targetId)) BattleLearning.Learn(world, IntelligenceTactics.Defenders(world, target), selected, events);
            TacticCombat.Grow(world, members, [actor.Id], selected, events);
        }
        if (captured is int capturedId) BattleAftermath.ResolveSettlementDefeat(world, capturedId, source, rules, events);
    }

    private static OfficerState? TacticActor(WorldMutation world, int settlementId)
    {
        var actor = LeadershipMaintenance.Commander(world, settlementId);
        return actor is { Injury: 0, TacticCooldown: 0 } ? actor : null;
    }
}
