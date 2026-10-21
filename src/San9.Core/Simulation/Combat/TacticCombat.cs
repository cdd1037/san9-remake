using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticParticipant(int OfficerId, int TacticId);

public sealed record TacticActivatedEvent(GameClock Clock, int SourceId, bool SourceIsSettlement, int TargetId,
    bool TargetIsSettlement, int TacticId, ImmutableArray<TacticParticipant> Participants, bool Doubled, int Effect) : DomainEvent
{
    public TacticOccurrence? Occurrence { get; init; }
    public bool IsChain { get; init; }
}
public sealed record TacticProficiencyGrownEvent(GameClock Clock, int OfficerId, int Category, int Previous, int Current) : DomainEvent;
public enum TacticLearningOrigin { Grant, Practice }
public sealed record TacticLearnedEvent(GameClock Clock, int OfficerId, int TacticId) : DomainEvent
{
    [System.Text.Json.Serialization.JsonRequired] public TacticLearningOrigin Origin { get; init; }
    [System.Text.Json.Serialization.JsonRequired] public PersonOccurrence Occurrence { get; init; } = null!;
}

internal static class TacticCombat
{
    private sealed record Participant(OfficerState Officer, TacticRule Tactic);

    internal static void TryUnit(WorldMutation world, int sourceId, MilitaryOrder target, SimulationRules rules,
        List<DomainEvent> events, bool counter = false)
    {
        if (PrepareOpportunity(world, sourceId, target, rules, counter) == true)
            ResolveOpportunity(world, sourceId, target, rules, events);
    }

    // The ordinary tactic roll precedes the independent duel attempt. Passive equipment is eligible here.
    internal static bool? PrepareOpportunity(WorldMutation world, int sourceId, MilitaryOrder target, SimulationRules rules, bool counter)
    {
        var military = rules.Military!;
        if (!world.Units.TryGetValue(sourceId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0 ||
            military.TacticFormation(unit) is not { Tactics: { } profile } formation ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military) ||
            !MilitaryCombat.Area(unit, military).IntersectsExpanded(MilitaryCombat.TargetArea(world, target, military), MilitaryCombat.UnitRange(world, unit, target, military))) return null;
        if (!world.ServingUnitMembers(sourceId).Any(p => p.OwnerId == unit.OwnerId &&
            p.Injury == 0 && p.TacticCooldown == 0 && !p.Acted)) return null;
        if (counter && (unit.Condition is not (UnitCondition.Normal or UnitCondition.HighSpirits) ||
            !Roll(world, "military-tactic-counter", military.DefensiveTacticChance))) return null;
        int cityBonus = formation.TravelMode == TravelMode.Land && military.HasCityInfluence(unit.OwnerId, unit.Position, world.Settlements.Values)
            ? military.CityInfluence!.LandTacticBonus : 0;
        return Roll(world, "military-tactic-opportunity", Math.Min(100, profile.OpportunityChance + cityBonus +
            (unit.Condition == UnitCondition.HighSpirits ? 5 : 0)));
    }

