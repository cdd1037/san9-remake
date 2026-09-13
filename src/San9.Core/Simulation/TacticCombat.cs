using System.Collections.Immutable;
using San9.Core.Domain;

namespace San9.Core.Simulation;

public sealed record TacticActivatedEvent(GameClock Clock, int SourceId, bool SourceIsSettlement, int TargetId,
    bool TargetIsSettlement, int TacticId, ImmutableArray<int> OfficerIds, bool Doubled, int Effect) : DomainEvent;
public sealed record TacticProficiencyGrownEvent(GameClock Clock, int OfficerId, int Category, int Previous, int Current) : DomainEvent;
public sealed record TacticLearnedEvent(GameClock Clock, int OfficerId, int TacticId) : DomainEvent;

internal static class TacticCombat
{
    private sealed record Participant(OfficerState Officer, TacticRule Tactic);

    internal static void TryUnit(WorldMutation world, int sourceId, MilitaryOrder target, SimulationRules rules,
        List<DomainEvent> events, bool counter = false)
    {
        var military = rules.Military!;
        if (!world.Units.TryGetValue(sourceId, out var unit) || unit.Soldiers == 0 || unit.Morale == 0 ||
            unit.Condition is UnitCondition.Confused or UnitCondition.Routed || unit.IsEmbedded || unit.IsPersonnel ||
            unit.FormationId is not int formationId || military.Formations[formationId] is not { Tactics: { } profile } formation ||
            !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military) ||
            MilitaryCombat.Area(unit, military).DistanceTo(MilitaryCombat.TargetArea(world, target, military)) > MilitaryCombat.UnitRange(world, unit, target, military)) return;
        var members = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
            .OrderBy(p => unit.MemberRanks.GetValueOrDefault(p.Id, BattleRank.Middle)).ThenBy(p => p.Id).ToArray();
        var candidates = members.Where(p => p.Injury == 0 && p.TacticCooldown == 0)
            .Select(p => new { Officer = p, Tactic = ChooseTactic(world, p, unit.OwnerId, formation, target, rules) })
            .Where(p => p.Tactic is not null).Select(p => new Participant(p.Officer, p.Tactic!)).ToArray();
        if (candidates.Length == 0 || counter && !Roll(world, "military-tactic-counter", profile.CounterChance)) return;
        int cityBonus = formation.TravelMode == TravelMode.Land && military.HasCityInfluence(unit.OwnerId, unit.Position, world.Settlements.Values)
            ? military.CityInfluence!.LandTacticBonus : 0;
        int chance = Math.Min(100, profile.OpportunityChance + cityBonus + (unit.Condition == UnitCondition.HighSpirits ? 5 : 0));
        if (!Roll(world, "military-tactic-opportunity", chance)) return;
        Participant? primary = null;
        int intelligenceEffect = 0;
        var selection = !profile.AutomaticTactics.IsEmpty
            ? new[] { candidates[(int)(world.NextRandom("military-tactic-member") % (uint)candidates.Length)] } : candidates;
        foreach (var candidate in selection)
        {
            if (profile.AutomaticTactics.IsEmpty && !Roll(world, "military-tactic-member", AllocationChance(unit.MemberRanks[candidate.Officer.Id],
                members.Length, candidate.Tactic.Effect == TacticEffect.Martial && profile.FavoredCategories.Contains(candidate.Tactic.Category!.Value)))) continue;
            if (candidate.Tactic.Effect != TacticEffect.Martial)
            {
                intelligenceEffect = IntelligenceTactics.Effect(world, candidate.Officer, candidate.Tactic, target, rules);
                if (intelligenceEffect == 0) continue;
            }
            primary = candidate;
            break;
        }
        if (primary is null) return;
        if (primary.Tactic.Effect != TacticEffect.Martial)
        {
            bool canStartChain = primary.Tactic.Effect is TacticEffect.Trap or TacticEffect.Defection ||
                primary.Tactic.Effect == TacticEffect.Confusion &&
                (target.UnitId is int previousTarget ? world.Units[previousTarget].Condition is not (UnitCondition.Confused or UnitCondition.Routed) :
                    world.Settlements[target.SettlementId!.Value].ConfusionDaysRemaining == 0);
            world.Replace(world.Officers[primary.Officer.Id] with { TacticCooldown = primary.Tactic.MinimumInterval });
            events.Add(new TacticActivatedEvent(world.Clock, unit.Id, false, target.UnitId ?? target.SettlementId!.Value,
                target.UnitId is null, primary.Tactic.Id, [primary.Officer.Id], false, intelligenceEffect));
            IntelligenceTactics.Resolve(world, new(unit.Id, false), primary.Officer, primary.Tactic, target, intelligenceEffect, rules, events);
            var learning = primary.Officer.EquippedTacticId is int selected && military.Tactics[selected].Effect == TacticEffect.TrapSelection
                ? military.Tactics[selected] : primary.Tactic;
            Grow(world, members, [primary.Officer.Id], learning, events);
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
            unit.Soldiers == 0 || unit.Morale == 0 || unit.Condition is UnitCondition.Confused or UnitCondition.Routed ||
            unit.FormationId is not int formationId || military.Formations[formationId] is not { Tactics: { } profile } formation ||
            formation.TravelMode != TravelMode.Land || !MilitaryCombat.ValidTarget(world, unit.OwnerId, target, formation, military)) return;
        bool triggered = target.UnitId is int id
            ? world.Units[id].Soldiers > 0 && world.Units[id].Morale > 0 && world.Units[id].Condition ==
                (original.Tactic.Effect == TacticEffect.Defection ? UnitCondition.NoFormation : UnitCondition.Confused)
            : original.Tactic.Effect != TacticEffect.Defection && world.Settlements[target.SettlementId!.Value] is var place &&
                place.Soldiers > 0 && place.Morale > 0 && place.ConfusionDaysRemaining > 0;
        if (!triggered) return;
        var members = world.Officers.Values.Where(p => p.IsServing && p.OwnerId == unit.OwnerId && p.UnitId == unit.Id)
            .OrderBy(p => p.Id).ToArray();
        var candidates = members.Where(p => p.Id != original.Officer.Id && p.Injury == 0 && p.TacticCooldown == 0)
            .Select(p => new { Officer = p, Tactic = ChooseTactic(world, p, unit.OwnerId, formation, target, rules, equippedOnly: true) })
            .Where(p => p.Tactic?.Effect == TacticEffect.Martial).Select(p => new Participant(p.Officer, p.Tactic!)).ToArray();
        if (candidates.Length == 0) return;
        var primary = candidates[(int)(world.NextRandom("military-tactic-chain-member") % (uint)candidates.Length)];
        if (!Roll(world, "military-tactic-chain-allocation", AllocationChance(unit.MemberRanks[primary.Officer.Id], members.Length,
            profile.FavoredCategories.Contains(primary.Tactic.Category!.Value)))) return;
        ActivateMartial(world, unit, members, candidates, primary, target, rules, events);
    }

    private static void ActivateMartial(WorldMutation world, UnitState unit, OfficerState[] members, Participant[] candidates,
        Participant primary, MilitaryOrder target, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        var profile = military.Formations[unit.FormationId!.Value].Tactics!;
        List<Participant> participants = [primary];
        foreach (var other in candidates.Where(p => p.Officer.Id != primary.Officer.Id && p.Tactic.Effect == TacticEffect.Martial && p.Tactic.Category == primary.Tactic.Category))
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
            target.UnitId is null, primary.Tactic.Id, participants.Select(p => p.Officer.Id).ToImmutableArray(), doubled, effect));
        var damageMembers = participants.Select(p => new MartialTacticMember(p.Tactic.Power,
            p.Officer.TacticProficiencies[category], world.Abilities(p.Officer).War, p.Officer.Id != primary.Officer.Id)).ToArray();
        ResolveMartial(world, new(unit.Id, false), target, damageMembers, doubled, effect, rules, events);
        Grow(world, members, participants.Select(p => p.Officer.Id).ToArray(), primary.Tactic, events);
    }

    internal static void ResolveMartial(WorldMutation world, BattleSource source, MilitaryOrder target,
        MartialTacticMember[] members, bool doubled, int effect, SimulationRules rules, List<DomainEvent> events)
    {
        var military = rules.Military!;
        if (target.UnitId is int targetId)
        {
            var defender = world.Units[targetId];
            var defense = military.CombatFormation(defender)!;
            uint ordinary = MilitaryCombat.NormalDamage(world, source, new(targetId, false), military);
            uint damage = TacticMath.MartialDamage(members, doubled, ordinary, effect,
                defender.Condition is UnitCondition.NoFormation or UnitCondition.Confused or UnitCondition.Routed, defender.Soldiers);
            MilitaryCombat.DamageUnit(world, defender, damage, defense, source.Id, events, source.IsSettlement);
            var current = world.Units[targetId];
            if (current.Soldiers == 0 || current.Morale == 0) BattleAftermath.TerminateUnit(world, targetId, source, rules, events);
        }
        else
        {
            var defender = world.Settlements[target.SettlementId!.Value];
            var defense = military.Place(defender).Combat!;
            uint ordinary = MilitaryCombat.NormalDamage(world, source, new(defender.Id, true), military);
            uint damage = TacticMath.MartialDamage(members, doubled, ordinary, effect, false, defender.Soldiers);
            MilitaryCombat.DamageSettlement(world, defender, damage, defense, source.Id, events, attackerIsSettlement: source.IsSettlement);
            var current = world.Settlements[defender.Id];
            if (MilitaryCombat.Defeated(current)) BattleAftermath.ResolveSettlementDefeat(world, defender.Id, source, rules, events);
        }
    }

    internal static int MartialEffect(WorldMutation world, OfficerState actor, TacticRule tactic,
        MilitaryOrder target, MilitaryRulebook military)
    {
        int category = tactic.Category!.Value;
        if (target.UnitId is int id && world.Units[id].FormationId is int formation &&
            military.Formations[formation].MartialTacticVulnerabilities.Contains(category)) return 15;
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

    internal static int AllocationChance(BattleRank rank, int members, bool favored)
    {
        int count = members switch { 1 => 20, 2 => 14, 3 => 10, 4 => 9, >= 5 => 8, _ => throw new ArgumentOutOfRangeException(nameof(members)) };
        int position = rank switch { BattleRank.Front => 5, BattleRank.Middle => 4, BattleRank.Rear => 3, _ => throw new ArgumentException("Unknown battle rank.") };
        return position * count * (favored ? 2 : 1) * 5 / 8;
    }

    internal static bool Roll(WorldMutation world, string stream, int chance) =>
        chance > 0 && (chance >= 100 || world.NextRandom(stream) % 100 < (uint)chance);

    internal static void Grow(WorldMutation world, OfficerState[] members, IReadOnlyList<int> participants, TacticRule tactic, List<DomainEvent> events)
    {
        int category = tactic.Category!.Value;
        foreach (var original in members.OrderBy(p => p.Id))
        {
            OfficerTacticGrowth.Increase(world, original.Id, category, 3, events);
            if (tactic.Selectable) OfficerTacticGrowth.TryLearn(world, original.Id, tactic, "military-tactic-learning", events);
        }
        var actor = world.Officers[participants[(int)(world.NextRandom("military-tactic-growth-member") % (uint)participants.Count)]];
        bool martial = tactic.Effect == TacticEffect.Martial;
        int ability = martial ? actor.BaseAbilities.War : actor.BaseAbilities.Intelligence;
        if (ability < 100 && Roll(world, "military-tactic-growth", 3))
        {
            world.Replace(actor with { BaseAbilities = martial ? actor.BaseAbilities with { War = ability + 1 } : actor.BaseAbilities with { Intelligence = ability + 1 } });
            events.Add(new OfficerAbilityGrownEvent(world.Clock, actor.Id, martial ? "war" : "intelligence", ability, ability + 1));
        }
    }
}