    internal static void ResolveOpportunity(WorldMutation world, int sourceId, MilitaryOrder target, SimulationRules rules,
        List<DomainEvent> events)
    {
        var military = rules.Military!;
        if (!world.Units.TryGetValue(sourceId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0 ||
            military.TacticFormation(unit) is not { Tactics: { } profile } formation ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military) ||
            !MilitaryCombat.Area(unit, military).IntersectsExpanded(MilitaryCombat.TargetArea(world, target, military), MilitaryCombat.UnitRange(world, unit, target, military))) return;
        if (world.Simulation.TerminalReason is not null) return;
        var members = IntelligenceTactics.Defenders(world, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id });
        var candidates = members.OrderBy(p => military.Rank(unit, p.Id)).ThenBy(p => p.Id)
            .Where(p => p.Injury == 0 && p.TacticCooldown == 0 && !p.Acted)
            .Select(p => new { Officer = p, Tactic = ChooseTactic(world, p, unit.OwnerId, formation, target, rules) })
            .Where(p => p.Tactic is not null).Select(p => new Participant(p.Officer, p.Tactic!)).ToArray();
        if (candidates.Length == 0) return;
        Participant? primary = null;
        int intelligenceEffect = 0;
        var selection = !profile.AutomaticTactics.IsEmpty
            ? new[] { candidates.OrderBy(p => p.Officer.Id).ElementAt((int)(world.NextRandom("military-tactic-member") % (uint)candidates.Length)) } : candidates;
        foreach (var candidate in selection)
        {
            if (profile.AutomaticTactics.IsEmpty && !Roll(world, "military-tactic-member", TacticMath.AllocationChance(military.Rank(unit, candidate.Officer.Id),
                members.Length, candidate.Tactic.Effect == TacticEffect.Martial && profile.FavoredCategories.Contains(candidate.Tactic.Category!.Value)))) continue;
            if (candidate.Tactic.Effect != TacticEffect.Martial)
            {
                intelligenceEffect = IntelligenceTactics.Effect(world, candidate.Officer, candidate.Tactic, target, rules);
                if (intelligenceEffect == 0)
                {
                    events.Add(new IntelligenceEffectResolvedEvent(world.Clock, new(unit.Id, false),
                        target.UnitId ?? target.SettlementId!.Value, target.UnitId is null, candidate.Tactic.Id, 0)
                    { ActorId = candidate.Officer.Id, IsAttempt = true, Occurrence = TacticOccurrence.Capture(world, new(unit.Id, false), target, candidate.Tactic, military, rules.Treasures) });
                    continue;
                }
            }
            primary = candidate;
            break;
        }
        if (primary is null) return;
        if (primary.Tactic.Effect != TacticEffect.Martial)
        {
            bool canStartChain = primary.Tactic.Effect is TacticEffect.Trap or TacticEffect.Defection or TacticEffect.Illusion ||
                primary.Tactic.Effect == TacticEffect.Confusion &&
                (target.UnitId is int previousTarget ? world.Units[previousTarget].Condition is not (UnitCondition.Confused or UnitCondition.Routed) :
                    world.Settlements[target.SettlementId!.Value].ConfusionDaysRemaining == 0);
            world.Replace(world.Officers[primary.Officer.Id] with { TacticCooldown = primary.Tactic.MinimumInterval });
            events.Add(new TacticActivatedEvent(world.Clock, unit.Id, false, target.UnitId ?? target.SettlementId!.Value,
                target.UnitId is null, primary.Tactic.Id, [new(primary.Officer.Id, primary.Tactic.Id)], false, intelligenceEffect)
            { Occurrence = TacticOccurrence.Capture(world, new(unit.Id, false), target, primary.Tactic, military, rules.Treasures) });
            IntelligenceTactics.Resolve(world, new(unit.Id, false), primary.Officer, primary.Tactic, target, intelligenceEffect, rules, events);
            if (canStartChain) TryMartialChain(world, unit.Id, primary, target, rules, events);
            return;
        }
        ActivateMartial(world, unit, members, candidates, primary, target, rules, events);
    }

    private static void TryMartialChain(WorldMutation world, int sourceId, Participant original, MilitaryOrder target,
        SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        if (world.Simulation.TerminalReason is not null || !world.Units.TryGetValue(sourceId, out var unit) ||
            unit.Soldiers == 0 || unit.Morale == 0 || military.TacticFormation(unit) is not { Tactics: { } profile } formation ||
            formation.TravelMode != TravelMode.Land || !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military)) return;
        bool triggered = target.UnitId is int id
            ? world.Units[id].Soldiers > 0 && world.Units[id].Morale > 0 && world.Units[id].Condition ==
                (original.Tactic.Effect == TacticEffect.Defection ? UnitCondition.NoFormation : UnitCondition.Confused)
            : original.Tactic.Effect != TacticEffect.Defection && world.Settlements[target.SettlementId!.Value] is var place &&
                place.Soldiers > 0 && place.Morale > 0 && place.ConfusionDaysRemaining > 0;
        if (!triggered) return;
        var members = IntelligenceTactics.Defenders(world, new(MilitaryOrderKind.AttackUnit, null, null) { UnitId = unit.Id });
        var candidates = members.Where(p => p.Id != original.Officer.Id && p.Injury == 0 && p.TacticCooldown == 0)
            .Select(p => new { Officer = p, Tactic = ChooseTactic(world, p, unit.OwnerId, formation, target, rules, equippedOnly: true) })
            .Where(p => p.Tactic?.Effect == TacticEffect.Martial).Select(p => new Participant(p.Officer, p.Tactic!)).ToArray();
        if (candidates.Length == 0) return;
        var primary = candidates[(int)(world.NextRandom("military-tactic-chain-member") % (uint)candidates.Length)];
        if (!Roll(world, "military-tactic-chain-allocation", TacticMath.AllocationChance(military.Rank(unit, primary.Officer.Id), members.Length,
            profile.FavoredCategories.Contains(primary.Tactic.Category!.Value)))) return;
        ActivateMartial(world, unit, members, candidates, primary, target, rules, events, isChain: true);
    }

    private static void ActivateMartial(WorldMutation world, UnitState unit, OfficerState[] members, Participant[] candidates,
        Participant primary, MilitaryOrder target, SimulationRules rules, List<DomainEvent> events, bool isChain = false)
    {
        var military = rules.Military!;
        var profile = military.Formation(unit).Tactics!;
        List<Participant> participants = [primary];
        foreach (var other in candidates.OrderBy(p => p.Officer.Id).Where(p => p.Officer.Id != primary.Officer.Id && p.Tactic.Effect == TacticEffect.Martial && p.Tactic.Category == primary.Tactic.Category))
        {
            int linkChance = military.LinkRules.Chance(primary.Officer, other.Officer, profile);
            if (Roll(world, "military-tactic-link", linkChance)) participants.Add(other);
        }
        int category = primary.Tactic.Category!.Value;
        int proficiency = primary.Officer.TacticProficiencies[category];
        int effect = MartialEffect(world, primary.Officer, primary.Tactic, target, military);
        bool doubled = Roll(world, "military-tactic-double", 1 + proficiency / 100);
        foreach (var participant in participants)
            world.Replace(world.Officers[participant.Officer.Id] with
            { TacticCooldown = participant.Officer.Id == primary.Officer.Id ? primary.Tactic.MinimumInterval : participant.Tactic.MinimumInterval / 3 });
        events.Add(new TacticActivatedEvent(world.Clock, unit.Id, false, target.UnitId ?? target.SettlementId!.Value,
            target.UnitId is null, primary.Tactic.Id, participants.Select(p => new TacticParticipant(p.Officer.Id, p.Tactic.Id)).ToImmutableArray(), doubled, effect)
        { Occurrence = TacticOccurrence.Capture(world, new(unit.Id, false), target, primary.Tactic, military, rules.Treasures), IsChain = isChain });
        var damageMembers = participants.Select(p => new MartialTacticMember(p.Tactic.Power,
            p.Officer.TacticProficiencies[category], world.Abilities(p.Officer).War, p.Officer.Id != primary.Officer.Id)).ToArray();
        var sniping = TacticSniping.Select(world, new(unit.Id, false), primary.Tactic, target,
            participants.Select(p => p.Officer.Id), effect, military);
        int? captured = ResolveMartial(world, new(unit.Id, false), primary.Officer.Id, primary.Tactic, target, damageMembers, doubled, effect, rules, events);
        var learning = primary.Tactic.Selectable ? primary.Tactic :
            military.Formation(unit).AutomaticTacticLearningId is int related ? military.Tactics[related] : null;
        if (captured is null && !TacticSniping.Apply(world, sniping,
            target.UnitId is int defeated && !world.Units.ContainsKey(defeated), rules, events))
        {
            SnipingSuccession.CaptureTactic(world, new(new(unit.Id, false), target, primary.Tactic.Id, learning?.Id,
                members.ToImmutableArray(), participants.Select(p => p.Officer.Id).ToImmutableArray()));
            return;
        }
        if (target.UnitId is int survived ? world.Units.ContainsKey(survived) : captured is null)
        {
            if (learning is not null) BattleLearning.Learn(world, IntelligenceTactics.Defenders(world, target), learning, events);
        }
        Grow(world, members, participants.Select(p => p.Officer.Id).ToArray(), primary.Tactic, events, learning);
        if (captured is int settlementId)
        {
            BattleAftermath.ResolveSettlementDefeat(world, settlementId, new(unit.Id, false), rules, events);
            TacticSniping.Apply(world, sniping, true, rules, events);
        }
    }

    internal static void ResumeAfterSniping(WorldMutation world, TacticAftermathContinuation pending,
        SimulationRules rules, List<DomainEvent> events)
    {
        var tactic = rules.Military!.Tactics[pending.TacticId];
        var learning = pending.LearningTacticId is int id ? rules.Military.Tactics[id] : null;
        bool survived = pending.Target.UnitId is int unit ? world.Units.ContainsKey(unit) :
            pending.Target.SettlementId is int place && !world.Settlements[place].IsDestroyed;
        if (survived && learning is not null)
            BattleLearning.Learn(world, IntelligenceTactics.Defenders(world, pending.Target), learning, events);
        Grow(world, pending.Members.ToArray(), pending.Participants, tactic, events, pending.Source.IsSettlement ? null : learning);
    }

    // Returns a defeated settlement for the caller to capture after tactic growth, before source absorption.
    internal static int? ResolveMartial(WorldMutation world, BattleSource source, int actorId, TacticRule tactic, MilitaryOrder target,
        MartialTacticMember[] members, bool doubled, int effect, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        int? rescued = TacticRescue.Select(world, source, tactic, target, effect, military);
        if (rescued is int selected)
            events.Add(new TacticPrisonerRescuedEvent(world.Clock, source, actorId, tactic.Id, target.UnitId!.Value, selected));
        if (target.UnitId is int targetId)
        {
            var defender = world.Units[targetId];
            var defense = military.CombatFormation(defender)!;
            uint ordinary = MilitaryCombat.NormalDamage(world, source, new(targetId, false), military);
            var damage = TacticMath.MartialDamage(members, doubled, ordinary, effect,
                defender.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed, defender.Soldiers);
            MilitaryCombat.DamageUnit(world, defender, damage.Applied, defense, source.Id, events, source.IsSettlement, tactic.Id, damage.Calculated);
            var current = world.Units[targetId];
            if (current.Soldiers == 0 || current.Morale == 0)
                BattleAftermath.TerminateUnit(world, targetId, source, rules, events,
                    doubled ? UnitDefeatCause.DoubledMartialTactic : UnitDefeatCause.Other);
            else if (rescued is int prisonerId)
                TacticRescue.Release(world, targetId, prisonerId, rules, events);
        }
        else
        {
            var defender = world.Settlements[target.SettlementId!.Value];
            var defense = military.Place(defender).Combat!;
            uint ordinary = MilitaryCombat.NormalDamage(world, source, new(defender.Id, true), military);
            var damage = TacticMath.MartialDamage(members, doubled, ordinary, effect, false, defender.Soldiers);
            MilitaryCombat.DamageSettlement(world, defender, damage.Applied, defense, source.Id, events, attackerIsSettlement: source.IsSettlement,
                tacticId: tactic.Id, calculatedSoldierLoss: damage.Calculated);
            var current = world.Settlements[defender.Id];
            if (MilitaryCombat.Defeated(current)) return defender.Id;
        }
        return null;
    }

    internal static int MartialEffect(WorldMutation world, OfficerState actor, TacticRule tactic,
        MilitaryOrder target, MilitaryRulebook military)
    {
        int category = tactic.Category!.Value;
        if (target.UnitId is int id && world.Units[id].FormationId is int formation &&
            military.Formation(world.Units[id]).MartialTacticVulnerabilities.Contains(category)) return 15;
        int resistance = Math.Max(0, (IntelligenceTactics.Defenders(world, target)
            .Select(p => p.TacticProficiencies[category]).DefaultIfEmpty(0).Max() - actor.TacticProficiencies[category]) / 20);
        return Roll(world, "military-tactic-resistance", resistance) ? 1 : 10;
    }

    private static TacticRule? ChooseTactic(WorldMutation world, OfficerState officer, int owner, FormationRule formation,
        MilitaryOrder target, SimulationRules rules, bool equippedOnly = false)
    {
        var military = rules.Military!;
        bool automatic = !equippedOnly && !formation.Tactics!.AutomaticTactics.IsEmpty;
        var ids = !automatic ? officer.EquippedTacticId is int selected ? new[] { selected } : [] :
            formation.Tactics!.AutomaticTactics.ToArray();
        var available = ids.Where(id => automatic ||
            MilitaryTactics.HasTactic(world, owner, [officer.Id], id, rules.Treasures)).Select(id => military.Tactics[id])
            .Where(t => t.CompatibleFormations.Contains(formation.Id) && (target.UnitId is not null || t.AffectsFacilities));
        var position = target.UnitId is int unitId ? world.Units[unitId].Position : military.Place(world.Settlements[target.SettlementId!.Value]).Position;
        return available.Select(t => t.Effect == TacticEffect.TrapSelection ? military.TrapAt(position) : t)
            .OfType<TacticRule>().OrderByDescending(t => t.Power).ThenBy(t => t.Id).FirstOrDefault(t => (t.Effect == TacticEffect.Martial || IntelligenceTactics.IsOffensive(t.Effect)) &&
            t.CompatibleFormations.Contains(formation.Id) && (target.UnitId is not null || t.AffectsFacilities) &&
            t.Category is not null && (t.ContactRange is not int range || target.UnitId is not null &&
                MilitaryCombat.Area(world.Units[officer.UnitId!.Value], military).IntersectsExpanded(MilitaryCombat.TargetArea(world, target, military), range)));
    }

    internal static bool Roll(WorldMutation world, string stream, int chance) =>
        chance > 0 && (chance >= 100 || world.NextRandom(stream) % 100 < (uint)chance);

    internal static void Grow(WorldMutation world, OfficerState[] members, IReadOnlyList<int> participants, TacticRule tactic, List<DomainEvent> events, TacticRule? learning = null)
    {
        members = members.Where(p => world.Officers.TryGetValue(p.Id, out var current) && current.IsServing &&
            current.OwnerId == p.OwnerId && current.UnitId == p.UnitId && current.SettlementId == p.SettlementId).OrderBy(p => p.Id).ToArray();
        participants = participants.Where(id => members.Any(p => p.Id == id)).Distinct().Order().ToArray();
        if (participants.Count == 0) return;
        int category = tactic.Category!.Value;
        var actor = world.Officers[participants[(int)(world.NextRandom("military-tactic-growth-member") % (uint)participants.Count)]];
        OfficerAbilityGrowth.TryIncrease(world, actor.Id,
            category is 7 or 8 ? OfficerAbility.Intelligence : OfficerAbility.War,
            "military-tactic-growth", events);
        foreach (var original in members)
            OfficerTacticGrowth.Increase(world, original.Id, category, 3, events);
        if (learning is not null || tactic.Selectable) BattleLearning.Learn(world, members, learning ?? tactic, events);
    }
}
